using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Data.Tests;

/// <summary>
/// Each kind of database, through its own driver: a table made, rows written with parameters, read back as text, and
/// counted. SQLite always runs; the servers run where their connection string is in the environment, as CI sets it.
/// </summary>
public sealed class DatabaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerDataTests", Path.GetRandomFileName());
    private readonly Connections _connections = new(new UnusedMachine());

    public DatabaseTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    public static TheoryData<string, string> Servers() => new()
    {
        { "sqlServer", "ATR_SQLSERVER" },
        { "postgres", "ATR_POSTGRES" },
        { "oracle", "ATR_ORACLE" },
        { "mySql", "ATR_MYSQL" },
    };

    [Fact]
    public void Sqlite_reads_and_writes_rows_with_parameters() =>
        ReadsAndWrites(new DatabaseSettings("shop", "sqlite", $"Data Source={Path.Combine(_folder, "shop.db")}"));

    [Theory]
    [MemberData(nameof(Servers))]
    public void A_server_reads_and_writes_rows_with_parameters(string kind, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Assert.SkipWhen(string.IsNullOrEmpty(connection), $"{variable} isn't set, so there is no {DatabaseSettings.Kinds[kind]} to try.");
        ReadsAndWrites(new DatabaseSettings("shop", kind, connection!));
    }

    private void ReadsAndWrites(DatabaseSettings database)
    {
        var oracle = database.Kind == "oracle";
        var at = oracle ? ":" : "@";
        var table = "orders_" + Path.GetRandomFileName().Replace(".", "", StringComparison.Ordinal)[..8];
        var timeout = TimeSpan.FromSeconds(30);
        QueryResult Run(string sql, Dictionary<string, string>? parameters = null) => _connections.Query(database, sql, parameters ?? [], timeout);

        Run(oracle
            ? $"create table {table} (id number(10) primary key, customer varchar2(50), total number(10,2), shipped varchar2(5))"
            : $"create table {table} (id int primary key, customer varchar(50), total decimal(10,2), shipped varchar(5))");
        try
        {
            var insert = $"insert into {table} (id, customer, total, shipped) values ({at}id, {at}customer, {at}total, {at}shipped)";
            Assert.Equal(1, Run(insert, new() { ["id"] = "1001", ["customer"] = "Ada Lovelace", ["total"] = "26.00", ["shipped"] = "no" }).Count);
            Run(insert, new() { ["id"] = "1002", ["customer"] = "Grace Hopper", ["total"] = "4.50", ["shipped"] = "yes" });

            var customer = Run($"select customer from {table} where id = {at}id", new() { ["id"] = "1002" });
            Assert.Equal("Grace Hopper", customer.First);
            Assert.Equal(1, customer.Count);

            var all = Run($"select id, total from {table} order by id");
            Assert.Equal(2, all.Count);
            Assert.Equal(["1001", "1002"], all.Rows.Select(row => row[0]));
            Assert.StartsWith("26", all.Rows[0][1], StringComparison.Ordinal);

            Assert.Equal(Checks.Absent, Run($"select customer from {table} where id = 9").First);
            Assert.Equal(QueryResult.Null, Run($"select null as blank_value from {table} where id = 1001").First);
            Assert.Equal(2, Run($"update {table} set shipped = 'yes'").Count);
        }
        finally
        {
            Run($"drop table {table}");
        }
    }
}
