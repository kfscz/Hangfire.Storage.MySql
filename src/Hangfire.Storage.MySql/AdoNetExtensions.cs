using System.Data;
using System.Diagnostics;
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

  public static IDbCommand AddParameters(
    this IDbCommand command, IEnumerable<KeyValuePair<string, object>> parameters)
  {
    foreach (var parameter in parameters)
    {
      command.AddParameter(parameter.Key, parameter.Value);
    }
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
    this IDbCommand command, IDbTransaction? transaction)
  {
    if (command is null) throw new ArgumentNullException(nameof(command));
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

static class SqlHelper
{
  public static string SqlInOperator<T>(
    this IEnumerable<T> values, string field, string parameterNamePrefix, 
    out Dictionary<string, object> parameters)
  {
    if (string.IsNullOrWhiteSpace(field))
    {
      throw new ArgumentException($"'{nameof(field)}' cannot be null or whitespace.", nameof(field));
    }
    using var en = values.GetEnumerator();
    if (!en.MoveNext())
    {
      parameters = new();
      return "false";
    }
    else
    {
      var parameterGen = new ParameterGen(parameterNamePrefix);
      field = field.Trim();
      var b = new StringBuilder($"`{field}` in (")
        .Append(parameterGen.AddParameter(en.Current));
      while (en.MoveNext())
      {
        b .Append(',')
          .Append(parameterGen.AddParameter(en.Current));
      }
      b.Append(')');
      parameters = parameterGen.GetParameters();
      return b.ToString();
    }
  }

  class ParameterGen(string parameterNamePrefix)
  {

    readonly string _parameterNamePrefix = parameterNamePrefix;
    readonly Dictionary<string, object> _items = new();
    int _n = 0;

    public string AddParameter(object? value)
    {
      var currentNum = Interlocked.Increment(ref _n);
      var parameterName = $"{_parameterNamePrefix}{currentNum}";
      _items.Add(parameterName, value ?? DBNull.Value);
      return parameterName;
    }

    public Dictionary<string, object> GetParameters() => _items;
  }

}