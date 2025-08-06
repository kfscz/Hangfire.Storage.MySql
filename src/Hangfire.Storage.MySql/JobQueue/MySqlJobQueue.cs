using System.Data;
using Hangfire.Logging;
using Hangfire.Storage.MySql.Locking;
using System.Data.Common;

namespace Hangfire.Storage.MySql.JobQueue;

internal class MySqlJobQueue(
  MySqlStorage storage,  MySqlStorageOptions options
  ) : IPersistentJobQueue
{

  static readonly ILog Logger = LogProvider.GetLogger(typeof(MySqlJobQueue));

  readonly MySqlStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
  readonly MySqlStorageOptions _options = options ?? throw new ArgumentNullException(nameof(options));

  public IFetchedJob Dequeue(string[] queues, CancellationToken cancellationToken)
  {
    if (queues is null)
    {
      throw new ArgumentNullException(nameof(queues));
    }
    else if (queues.Length == 0)
    {
      throw new ArgumentException("Queue array must be non-empty.", nameof(queues));
    }

    var token = Guid.NewGuid().ToString();
    // I'm not sure if this is used correctly...
    var expiration = _options.InvisibilityTimeout;
    // the connection variable is mutable, it job is claimed then this connection
    // is passed to job itself, and variable is set to null so it does not get
    // disposed here in such case
    var connection = _storage.CreateAndOpenConnection();
    try
    {
      while (true)
      {
        cancellationToken.ThrowIfCancellationRequested();
        using (ResourceLock.AcquireOne(connection, _options.TablesPrefix, LockableResource.Queue))
        {
          var updated = ClaimJobLockless(connection, queues, expiration, token);
          if (updated != 0)
          {
            var fetchedJob = FetchJobByTokenLockless(connection, token);
            return new MySqlFetchedJob(
              storage: _storage,
              options: _options,
              connection: Interlocked.Exchange(ref connection, null)!,
              fetchedJob: fetchedJob);
          }
        }
        cancellationToken.WaitHandle.WaitOne(_options.QueuePollInterval);
        cancellationToken.ThrowIfCancellationRequested();
      }
    }
    catch (DbException ex)
    {
      Logger.ErrorException(ex.Message, ex);
      throw;
    }
    finally
    {
      _storage.ReleaseConnection(connection);
    }
  }

  private int ClaimJobLockless(
    IDbConnection connection, string[] queues, TimeSpan expiration, string token)
  {
    var now = DateTime.Now;
    var then = now.Subtract(expiration);
    var queueRestriction = queues.SqlInOperator("Queue", "@queue", out var queueParams);
    string updateCommand = $"""
      /* {nameof(MySqlJobQueue)}.{nameof(ClaimJobLockless)} */
      UPDATE `{_options.TablesPrefix}JobQueue` 
      SET 
        FetchedAt = @now, 
        FetchToken = @token
      WHERE ({queueRestriction}) and (FetchedAt is null or FetchedAt < @then)
      LIMIT 1;
      """;
    using var command = connection.CreateCommand(updateCommand)
      .AddParameters(queueParams)
      .AddParameter("@now", now)
      .AddParameter("@then", then)
      .AddParameter("@token", token);
    return command.ExecuteNonQuery();
  }

  private FetchedJob FetchJobByTokenLockless(IDbConnection connection, string token)
  {
    using var command = connection
      .CreateCommand($"""
        /* {nameof(MySqlJobQueue)}.{nameof(FetchJobByTokenLockless)} */
        SELECT Id, JobId, Queue
        FROM `{_options.TablesPrefix}JobQueue`
        WHERE FetchToken = @token;
        """)
      .AddParameter("@token", token);
    using var reader = command.ExecuteReader();
    return reader.Read()
      ? new () {
          Id = Convert.ToInt32(reader["Id"]),
          JobId = Convert.ToInt32(reader["JobId"]),
          Queue = (string)reader["Queue"],
        }
      : throw new InvalidOperationException("Expected at least on row result");
  }

  private void Enqueue(
    IDbConnection connection, IDbTransaction? transaction, string queue, string jobId)
  {
    Logger.TraceFormat("Enqueue JobId={0} Queue={1}", jobId, queue);
    using (ResourceLock.AcquireOne(
      connection, transaction, _options.TablesPrefix, LockableResource.Queue))
    {
      using var command = connection
        .CreateCommand($"""
          /* {nameof(MySqlJobQueue)}.{nameof(Enqueue)} */
          INSERT INTO `{_options.TablesPrefix}JobQueue` (JobId, Queue)
          VALUES (@jobId, @queue);
          """)
        .WithTransaction(transaction)
        .AddParameter("@jobId", jobId)
        .AddParameter("@queue", queue);
      command.ExecuteNonQuery();
    }
  }

  public void Enqueue(IDbConnection connection, string queue, string jobId) =>
    Enqueue(connection, null, queue, jobId);

  public void Enqueue(IDbTransaction transaction, string queue, string jobId) =>
    Enqueue(transaction.Connection!, transaction, queue, jobId);

}
