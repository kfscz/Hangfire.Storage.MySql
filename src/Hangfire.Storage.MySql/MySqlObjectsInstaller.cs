using System.Data;
using Hangfire.Logging;
using System.Text;
using System.Xml.Linq;
using Hangfire.Storage.MySql.Locking;
using System.Diagnostics;

namespace Hangfire.Storage.MySql;

using Migration = (string Id, string Script);

class MySqlObjectsInstaller(
  IDbConnection connection, string? tablesPrefix, TimeSpan? migrationTimeout = null)
{

  const double DEFAULT_MIGRATION_TIMEOUT_MINUTES = 1.0;

  static readonly ILog Log = LogProvider.GetLogger(typeof(MySqlStorage));

  readonly TimeSpan _migrationTimeout = migrationTimeout ?? TimeSpan.FromMinutes(DEFAULT_MIGRATION_TIMEOUT_MINUTES);
  readonly IDbConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));
  readonly string _tablesPrefix = tablesPrefix ?? string.Empty;

  public void Install()
  {
    if (IsAlreadyInstalled())
    {
      Log.Info("DB tables already exist. Exit install");
    }
    else
    {
      Log.Info("Start installing Hangfire SQL objects...");
      string installScript = ReadInstallScriptFromTemplate();
      using (var command = _connection.CreateCommand(installScript))
      {
        command.ExecuteNonQuery();
      }
      Log.Info("Hangfire SQL objects installed.");
    }
  }

  public void Upgrade()
  {
    using (ResourceLock.AcquireOne(
        _connection, _tablesPrefix,
        _migrationTimeout, CancellationToken.None,
        LockableResource.Migration))
    {
      EnsureMigrationsTableExists();
      var appliedMigrations = ReadAppliedMigrations();
      var migrationDefinitions = ReadMigrationDefinitionsFromResources();
      var migrationsToApply = migrationDefinitions
        .Where(m => !appliedMigrations.Contains(m.Id));
      foreach (var migration in migrationsToApply)
      {
        ApplyMigration(migration);
      }
    }
  }

  private void EnsureMigrationsTableExists()
  {
    if (!_connection.TableExists(_tablesPrefix, "Migration"))
    {
      using var command = _connection.CreateCommand($"""
        /* Create migrations table */
        CREATE TABLE {_tablesPrefix}Migration (
          Id nvarchar(128) not null, 
          ExecutedAt datetime(6) not null, 
          primary key (`Id`)
        ) ENGINE = InnoDB default character set utf8 collate utf8_general_ci;
        """);
      command.ExecuteNonQuery();
    }
  }

  private HashSet<string> ReadAppliedMigrations()
  { 
    using var command = _connection
      .CreateCommand($"SELECT trim(Id) as Id FROM {_tablesPrefix}Migration;");
    using var reader = command.ExecuteReader();
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    while (reader.Read())
    {
      var id = reader["Id"] as string ?? string.Empty;
      Debug.Assert(!string.IsNullOrEmpty(id));
      Debug.Assert(!result.Contains(id));
      result.Add(id);
    }
    return result;
  }

  private void ApplyMigration(Migration migration)
  {
    // NOTE: Some operations cannot be executed in transactions (create table?)
    // we will need some mechanism to handle that if we need it
    using var transaction = _connection.BeginTransaction();
    using (var command = _connection
      .CreateCommand(migration.Script)
      .WithTransaction(transaction))
    {
      command.ExecuteNonQuery();
    }
    using (var command = _connection
      .CreateCommand($"""
          INSERT INTO `{_tablesPrefix}Migration` (Id, ExecutedAt)
          VALUES (TRIM(@id), @now);
          """)
      .WithTransaction(transaction)
      .AddParameter("@id", migration.Id)
      .AddParameter("@now", DateTime.UtcNow))
    {
      command.ExecuteNonQuery();
    }
    transaction.Commit();
  }

  private bool IsAlreadyInstalled()
    => _connection.TableExists(_tablesPrefix, "Job");

  private string TranslateScriptTemplateToScript(string scriptTemplate)
  {
    var sb = new StringBuilder(scriptTemplate);
    sb.Replace("[tablesPrefix]", _tablesPrefix);
    return sb.ToString();
  }

  private string ReadInstallScriptFromTemplate()
  {
    var scriptTemplate = ResourceHelper.GetStringResource("Install.sql");
    var script = TranslateScriptTemplateToScript(scriptTemplate);
    return script;
  }

  private IEnumerable<Migration> ReadMigrationDefinitionsFromResources()
  {
    var document = XElement.Parse(ResourceHelper.GetStringResource("Migrations.xml"));
    var migrations = document
        .Elements("migration")
        .Select(e => (
          Id: e.Attribute("id")?.Value?.Trim()
            ?? throw new InvalidOperationException("Missing migration Id"),
          Script: TranslateScriptTemplateToScript(e.Value)));
    return migrations;
  }

}