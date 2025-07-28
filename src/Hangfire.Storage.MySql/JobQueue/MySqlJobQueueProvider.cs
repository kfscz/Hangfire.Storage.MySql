using System;

namespace Hangfire.Storage.MySql.JobQueue;

internal class MySqlJobQueueProvider : IPersistentJobQueueProvider
{

  readonly IPersistentJobQueue _jobQueue;
  readonly IPersistentJobQueueMonitoringApi _monitoringApi;

  public MySqlJobQueueProvider(MySqlStorage storage, MySqlStorageOptions options)
  {
    if (storage is null) throw new ArgumentNullException(nameof(storage));
    if (options is null) throw new ArgumentNullException(nameof(options));
    _jobQueue = new MySqlJobQueue(storage, options);
    _monitoringApi = new MySqlJobQueueMonitoringApi(storage, options);
  }

  public IPersistentJobQueue GetJobQueue() => _jobQueue;

  public IPersistentJobQueueMonitoringApi GetJobQueueMonitoringApi() => _monitoringApi;

}