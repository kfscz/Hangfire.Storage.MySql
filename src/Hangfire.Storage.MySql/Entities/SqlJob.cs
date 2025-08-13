#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Hangfire.Storage.MySql.Entities;

class SqlJob
{
  public int Id { get; set; }
  public string InvocationData { get; set; }
  public string Arguments { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime? ExpireAt { get; set; }
  public string? StateName { get; set; }
  public string? StateReason { get; set; }
  public string? StateData { get; set; }
}

/// <summary>
/// Possible values in <c>StateName</c> field in <c>job</c> table.
/// </summary>
static class JobStateValue
{
  public const string Enqueued = "Enqueued";
  public const string Failed = "Failed";
  public const string Processing = "Processing";
  public const string Scheduled = "Scheduled";
}