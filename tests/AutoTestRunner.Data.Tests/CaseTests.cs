using AutoTestRunner.Core.Tests;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Data.Tests;

/// <summary>A case as a suite writes it, played by the runner against a real database, and a real queue when there is one.</summary>
public sealed class CaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerDataTests", Path.GetRandomFileName());

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private (FakeRunView View, Results Results) Run(string settings, string steps)
    {
        Directory.CreateDirectory(Path.Combine(_folder, "cases"));
        File.WriteAllText(Path.Combine(_folder, "suite.json"), $$"""{ "name": "Shop", "app": { "exe": "Shop.exe", "window": "Shop" }, {{settings}} }""");
        File.WriteAllText(Path.Combine(_folder, "cases", "01-shop.json"), $$"""{ "name": "Shop", "steps": [ {{steps}} ] }""");
        var suite = Suite.Load(_folder);
        var view = new FakeRunView();
        var results = new Results(Path.Combine(_folder, "results"), console: null);
        new Runner(suite, new FakeDriver(), new WindowsMachine(), view, new RunControl(unattended: true) { TimeoutScale = 0.2 }, results)
        {
            Connections = new Connections(new WindowsMachine()),
        }.Run(suite.Cases);
        return (view, results);
    }

    [Fact]
    public void A_case_sets_up_a_database_and_checks_what_is_in_it()
    {
        var (view, results) = Run("""
            "databases": { "shop": { "kind": "sqlite", "connection": "Data Source=${suiteDir}/shop.db" } }
            """, """
            { "query": "shop", "sql": "create table orders (id int, customer text, status text)" },
            { "query": "shop", "sql": "insert into orders values (@id, @customer, 'Open')", "params": { "id": "1001", "customer": "Ada" }, "as": "added" },
            { "query": "shop", "sql": "select customer from orders where id = @id", "params": { "id": "1001" }, "as": "customer" },
            { "checkpoint": "The order is in the database", "checks": [
              { "equals": "${added.rows}", "to": "1" },
              { "equals": "${customer}", "to": "Ada" },
              { "dbValue": "shop", "sql": "select status from orders where id = @id", "params": { "id": 1001 }, "equals": "Open" },
              { "dbValue": "shop", "sql": "select * from orders", "rows": 1 } ] }
            """);

        Assert.All(view.Checks, check => Assert.True(check.Passed, $"{check.Label}: {check.Actual}"));
        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
        Assert.Contains("out customer", view.Commands);
        Assert.Contains("out Ada", view.Commands);
    }

    [Fact]
    public void A_case_sends_and_receives_on_a_real_queue()
    {
        var queue = QueueTests.Queue("rabbitMq", "autotest.case");
        var (view, results) = Run($$"""
            "queues": { "orders": { "kind": "rabbitMq", "connection": "{{queue.Connection}}", "queue": "{{queue.Queue}}" } }
            """, """
            { "purge": "orders" },
            { "send": "orders", "message": "Order 1001 for Ada" },
            { "checkpoint": "Waiting", "checks": [ { "queue": "orders", "count": 1 }, { "queue": "orders", "has": "1001" } ] },
            { "receive": "orders", "contains": "Ada", "as": "order" },
            { "checkpoint": "Taken", "checks": [ { "equals": "${order}", "to": "Order 1001 for Ada" }, { "queue": "orders", "count": 0 } ] }
            """);

        Assert.All(view.Checks, check => Assert.True(check.Passed, $"{check.Label}: {check.Actual}"));
        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
    }
}
