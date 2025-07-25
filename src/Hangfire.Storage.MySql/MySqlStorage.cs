using Hangfire.Annotations;
using Hangfire.Logging;
using Hangfire.Server;
using Hangfire.Storage.MySql.JobQueue;
using Hangfire.Storage.MySql.Locking;
using Hangfire.Storage.MySql.Monitoring;
using System;
using System.Collections.Generic;
using System.Data;

namespace Hangfire.Storage.MySql;

public class MySqlStorage : JobStorage, IDisposable
{
  //private static readonly ILog Logger = LogProvider.GetLogger(typeof(MySqlStorage));

  private readonly IDbConnector _connector;
  private readonly MySqlStorageOptions _storageOptions;

  public virtual PersistentJobQueueProviderCollection QueueProviders { get; private set; }

  public MySqlStorage(IDbConnector connector, MySqlStorageOptions storageOptions)
  {
    if (storageOptions == null) throw new ArgumentNullException(nameof(storageOptions));

    _connector = connector;
    _storageOptions = storageOptions;

    if (storageOptions.PrepareSchemaIfNecessary)
    {
      using (var connection = CreateAndOpenConnection())
      {
        MySqlObjectsInstaller.Install(connection, storageOptions.TablesPrefix);
        MySqlObjectsInstaller.Upgrade(connection, storageOptions.TablesPrefix);
      }
    }

    QueueProviders = new (new MySqlJobQueueProvider(this, _storageOptions));
  }

  public override IEnumerable<IServerComponent> GetComponents()
  {
    yield return new ExpirationManager(this, _storageOptions);
    yield return new CountersAggregator(this, _storageOptions);
  }

  public override void WriteOptionsToLog(ILog logger)
  {
    logger.Info("Using the following options for SQL Server job storage:");
    logger.InfoFormat("    Queue poll interval: {0}.", _storageOptions.QueuePollInterval);
  }

  public override IMonitoringApi GetMonitoringApi()
  {
    return new MySqlMonitoringApi(this, _storageOptions);
  }

  public override IStorageConnection GetConnection()
  {
    return new MySqlStorageConnection(this, _storageOptions);
  }

  internal void UseTransaction([InstantHandle] Action<IDbTransaction> action)
  {
    UseTransaction(connection =>
    {
      action(connection);
      return true;
    }, null);
  }

  internal T UseTransaction<T>(
      [InstantHandle] Func<IDbTransaction, T> func, IsolationLevel? isolationLevel)
  {
    var connection = CreateAndOpenConnection();
    try
    {
      using (var transaction = connection.BeginTransaction(
          isolationLevel ?? IsolationLevel.ReadCommitted))
      {
        var result = func(transaction);
        transaction.Commit();
        return result;
      }
    }
    finally
    {
      ReleaseConnection(connection);
    }
  }

  internal void UseConnection([InstantHandle] Action<IDbConnection> action)
  {
    UseConnection(connection =>
    {
      action(connection);
      return true;
    });
  }

  internal T UseConnection<T>([InstantHandle] Func<IDbConnection, T> func)
  {
    IDbConnection? connection = null;

    try
    {
      connection = CreateAndOpenConnection();
      return func(connection);
    }
    finally
    {
      ReleaseConnection(connection);
    }
  }

  internal IDbConnection CreateAndOpenConnection()
  {
    var connection = _connector.Connect();
    ResourceLock.ReleaseAll(connection);
    return connection;
  }

  internal void ReleaseConnection(IDbConnection? connection)
  {
    if (connection != null)
    {
      connection.Dispose();
    }
  }

  public void Dispose()
  {
  }
}

