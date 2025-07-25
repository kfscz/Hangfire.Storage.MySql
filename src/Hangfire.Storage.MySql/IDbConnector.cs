using System;
using System.Data;
using System.Data.Common;

namespace Hangfire.Storage.MySql;

/// <summary>
/// Creates connection to database
/// </summary>
public interface IDbConnector
{
  /// <summary>
  /// Creates and open new connection to the database.
  /// Implemention should ensure that <c>Allow User Variables</c> is enabled in the connection string.
  /// </summary>
  IDbConnection Connect();
}

public class DbProviderFactoryConnector : IDbConnector
{

  readonly DbProviderFactory _factory;
  readonly string _connectionString;

  public DbProviderFactoryConnector(DbProviderFactory factory, string connectionString)
  {
    if (connectionString is null)
    {
      throw new ArgumentNullException(nameof(connectionString));
    }
    _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    var b = _factory.CreateConnectionStringBuilder()!;
    b.ConnectionString = connectionString;
    b["Allow User Variables"] = true;
    _connectionString = b.ToString();
  }

  public IDbConnection Connect()
  {
    var conn = _factory.CreateConnection()!;
    conn.ConnectionString = _connectionString;
    conn.Open();
    return conn;
  }
}