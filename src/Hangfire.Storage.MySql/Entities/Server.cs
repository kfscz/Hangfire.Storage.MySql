#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
using System;

namespace Hangfire.Storage.MySql.Entities
{
  internal class Server
  {
    public string Id { get; set; }
    public string Data { get; set; }
    public DateTime LastHeartbeat { get; set; }
  }
}