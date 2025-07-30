using System.Data;
using Hangfire.Logging;
using System.Text;
using System.Xml.Linq;
using Hangfire.Storage.MySql.Locking;
using System.Diagnostics;

namespace Hangfire.Storage.MySql;

using Migration = (string Id, string Script);

internal static class MySqlObjectsInstaller
{

  static readonly TimeSpan MigrationTimeout = TimeSpan.FromMinutes(1);
  static readonly ILog Log = LogProvider.GetLogger(typeof(MySqlStorage));

  public static void Install(IDbConnection connection, string? tablesPrefix = null)
  {
    if (connection is null) throw new ArgumentNullException(nameof(connection));
    var prefix = tablesPrefix ?? string.Empty;

    if (IsAlreadyInstalled(connection, prefix))
    {
      Log.Info("DB tables already exist. Exit install");
    }
    else
    {
      Log.Info("Start installing Hangfire SQL objects...");
      string installScript = ReadInstallScriptFromTemplate(prefix);
      using (var command = connection.CreateCommand(installScript))
      {
        command.ExecuteNonQuery();
      }
      Log.Info("Hangfire SQL objects installed.");
    }
  }

  public static void Upgrade(IDbConnection connection, string? tablesPrefix = null)
  {
    if (connection is null) throw new ArgumentNullException(nameof(connection));
    var prefix = tablesPrefix ?? string.Empty;

    using (ResourceLock.AcquireOne(
        connection, prefix,
        MigrationTimeout, CancellationToken.None,
        LockableResource.Migration))
    {
      EnsureMigrationsTable(connection, prefix);
      var appliedMigrations = ReadAppliedMigrations(connection, prefix);
      var migrationDefinitions = ReadMigrationDefinitionsFromResources(prefix);
      var migrationsToApply = migrationDefinitions
        .Where(m => !appliedMigrations.Contains(m.Id))
        .ToArray();
      foreach (var migration in migrationsToApply)
      {
        ApplyMigration(connection, migration.Script, prefix, migration.Id);
      }
    }
  }

  private static void EnsureMigrationsTable(IDbConnection connection, string prefix)
  {
    if (!connection.TableExists(prefix, "Migration"))
    {
      using var command = connection.CreateCommand($"""
        /* Create migrations table */
        CREATE TABLE {prefix}Migration (
          Id nvarchar(128) not null, 
          ExecutedAt datetime(6) not null, 
          primary key (`Id`)
        ) ENGINE = InnoDB default character set utf8 collate utf8_general_ci;
        """);
      command.ExecuteNonQuery();
    }
  }

  private static HashSet<string> ReadAppliedMigrations(
    IDbConnection connection, string prefix)
  { 
    using var command = connection
      .CreateCommand($"SELECT trim(Id) as Id FROM {prefix}Migration;");
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

  private static void ApplyMigration(
      IDbConnection connection, string script, string prefix, string id)
  {
    // NOTE: Some operations cannot be executed in transactions (create table?)
    // we will need some mechanism to handle that if we need it
    using var transaction = connection.BeginTransaction();
    using (var command = connection
      .CreateCommand(script)
      .WithTransaction(transaction))
    {
      command.ExecuteNonQuery();
    }
    using (var command = connection
      .CreateCommand("""
          INSERT INTO `{prefix}Migration` (Id, ExecutedAt)
          VALUES (TRIM(@id), @now);
          """)
      .WithTransaction(transaction)
      .AddParameter("@id", id)
      .AddParameter("@now", DateTime.UtcNow))
    {
      command.ExecuteNonQuery();
    }
    transaction.Commit();
  }

  private static bool IsAlreadyInstalled(IDbConnection connection, string tablesPrefix)
    => connection.TableExists(tablesPrefix, "Job");

  private static string ProcessTemplate(string script, string tablesPrefix)
  {
    var sb = new StringBuilder(script);
    sb.Replace("[tablesPrefix]", tablesPrefix);
    return sb.ToString();
  }

  private static string ReadInstallScriptFromTemplate(string prefix)
  {
    var scriptTemplate = ResourceHelper.GetStringResource("Install.sql");
    var script = ProcessTemplate(scriptTemplate, prefix);
    return script;
  }

  private static IEnumerable<Migration> ReadMigrationDefinitionsFromResources(string tablePrefix)
  {
    var document = XElement.Parse(ResourceHelper.GetStringResource("Migrations.xml"));
    var migrations = document
        .Elements("migration")
        .Select(e => (
          Id: e.Attribute("id")?.Value?.Trim()
            ?? throw new InvalidOperationException("Missing migration Id"),
          Script: ProcessTemplate(e.Value, tablePrefix)));
    return migrations;
  }

}