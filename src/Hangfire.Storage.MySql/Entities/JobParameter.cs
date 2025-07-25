#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
namespace Hangfire.Storage.MySql.Entities;

internal class JobParameter
{
  public int JobId { get; set; }
  public string Name { get; set; }
  public string Value { get; set; }
}
