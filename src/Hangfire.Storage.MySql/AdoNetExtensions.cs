using System.Data;
using System.Diagnostics;

namespace Hangfire.Storage.MySql;

static class AdoNetExtensions
{

  public static IDbCommand CreateCommand(
    this IDbConnection connection, string commandText, CommandType commandType = CommandType.Text)
  {
    if (connection is null) throw new ArgumentNullException(nameof(connection));
    if (string.IsNullOrWhiteSpace(commandText)) throw new ArgumentException("Command text cannot be null or empty.", nameof(commandText));
    var command = connection.CreateCommand();
    command.CommandText = commandText;
    command.CommandType = commandType;
    return command;
  }

  public static IDbCommand AddParameter(
    this IDbCommand command, string name, object? value)
  {
    if (command is null) throw new ArgumentNullException(nameof(command));
    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
    Debug.Assert(name.StartsWith("@"));

    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.Value = value ?? DBNull.Value;
    command.Parameters.Add(parameter);
    return command;
  }

  public static IDbCommand WithTransaction(
    this IDbCommand command, IDbTransaction transaction)
  {
    if (command is null) throw new ArgumentNullException(nameof(command));
    if (transaction is null) throw new ArgumentNullException(nameof(transaction));
    command.Transaction = transaction;
    return command;
  }

  public static bool TableExists(
    this IDbConnection connection, string? tablesPrefix, string tableName)
  {
    using var command = connection.CreateCommand(
      $"""
      SELECT COUNT(*) as Count
      FROM information_schema.tables 
      WHERE table_schema = DATABASE() AND table_name = '{tablesPrefix}{tableName}';
      """);
    var count = Convert.ToInt32(command.ExecuteScalar());
    Debug.Assert(count is 0 or 1);
    return count > 0;
  }

}
