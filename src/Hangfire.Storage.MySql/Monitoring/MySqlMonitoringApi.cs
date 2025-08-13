using Dapper;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage.Monitoring;
using Hangfire.Storage.MySql.Entities;
using Hangfire.Storage.MySql.JobQueue;
using System.Data;
using System.Diagnostics;

namespace Hangfire.Storage.MySql.Monitoring;

class MySqlMonitoringApi(
    MySqlStorage storage, 
    MySqlStorageOptions storageOptions
  ) : IMonitoringApi
{

  readonly MySqlStorage _storage = storage 
    ?? throw new ArgumentNullException(nameof(storage));
  readonly MySqlStorageOptions _storageOptions = storageOptions 
    ?? throw new ArgumentNullException(nameof(storageOptions));

  public IList<QueueWithTopEnqueuedJobsDto> Queues()
  {
    var tuples = _storage.QueueProviders
        .Select(x => x.GetJobQueueMonitoringApi())
        .SelectMany(x => x.GetQueues(), (monitoring, queue) => (Monitoring: monitoring, Queue: queue))
        .OrderBy(x => x.Queue)
        .ToArray();
    var result = new List<QueueWithTopEnqueuedJobsDto>(tuples.Length);
    foreach (var (monitor, queue) in tuples)
    {
      var enqueuedJobIds = monitor.GetEnqueuedJobIds(queue, 0, 5);
      var counters = monitor.GetEnqueuedAndFetchedCount(queue);
      var firstJobs = UseConnection(c => EnqueuedJobs(c, enqueuedJobIds));
      result.Add(new()
      {
        Name = queue,
        Length = counters.EnqueuedCount ?? 0,
        Fetched = counters.FetchedCount,
        FirstJobs = firstJobs
      });
    }
    return result;
  }

  public IList<ServerDto> Servers() => UseConnection(SelectServers);

  private IList<ServerDto> SelectServers(IDbConnection connection)
  {
    using var command = connection.CreateCommand($"""
      SELECT Id, Data, LastHeartbeat
      FROM `{_storageOptions.TablesPrefix}Server`;
      """);
    using var reader = command.ExecuteReader();    
    var result = new List<ServerDto>(1);
    while (reader.Read())
    {
      string dataStr = reader.GetString("Data");
      var data = SerializationHelper.Deserialize<ServerData>(dataStr);
      result.Add(new () {
        Name = reader.GetString("Id"),
        Heartbeat = reader.GetNullableDateTime("LastHeartbeat"),
        Queues = data.Queues,
        StartedAt = data.StartedAt ?? DateTime.MinValue,
        WorkersCount = data.WorkerCount
      });
    }
    return result;
  }

  public JobDetailsDto? JobDetails(string jobId)
  {
    return UseConnection(connection =>
    {
      string sql = $@"/* select Job, Parameter & State for JobId */
                    select * from `{_storageOptions.TablesPrefix}Job` where Id = @id;
                    select * from `{_storageOptions.TablesPrefix}JobParameter` where JobId = @id;
                    select * from `{_storageOptions.TablesPrefix}State` where JobId = @id order by Id desc;";

      using (var multi = connection.QueryMultiple(sql, new { id = jobId }))
      {
        var job = multi.Read<SqlJob>().SingleOrDefault();
        if (job == null) return null;

        var parameters = multi.Read<JobParameter>().ToDictionary(x => x.Name, x => x.Value);
        var history =
                  multi.Read<SqlState>()
                      .ToList()
                      .Select(x => new StateHistoryDto
                    {
                      StateName = x.Name,
                      CreatedAt = x.CreatedAt,
                      Reason = x.Reason,
                      Data = new Dictionary<string, string>(
                              SerializationHelper.Deserialize<Dictionary<string, string>>(x.Data),
                              StringComparer.OrdinalIgnoreCase),
                    })
                      .ToList();

        return new JobDetailsDto
        {
          CreatedAt = job.CreatedAt,
          ExpireAt = job.ExpireAt,
          Job = DeserializeJob(job.InvocationData, job.Arguments),
          History = history,
          Properties = parameters
        };
      }
    });
  }

  public StatisticsDto GetStatistics()
  {
    var statistics = UseConnection(SelectStatistics);
    statistics.Queues = _storage.QueueProviders
      .SelectMany(x => x.GetJobQueueMonitoringApi().GetQueues())
      .Count();
    return statistics;
  }

  StatisticsDto SelectStatistics(IDbConnection connection)
  {
    const string statsSucceeded = "stats:succeeded", statsDeleted = "stats:deleted";
    string tablesPrefix = _storageOptions.TablesPrefix;
    using var command = connection.CreateCommand($"""
      /* statistics jobs */
      SELECT 
        j.StateName,
        COUNT(*) AS Count
      FROM `{tablesPrefix}Job` j
      WHERE j.StateName IN (
        '{JobStateValue.Enqueued}', '{JobStateValue.Failed}', 
        '{JobStateValue.Processing}', '{JobStateValue.Scheduled}')
      GROUP BY j.StateName;
      
      SELECT count(Id) AS Count
      FROM `{tablesPrefix}Server`;

      SELECT count(*) AS Count
      FROM `{tablesPrefix}Set`
      WHERE `Key` = 'recurring-jobs';

      SELECT 
        stats.`Key`, 
        SUM(stats.`Value`) AS `Value`
      FROM (
        SELECT 
          c.`Key`,
          sum(c.`Value`) as `Value`
        FROM `{tablesPrefix}Counter` c
        WHERE `Key` in ('{statsSucceeded}', '{statsDeleted}')
        GROUP BY `Key`
        UNION ALL SELECT 
          ac.`Key`,
          sum(ac.`Value`) AS `Value`
        FROM `{tablesPrefix}AggregatedCounter` ac
        WHERE ac.`Key` in ('{statsSucceeded}', '{statsDeleted}')
        GROUP BY ac.`Key`
      ) stats
      GROUP BY stats.`Key`;
      """);
    using var reader = command.ExecuteReader();

    var jobStatesCount = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    while (reader.Read())
    {
      var stateName = reader.GetString("StateName");
      var count = reader.GetLong("Count");
      jobStatesCount.Add(stateName, count);
    }

    reader.NextResult();
    long serverCount = reader.Read() ? reader.GetLong("Count") : 0L;

    reader.NextResult();
    long recurringCount = reader.Read() ? reader.GetLong("Count") : 0L;

    reader.NextResult();
    var statsCount = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    while (reader.Read())
    {
      var key = reader.GetString("Key");
      var value = reader.GetLong("Value");
      statsCount.Add(key, value);
    }
    return new () {
      Enqueued = jobStatesCount.GetValueOrDefault(JobStateValue.Enqueued, 0L),
      Failed = jobStatesCount.GetValueOrDefault(JobStateValue.Failed, 0L),
      Processing = jobStatesCount.GetValueOrDefault(JobStateValue.Processing, 0L),
      Scheduled = jobStatesCount.GetValueOrDefault(JobStateValue.Scheduled, 0L),
      Servers = serverCount,
      Succeeded = statsCount.GetValueOrDefault(statsSucceeded, 0L),
      Deleted = statsCount.GetValueOrDefault(statsDeleted, 0L),
      Recurring = recurringCount
    };

  }

  public JobList<EnqueuedJobDto> EnqueuedJobs(string queue, int @from, int perPage)
  {
    var queueApi = GetQueueApi(queue);
    var enqueuedJobIds = queueApi.GetEnqueuedJobIds(queue, from, perPage);
    return UseConnection(c => EnqueuedJobs(c, enqueuedJobIds));
  }

  public JobList<FetchedJobDto> FetchedJobs(string queue, int @from, int perPage)
  {
    var queueApi = GetQueueApi(queue);
    var fetchedJobIds = queueApi.GetFetchedJobIds(queue, from, perPage).ToArray();

    return UseConnection(connection => FetchedJobs(connection, fetchedJobIds));
  }

  public JobList<ProcessingJobDto> ProcessingJobs(int @from, int count)
  {
    return UseConnection(connection => GetJobs(
        connection,
        from, count,
        ProcessingState.StateName,
        (sqlJob, job, stateData) => new ProcessingJobDto
        {
          Job = job,
          ServerId = stateData.ContainsKey("ServerId") ? stateData["ServerId"] : stateData["ServerName"],
          StartedAt = JobHelper.DeserializeDateTime(stateData["StartedAt"]),
        }));
  }

  public JobList<ScheduledJobDto> ScheduledJobs(int @from, int count)
  {
    return UseConnection(connection => GetJobs(
        connection,
        from, count,
        ScheduledState.StateName,
        (sqlJob, job, stateData) => new ScheduledJobDto
        {
          Job = job,
          EnqueueAt = JobHelper.DeserializeDateTime(stateData["EnqueueAt"]),
          ScheduledAt = JobHelper.DeserializeDateTime(stateData["ScheduledAt"])
        }));
  }

  public JobList<SucceededJobDto> SucceededJobs(int @from, int count)
  {
    long? ExtractTotalDuration(IReadOnlyDictionary<string, string> stateData)
    {
      const string durationName = "PerformanceDuration";
      const string latencyName = "Latency";
      return stateData.ContainsKey(durationName) && stateData.ContainsKey(latencyName)
          ? long.Parse(stateData[durationName]) + long.Parse(stateData[latencyName])
          : default(long?);
    }

    return UseConnection(connection => GetJobs(
        connection,
        from,
        count,
        SucceededState.StateName,
        (sqlJob, job, stateData) => new SucceededJobDto
        {
          Job = job,
          Result = stateData.ContainsKey("Result") ? stateData["Result"] : null,
          TotalDuration = ExtractTotalDuration(stateData),
          SucceededAt = JobHelper.DeserializeNullableDateTime(stateData["SucceededAt"])
        }));
  }

  public JobList<FailedJobDto> FailedJobs(int @from, int count)
  {
    return UseConnection(connection => GetJobs(
        connection,
        from,
        count,
        FailedState.StateName,
        (sqlJob, job, stateData) => new FailedJobDto
        {
          Job = job,
          Reason = sqlJob.StateReason,
          ExceptionDetails = stateData["ExceptionDetails"],
          ExceptionMessage = stateData["ExceptionMessage"],
          ExceptionType = stateData["ExceptionType"],
          FailedAt = JobHelper.DeserializeNullableDateTime(stateData["FailedAt"])
        }));
  }

  public JobList<DeletedJobDto> DeletedJobs(int @from, int count)
  {
    return UseConnection(connection => GetJobs(
        connection,
        from,
        count,
        DeletedState.StateName,
        (sqlJob, job, stateData) => new DeletedJobDto
        {
          Job = job,
          DeletedAt = JobHelper.DeserializeNullableDateTime(stateData["DeletedAt"])
        }));
  }

  public long ScheduledCount()
  {
    return UseConnection(connection =>
        GetNumberOfJobsByStateName(connection, ScheduledState.StateName));
  }

  public long EnqueuedCount(string queue)
  {
    var queueApi = GetQueueApi(queue);
    var counters = queueApi.GetEnqueuedAndFetchedCount(queue);

    return counters.EnqueuedCount ?? 0;
  }

  public long FetchedCount(string queue)
  {
    var queueApi = GetQueueApi(queue);
    var counters = queueApi.GetEnqueuedAndFetchedCount(queue);

    return counters.FetchedCount ?? 0;
  }

  public long FailedCount()
  {
    return UseConnection(connection =>
        GetNumberOfJobsByStateName(connection, FailedState.StateName));
  }

  public long ProcessingCount()
  {
    return UseConnection(connection =>
        GetNumberOfJobsByStateName(connection, ProcessingState.StateName));
  }

  public long SucceededListCount()
  {
    return UseConnection(connection =>
        GetNumberOfJobsByStateName(connection, SucceededState.StateName));
  }

  public long DeletedListCount()
  {
    return UseConnection(connection =>
        GetNumberOfJobsByStateName(connection, DeletedState.StateName));
  }

  public IDictionary<DateTime, long> SucceededByDatesCount()
  {
    return UseConnection(connection =>
        GetTimelineStats(connection, "succeeded"));
  }

  public IDictionary<DateTime, long> FailedByDatesCount()
  {
    return UseConnection(connection =>
        GetTimelineStats(connection, "failed"));
  }

  public IDictionary<DateTime, long> HourlySucceededJobs()
  {
    return UseConnection(connection =>
        GetHourlyTimelineStats(connection, "succeeded"));
  }

  public IDictionary<DateTime, long> HourlyFailedJobs()
  {
    return UseConnection(connection =>
        GetHourlyTimelineStats(connection, "failed"));
  }

  private T UseConnection<T>(Func<IDbConnection, T> action) =>
      _storage.UseConnection(action);

  private long GetNumberOfJobsByStateName(IDbConnection connection, string stateName)
  {
    var sqlQuery = _storageOptions.DashboardJobListLimit.HasValue
        ? $"select count(j.Id) from (select Id from `{_storageOptions.TablesPrefix}Job` where StateName = @state limit @limit) as j"
        : $"select count(Id) from `{_storageOptions.TablesPrefix}Job` where StateName = @state";

    var count = connection.Query<int>(
         sqlQuery,
         new { state = stateName, limit = _storageOptions.DashboardJobListLimit })
         .Single();

    return count;
  }
  private IPersistentJobQueueMonitoringApi GetQueueApi(string queueName)
  {
    var provider = _storage.QueueProviders.GetProvider(queueName);
    var monitoringApi = provider.GetJobQueueMonitoringApi();
    return monitoringApi;
  }

  private JobList<TDto> GetJobs<TDto>(
      IDbConnection connection,
      int from,
      int count,
      string stateName,
      Func<SqlJob, Job?, IReadOnlyDictionary<string, string>, TDto> selector)
  {
    var jobsSql =
        $@"select * from (
                  select j.*, s.Reason as StateReason, s.Data as StateData, @rownum := @rownum + 1 AS `rank`
                  from `{_storageOptions.TablesPrefix}Job` j
                    cross join (SELECT @rownum := 0) r
                  left join `{_storageOptions.TablesPrefix}State` s on j.StateId = s.Id
                  where j.StateName = @stateName
                  order by j.Id desc
                ) as j where j.`rank` between @start and @end ";

    var jobs = connection.Query<SqlJob>(
            jobsSql, new { stateName, start = @from + 1, end = @from + count })
        .ToList();

    return DeserializeJobs(jobs, selector);
  }

  private static JobList<TDto> DeserializeJobs<TDto>(
      IReadOnlyCollection<SqlJob> sqlJobs,
      Func<SqlJob, Job?, IReadOnlyDictionary<string, string>, TDto> dtoFactory)
  {
    var result = new JobList<TDto>([]);
    foreach (var sqlJob in sqlJobs)
    {
      var deserializedData = SerializationHelper // maybe deserialize to IEnumerable<KeyValuePair<string, string>> instead? To avoid unnecessary dictionary construction
        .Deserialize<Dictionary<string, string>>(sqlJob.StateData); 
      IReadOnlyDictionary<string, string> stateData = deserializedData is not null
        ? new Dictionary<string, string>(deserializedData, StringComparer.OrdinalIgnoreCase)
        : [];
      var job = DeserializeJob(sqlJob.InvocationData, sqlJob.Arguments);
      var dto = dtoFactory(sqlJob, job, stateData);
      result.Add(new KeyValuePair<string, TDto>(sqlJob.Id.ToString(), dto));
    }
    return result;
  }

  private static Job? DeserializeJob(string invocationData, string arguments)
  {
    var data = SerializationHelper.Deserialize<InvocationData>(invocationData);
    data.Arguments = arguments;
    try
    {
      return data.DeserializeJob();
    }
    catch (JobLoadException)
    {
      Debug.Fail("I guess this shouldn't be happening");
      return null;
    }
  }

  private Dictionary<DateTime, long> GetTimelineStats(
      IDbConnection connection,
      string type)
  {
    var endDate = DateTime.UtcNow.Date;
    var dates = new List<DateTime>();
    for (var i = 0; i < 7; i++)
    {
      dates.Add(endDate);
      endDate = endDate.AddDays(-1);
    }

    var keyMaps = dates.ToDictionary(x => String.Format("stats:{0}:{1}", type, x.ToString("yyyy-MM-dd")), x => x);

    return GetTimelineStats(connection, keyMaps);
  }

  private Dictionary<DateTime, long> GetTimelineStats(IDbConnection connection,
      IDictionary<string, DateTime> keyMaps)
  {
    var valuesMap = connection
      .Query<KeyCountAggregatedCounter>(
        $"""
        SELECT `Key`, `Value` as `Count` 
        FROM `{_storageOptions.TablesPrefix}AggregatedCounter`
        WHERE `Key` in @keys
        """,        
        new { keys = keyMaps.Keys })
      .ToDictionary(x => x.Key ?? string.Empty, x => x.Count);

    foreach (var key in keyMaps.Keys)
    {
      if (!valuesMap.ContainsKey(key)) valuesMap.Add(key, 0);
    }
    var result = new Dictionary<DateTime, long>();
    for (var i = 0; i < keyMaps.Count; i++)
    {
      var value = valuesMap[keyMaps.ElementAt(i).Key];
      result.Add(keyMaps.ElementAt(i).Value, value);
    }
    return result;
  }

  class KeyCountAggregatedCounter 
  {
#pragma warning disable CS0649
    public string? Key;
    public long Count;
#pragma warning restore CS0649
  }

  private JobList<EnqueuedJobDto> EnqueuedJobs(
      IDbConnection connection, IReadOnlyCollection<int> ids)
  {
    IReadOnlyCollection<SqlJob> jobs = ids.Count > 0 ? SelectJobs(connection, ids) : [];
    return DeserializeJobs(
        jobs,
        (sqlJob, job, stateData) => new EnqueuedJobDto
        {
          Job = job,
          State = sqlJob.StateName,
          EnqueuedAt = sqlJob.StateName == EnqueuedState.StateName
            ? JobHelper.DeserializeNullableDateTime(stateData["EnqueuedAt"])
            : null
        });
  }

  private List<SqlJob> SelectJobs(IDbConnection connection, IReadOnlyCollection<int> ids)
  {
    var idsIn = ids.SqlInOperator("j.Id", "@jobId");
    using var command = connection.CreateCommand($"""
      SELECT
        j.Id, 
        j.StateName,
        j.InvocationData,
        j.Arguments,
        j.CreatedAt,
        j.ExpireAt,
        s.Reason as StateReason, 
        s.Data as StateData 
      FROM `{_storageOptions.TablesPrefix}Job`        j
      LEFT JOIN `{_storageOptions.TablesPrefix}State` s ON s.Id = j.StateId
      WHERE {idsIn};
      """);
    using var reader = command.ExecuteReader();
    var result = new List<SqlJob>(ids.Count);
    while (reader.Read())
    {
      result.Add(new()
      {
        Id = reader.GetInt("Id"),
        StateName = reader.GetNullableString("StateName"),
        InvocationData = reader.GetString("InvocationData"),
        Arguments = reader.GetString("Arguments"),
        CreatedAt = reader.GetDateTime("CreatedAt"),
        ExpireAt = reader.GetNullableDateTime("ExpireAt"),
        StateReason = reader.GetNullableString("StateReason"),
        StateData = reader.GetNullableString("StateData")
      });
    }
    return result;
  }

  private JobList<FetchedJobDto> FetchedJobs(
      IDbConnection connection, int[] ids)
  {
    var fetchedJobsSql = $@"/* Jobs with State */
                select j.*, s.Reason as StateReason, s.Data as StateData 
                from `{_storageOptions.TablesPrefix}Job` j
                left join `{_storageOptions.TablesPrefix}State` s on s.Id = j.StateId
                where j.Id in @jobIds";

    KeyValuePair<string, FetchedJobDto> ToPair(SqlJob sqlJob) =>
        new KeyValuePair<string, FetchedJobDto>(
            sqlJob.Id.ToString(),
            new FetchedJobDto
            {
              Job = DeserializeJob(sqlJob.InvocationData, sqlJob.Arguments),
              State = sqlJob.StateName,
            });

    var jobs = ids.Any()
        ? connection.Query<SqlJob>(fetchedJobsSql, new { jobIds = ids }).ToArray()
        : Array.Empty<SqlJob>();

    return new JobList<FetchedJobDto>(jobs.Select(ToPair));
  }

  private Dictionary<DateTime, long> GetHourlyTimelineStats(
      IDbConnection connection,
      string type)
  {
    var endDate = DateTime.UtcNow;
    var dates = new List<DateTime>();
    for (var i = 0; i < 24; i++)
    {
      dates.Add(endDate);
      endDate = endDate.AddHours(-1);
    }

    var keyMaps = dates.ToDictionary(x => $"stats:{type}:{x:yyyy-MM-dd-HH}", x => x);

    return GetTimelineStats(connection, keyMaps);
  }
}
