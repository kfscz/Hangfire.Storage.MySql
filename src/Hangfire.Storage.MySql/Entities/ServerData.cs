#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
using System;

namespace Hangfire.Storage.MySql.Entities;

internal class ServerData
{
  public int WorkerCount { get; set; }
  public string[] Queues { get; set; }
  public DateTime? StartedAt { get; set; }
}