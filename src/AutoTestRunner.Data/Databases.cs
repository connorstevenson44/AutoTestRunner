using System.Data.Common;
using System.Globalization;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace AutoTestRunner.Data;

/// <summary>
/// SQL against any of the databases, through ADO.NET: each driver's connection, a command with the step's values as
/// parameters, never pasted into the SQL, and its rows read as text.
/// </summary>
internal static class Databases
{
    // Enough for any check, and a table a person can read: the rest are counted, not kept.
    private const int MostRows = 1000;

    public static QueryResult Query(DatabaseSettings database, string sql, IReadOnlyDictionary<string, string> parameters, TimeSpan timeout)
    {
        using var connection = Open(database);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = Math.Max(1, (int)timeout.TotalSeconds);
        // Oracle binds by position unless told otherwise.
        if (command is OracleCommand oracle)
            oracle.BindByName = true;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            // Values are text, as scripts write them. PostgreSQL, unlike the others, won't turn text into a number for a
            // number column, so it is sent with no type, for the server to read as the column needs.
            if (parameter is NpgsqlParameter postgres)
                postgres.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Unknown;
            command.Parameters.Add(parameter);
        }
        using var reader = command.ExecuteReader();
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<IReadOnlyList<string?>>();
        var count = 0;
        while (reader.Read())
        {
            if (++count > MostRows)
                continue;
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : Text(reader.GetValue(index)))]);
        }
        // A statement that reads nothing, such as an insert, says how many rows it changed.
        return new QueryResult(columns, rows, columns.Count == 0 ? Math.Max(reader.RecordsAffected, 0) : count);
    }

    private static DbConnection Open(DatabaseSettings database)
    {
        DbConnection connection = database.Kind switch
        {
            "sqlServer" => new SqlConnection(database.Connection),
            "postgres" => new NpgsqlConnection(database.Connection),
            "oracle" => new OracleConnection(database.Connection),
            "mySql" => new MySqlConnection(database.Connection),
            "sqlite" => new SqliteConnection(database.Connection),
            _ => throw new ScriptException($"There is no database kind {database.Kind}."),
        };
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    // A value as a check compares it: numbers and dates the same on every machine, bytes as hex.
    private static string Text(object value) => value switch
    {
        DateTime date => date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
