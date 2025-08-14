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

  public static DateTime GetDateTime(this IDataRecord rec, string field)
  { 
    return rec.GetNullableDateTime(field) 
      ?? throw new DataException($"Value of field '{field}' cannot be null.");
  }

  public static DateTime? GetNullableDateTime(this IDataRecord rec, string field)
  { 
    if (field is null)
    {
      throw new ArgumentNullException(nameof(field));
    }
    var obj = rec[field];
    return obj switch
    {
      DateTime dt => dt,
      null or DBNull => null,
      _ => Convert.ToDateTime(obj)
    };
  }

  public static int GetInt(this IDataRecord rec, string field)
  {
    return rec.GetNullableInt(field) 
      ?? throw new DataException($"Value of field '{field}' cannot be null.");
  }

  public static int? GetNullableInt(this IDataRecord rec, string field)
  {
    if (field is null)
    {
      throw new ArgumentNullException(nameof(field));
    }
    var obj = rec[field];
    return obj switch
    {
      int i => i,
      null or DBNull => null,
      _ => Convert.ToInt32(obj)
    };
  }

  public static long GetLong(this IDataRecord rec, string field)
  {
    return rec.GetNullableLong(field) 
      ?? throw new DataException($"Value of field '{field}' cannot be null.");
  }

  public static long? GetNullableLong(this IDataRecord rec, string field)
  {
    if (field is null)
    {
      throw new ArgumentNullException(nameof(field));
    }
    var obj = rec[field];
    return obj switch
    {
      long i => i,
      null or DBNull => null,
      _ => Convert.ToInt64(obj)
    };
  }

  /// <summary>
  /// Gets a string value from the IDataRecord by field name.
  /// </summary>
  /// <exception cref="DataException">When returned values is null</exception>
  public static string GetString(this IDataRecord rec, string field)
  { 
    return rec.GetNullableString(field) 
      ?? throw new DataException($"Value of field '{field}' cannot be null.");
  }

  public static string? GetNullableString(this IDataRecord rec, string field)
  {
    if (field is null)
    {
      throw new ArgumentNullException(nameof(field));
    }
    var obj = rec[field];
    return obj switch
    {
      string s => s,
      null or DBNull => null,
      _ => obj.ToString()
    };
  }

}

static class SqlHelper
{
  public static string SqlInOperator(
    this IEnumerable<int> values, string field, string parameterNamePrefix)
  {
    if (string.IsNullOrWhiteSpace(field))
    {
      throw new ArgumentException($"'{nameof(field)}' cannot be null or whitespace.", nameof(field));
    }
    if(values is not HashSet<int> valuesSet)
    {
      valuesSet = new (values);
    }
    if(valuesSet.Count == 0)
    {
      return "false";
    }
    else
    {
      field = field.Trim();
      var commaSeparatedValues = string.Join(",", valuesSet);
      return $"{field} in ({commaSeparatedValues})";
    }
  }

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
      var b = new StringBuilder($"{field} in (")
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