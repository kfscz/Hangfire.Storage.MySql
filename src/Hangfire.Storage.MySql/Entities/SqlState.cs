#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
using System;

namespace Hangfire.Storage.MySql.Entities;

internal class SqlState
{
  public int JobId { get; set; }
  public string Name { get; set; }
  public string Reason { get; set; }
  public DateTime CreatedAt { get; set; }
  public string Data { get; set; }
}
