#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
using System;

namespace Hangfire.Storage.MySql.Entities;

internal class SqlHash
{
  public int Id { get; set; }
  public string Key { get; set; }
  public string Field { get; set; }
  public string Value { get; set; }
  public DateTime? ExpireAt { get; set; }
}
