namespace Hangfire.Storage.MySql.JobQueue;

public interface IPersistentJobQueueMonitoringApi
{
  // TODO: return Lists is probably the better idea
  IEnumerable<string> GetQueues();
  HashSet<int> GetEnqueuedJobIds(string queue, int from, int perPage);
  IEnumerable<int> GetFetchedJobIds(string queue, int from, int perPage);
  EnqueuedAndFetchedCountDto GetEnqueuedAndFetchedCount(string queue);
}