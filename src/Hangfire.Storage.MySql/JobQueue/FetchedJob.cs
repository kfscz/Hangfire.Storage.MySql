#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
namespace Hangfire.Storage.MySql.JobQueue;

internal class FetchedJob
{
  public int Id { get; set; }
  public int JobId { get; set; }
  public string Queue { get; set; }
}
