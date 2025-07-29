using System;
using System.Data;
using System.Globalization;
using Hangfire.Logging;
using Hangfire.Storage.MySql.Locking;

namespace Hangfire.Storage.MySql.JobQueue;

internal class MySqlFetchedJob : IFetchedJob
{
  static readonly ILog Logger = LogProvider.GetLogger(typeof(MySqlFetchedJob));

  readonly MySqlStorage _storage;
  readonly IDbConnection _connection;
  readonly MySqlStorageOptions _options;
  readonly int _id;
  bool _removed, _requeued, _disposed;
  readonly string _queue;

  public string JobId { get; }

  public MySqlFetchedJob(
    MySqlStorage storage,
    MySqlStorageOptions options,
    IDbConnection connection,
    FetchedJob fetchedJob)
  {
    if (fetchedJob is null)
    {
      throw new ArgumentNullException(nameof(fetchedJob));
    }
    _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _id = fetchedJob.Id;
    _queue = fetchedJob.Queue;
    JobId = fetchedJob.JobId.ToString(CultureInfo.InvariantCulture);
  }

  public void Dispose()
  {
    if (_disposed) return;
    if (!_removed && !_requeued)
    {
      Requeue();
    }
    _storage.ReleaseConnection(_connection);
    _disposed = true;
  }

  public void RemoveFromQueue()
  {
    Logger.TraceFormat("RemoveFromQueue JobId={0}", JobId);

    using (ResourceLock.AcquireOne(
      _connection, _options.TablesPrefix, LockableResource.Queue))
    {
      using var command = _connection
        .CreateCommand($"""
          DELETE FROM `{_options.TablesPrefix}JobQueue` 
          WHERE Id = @id;
          """)
        .AddParameter("@id", _id);
      command.ExecuteNonQuery();
    }

    _removed = true;
  }

  public void Requeue()
  {
    Logger.TraceFormat("Requeue JobId={0}", JobId);

    using (ResourceLock.AcquireOne(
      _connection, _options.TablesPrefix, LockableResource.Queue))
    {
      using var command = _connection
        .CreateCommand($"""
          UPDATE `{_options.TablesPrefix}JobQueue`
          SET FetchedAt = null
          WHERE Id = @id;
          """)
        .AddParameter("@id", _id);
      command.ExecuteNonQuery();
    }

    _requeued = true;
  }
}
