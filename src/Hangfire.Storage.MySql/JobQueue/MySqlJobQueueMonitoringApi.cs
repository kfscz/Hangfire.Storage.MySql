using System.Data;
using System.Diagnostics;

namespace Hangfire.Storage.MySql.JobQueue;

class MySqlJobQueueMonitoringApi(
    MySqlStorage storage, MySqlStorageOptions storageOptions
  ) : IPersistentJobQueueMonitoringApi
{

  static readonly TimeSpan QueuesCacheTimeout = TimeSpan.FromSeconds(5.0);

  readonly object _cacheLock = new();
  readonly MySqlStorage _storage = storage 
    ?? throw new ArgumentNullException(nameof(storage));
  readonly MySqlStorageOptions _storageOptions = storageOptions
    ?? throw new ArgumentNullException(nameof(storageOptions));
  List<string> _queuesCache = [];
  DateTime _cacheUpdated;

  public IEnumerable<string> GetQueues()
  {
    lock (_cacheLock)
    {
      if (_queuesCache.Count == 0 || DateTime.UtcNow - _cacheUpdated > QueuesCacheTimeout)
      {
        _queuesCache = _storage.UseConnection(SelectDistinctQueues);
        _cacheUpdated = DateTime.UtcNow;
      }
      return new List<string>(_queuesCache); // cloning
    }
  }

  List<string> SelectDistinctQueues(System.Data.IDbConnection connection)
  {
    using var command = connection.CreateCommand($"""
      SELECT DISTINCT(Queue) as Queue
      FROM `{_storageOptions.TablesPrefix}JobQueue`;
      """);
    using var reader = command.ExecuteReader();
    var result = new List<string>();
    while (reader.Read())
    {
      Debug.Assert(reader["Queue"] is string);
      if (reader["Queue"] is string s)
      {
        result.Add(s);
      }
    }
    return result;
  }

  public IEnumerable<int> GetEnqueuedJobIds(string queue, int @from, int perPage)
  {
    List<int> selectJobIds(System.Data.IDbConnection connection)
    {
      using var command = connection
        .CreateCommand($"""
          SET @rank = 0;
          SELECT r.JobId FROM (
            SELECT jq.JobId, @rank := @rank+1 AS `rank` 
            FROM `{_storageOptions.TablesPrefix}JobQueue` jq
            WHERE jq.Queue = @queue
            ORDER BY jq.Id
          ) AS r
          WHERE r.`rank` between @start and @end;
          """)
        .AddParameter("@queue", queue)
        .AddParameter("@start", @from + 1)
        .AddParameter("@end", @from + perPage);
      using var reader = command.ExecuteReader();
      var result = new List<int>();
      while (reader.Read())
      {
        result.Add(Convert.ToInt32(reader["JobId"]));
      }
      return result;
    }

    return _storage.UseConnection(selectJobIds);
  }

  public IEnumerable<int> GetFetchedJobIds(string queue, int @from, int perPage) => [];

  public EnqueuedAndFetchedCountDto GetEnqueuedAndFetchedCount(string queue)
  {
    EnqueuedAndFetchedCountDto selectCount(IDbConnection connection)
    {
      using var command = connection
        .CreateCommand($"""
          SELECT count(Id)
          FROM `{_storageOptions.TablesPrefix}JobQueue`
          WHERE Queue = @queue;
          """)
        .AddParameter("@queue", queue);
      var count = command.ExecuteScalar();
      return new () { EnqueuedCount = Convert.ToInt32(count) };
    }
    
    return _storage.UseConnection(selectCount);
  }
}