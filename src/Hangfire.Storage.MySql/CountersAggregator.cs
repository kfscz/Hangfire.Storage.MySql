using Hangfire.Logging;
using Hangfire.Server;
using Hangfire.Storage.MySql.Locking;
using System.Data;

namespace Hangfire.Storage.MySql;

internal class CountersAggregator : IServerComponent
{
  static readonly ILog Logger = LogProvider.GetLogger(typeof(CountersAggregator));

  const int NumberOfRecordsInSinglePass = 1000;
  static readonly TimeSpan DelayBetweenPasses = TimeSpan.FromMilliseconds(500);

  readonly MySqlStorage _storage;
  readonly MySqlStorageOptions _options;

  public CountersAggregator(MySqlStorage storage, MySqlStorageOptions options)
  {
    _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    _options = options ?? throw new ArgumentNullException(nameof(options));
  }

  public void Execute(CancellationToken cancellationToken)
  {
    Logger.DebugFormat(
      $"Aggregating records in '{_options.TablesPrefix}Counter' table...");

    while (true)
    {
      var removedCount = _storage.UseConnection(AggregateCounter);
      if (removedCount < NumberOfRecordsInSinglePass)
      {
        break;
      }
      cancellationToken.WaitHandle.WaitOne(DelayBetweenPasses);
      cancellationToken.ThrowIfCancellationRequested();
    }

    cancellationToken.WaitHandle.WaitOne(_options.CountersAggregateInterval);
  }

  private int AggregateCounter(IDbConnection connection)
  {
    using (ResourceLock.AcquireOne(
      connection, _options.TablesPrefix, LockableResource.Counter))
    {
      string commantText = GetAggregationQuery();
      using var command = connection
        .CreateCommand(commantText)
        .AddParameter("@count", NumberOfRecordsInSinglePass);
      return command.ExecuteNonQuery();
    }
  }

  private string GetAggregationQuery()
  {
    var prefix = _options.TablesPrefix;
    return $"""
      CREATE TEMPORARY TABLE __refs__ 
      ENGINE = memory AS
      SELECT `Id` 
        FROM `{prefix}Counter` 
        LIMIT @count;

      INSERT INTO `{prefix}AggregatedCounter` (`Key`, Value, ExpireAt)
      SELECT `Key`, SumValue, MaxExpireAt
      FROM (
        SELECT `Key`, sum(Value) as SumValue, max(ExpireAt) AS MaxExpireAt
        FROM `{prefix}Counter` c
        JOIN __refs__ r ON r.Id = c.Id
        GROUP BY `Key`
      ) _
      ON DUPLICATE KEY UPDATE
        Value = Value + values(Value),
        ExpireAt = greatest(ExpireAt, values(ExpireAt));

      DELETE c FROM `{prefix}Counter` c
      JOIN __refs__ r ON r.Id = c.Id;

      DROP TABLE __refs__;
      """;
  }

  public override string ToString() => GetType().ToString();
}
