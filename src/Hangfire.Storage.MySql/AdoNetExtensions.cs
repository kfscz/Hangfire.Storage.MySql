using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

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
    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.Value = value ?? DBNull.Value;
    command.Parameters.Add(parameter);
    return command;
  }

}
