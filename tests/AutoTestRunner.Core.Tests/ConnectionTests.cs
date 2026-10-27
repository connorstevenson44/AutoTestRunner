using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>Database and queue steps and checks, against databases and queues that stand in for the servers.</summary>
public sealed class ConnectionTests : IDisposable
{
    private const string Settings = """
        "variables": { "pgPassword": "s3cret", "inbox": "orders.new" },
        "databases": { "shop": { "kind": "postgres", "connection": "Host=localhost;Database=shop;Username=test;Password=${pgPassword}" } },
        "queues": {
          "newOrders": { "kind": "rabbitMq", "connection": "amqp://guest:guest@localhost:5672/", "queue": "${inbox}" },
          "invoices": { "kind": "ibmMq", "connection": "localhost(1414)", "channel": "DEV.APP.SVRCONN", "queueManager": "QM1", "queue": "DEV.QUEUE.1" }
        }
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private string Suite(string steps, string settings = Settings)
    {
        var suite = Path.Combine(_folder, "suite");
        Directory.CreateDirectory(Path.Combine(suite, "cases"));
        File.WriteAllText(Path.Combine(suite, "suite.json"), $$"""{ "name": "Data", "app": { "exe": "App.exe", "window": "App" }, {{settings}} }""");
        File.WriteAllText(Path.Combine(suite, "cases", "01-data.json"), $$"""{ "name": "Data", "steps": [ {{steps}} ] }""");
        return suite;
    }

    private (FakeRunView View, Results Results) Run(string steps, FakeConnections connections, string settings = Settings)
    {
        var loaded = Scripting.Suite.Load(Suite(steps, settings));
        var view = new FakeRunView();
        var results = new Results(Path.Combine(_folder, "results"), console: null);
        new Runner(loaded, new FakeDriver(), new FakeMachine(), view, new Driving.RunControl(unattended: true) { TimeoutScale = 0.01 }, results) { Connections = connections }
            .Run(loaded.Cases);
        return (view, results);
    }

    [Fact]
    public void A_query_keeps_the_first_value_and_its_rows_with_its_variables_and_params_filled_in()
    {
        var connections = new FakeConnections();
        connections.Answers["select status from orders where id = @id"] = new QueryResult(["status", "total"], [["Shipped", "26.00"], ["Open", "4.50"]], 2);
        var (view, results) = Run("""
            { "set": { "order": "1001" } },
            { "query": "shop", "sql": "select status from orders where id = @id", "params": { "id": "${order}" }, "as": "status" },
            { "checkpoint": "Kept", "checks": [ { "equals": "${status}", "to": "Shipped" }, { "equals": "${status.rows}", "to": "2" },
              { "contains": "${status.output}", "text": "Open\t4.50" } ] }
            """, connections);

        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
        var (database, _, parameters) = connections.Queries.Single();
        Assert.Equal("Host=localhost;Database=shop;Username=test;Password=s3cret", database.Connection);
        Assert.Equal("1001", parameters["id"]);
        var step = view.Steps.Single(line => line.Kind == ActivityKind.Database);
        Assert.Equal("Query shop \"select status from orders where id = @id\"", step.Text);
        // Pointing at the database says what it is, without its password.
        var badge = step.Parts.Single(part => part.Kind == PartKind.Database);
        Assert.Equal("PostgreSQL\nHost=localhost;Database=shop;Username=test;Password=•••", badge.Tip);
        Assert.Contains("start shop (PostgreSQL)", view.Commands);
        Assert.Contains("out status\ttotal", view.Commands);
        Assert.DoesNotContain(view.Commands, line => line.Contains("s3cret", StringComparison.Ordinal));
    }

    [Fact]
    public void A_database_can_be_read_from_the_apps_own_config_file()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Orders.exe.config"), """
            <configuration><database><type>SQLite</type><connectionString>Data Source=orders.db</connectionString></database></configuration>
            """);
        var connections = new FakeConnections();
        connections.Answers["select 1"] = new QueryResult(["one"], [["1"]], 1);
        var (_, results) = Run("""{ "query": "orders", "sql": "select 1" }""", connections, """
            "variables": { "appConfig": "../Orders.exe.config" },
            "helpers": { "databaseType": { "xPath": "//database/type/text()" }, "databaseConnection": { "xPath": "//database/connectionString/text()" } },
            "databases": { "orders": { "kind": "${databaseType(readFile(appConfig))}", "connection": "${databaseConnection(readFile(appConfig))}" } }
            """);

        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
        var (database, _, _) = connections.Queries.Single();
        Assert.Equal(("sqlite", "Data Source=orders.db"), (database.Kind, database.Connection));
    }

    [Fact]
    public void A_database_kind_that_comes_to_no_kind_fails_the_step_saying_what_it_came_to()
    {
        var (_, results) = Run("""{ "query": "orders", "sql": "select 1" }""", new FakeConnections(), """
            "variables": { "kind": "access" },
            "databases": { "orders": { "kind": "${kind}", "connection": "Data Source=orders.db" } }
            """);

        Assert.Equal(CaseStatus.Failed, results.Cases.Single().Status);
        Assert.Contains(results.Cases.Single().Failures, failure => failure.Contains("comes to \"access\", which isn't one of: sqlServer", StringComparison.Ordinal));
    }

    [Fact]
    public void A_query_that_fails_fails_its_step_and_the_case_carries_on()
    {
        var (view, results) = Run("""
            { "query": "shop", "sql": "select nothing" },
            { "say": "Still here" }
            """, new FakeConnections());

        Assert.Equal(CaseStatus.Failed, results.Cases.Single().Status);
        Assert.Contains("The query on shop failed: relation in \"select nothing\" does not exist", view.StepFailures);
        Assert.Contains("Still here", view.Shown);
    }

    [Fact]
    public void A_database_value_check_compares_the_first_value_and_says_what_it_found()
    {
        var connections = new FakeConnections();
        connections.Answers["select status from orders"] = FakeConnections.Rows(["Open"]);
        connections.Answers["select 1 from orders where id = 1"] = FakeConnections.Rows();
        connections.Answers["delete from orders"] = new QueryResult([], [], 3);
        var (view, _) = Run("""
            { "checkpoint": "Database", "checks": [
              { "dbValue": "shop", "sql": "select status from orders", "equals": "Shipped" },
              { "dbValue": "shop", "sql": "select status from orders", "contains": "pen" },
              { "dbValue": "shop", "sql": "select 1 from orders where id = 1" },
              { "dbValue": "shop", "sql": "delete from orders", "rows": 3 } ] }
            """, connections);

        Assert.Equal([false, true, false, true], view.Checks.Select(check => check.Passed));
        Assert.Equal("Open", view.Checks[0].Actual);
        Assert.Equal("no rows", view.Checks[2].Actual);
        Assert.Equal(SubjectKind.Database, view.Checks[0].Subject.Kind);
        Assert.Equal("shop", view.Checks[0].Subject.Name);
        Assert.Equal("select status from orders", view.Checks[0].Subject.Detail);
    }

    [Fact]
    public void Messages_sent_are_received_in_order_and_a_queue_check_looks_without_taking_them()
    {
        var connections = new FakeConnections();
        var (view, results) = Run("""
            { "purge": "newOrders" },
            { "send": "newOrders", "message": "{\"id\":1001}" },
            { "send": "newOrders", "message": "{\"id\":1002}" },
            { "checkpoint": "Waiting", "checks": [ { "queue": "newOrders", "count": 2 }, { "queue": "newOrders", "has": "1002" }, { "queue": "invoices", "count": 0 } ] },
            { "receive": "newOrders", "contains": "1001", "as": "first" },
            { "checkpoint": "Taken", "checks": [ { "equals": "${first}", "to": "{\"id\":1001}" }, { "queue": "newOrders", "count": 1 } ] }
            """, connections);

        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
        Assert.All(view.Checks, check => Assert.True(check.Passed, $"{check.Label}: {check.Actual}"));
        Assert.Equal(["{\"id\":1002}"], connections.Queues["orders.new"]);
        Assert.Equal(["Empty newOrders", "Send \"{\"id\":1001}\" to newOrders"], view.Steps.Where(step => step.Kind == ActivityKind.Queue).Take(2).Select(step => step.Text));
        Assert.Equal("RabbitMQ\namqp://guest:•••@localhost:5672/\norders.new", view.Steps[0].Parts.Single(part => part.Kind == PartKind.Queue).Tip);
    }

    [Fact]
    public void A_message_that_isnt_the_one_expected_fails_its_step_and_nothing_coming_is_stuck()
    {
        var connections = new FakeConnections();
        connections.Queues["DEV.QUEUE.1"] = ["Invoice 1002"];
        var (view, results) = Run("""
            { "receive": "invoices", "contains": "1001" },
            { "receive": "invoices", "within": 1 }
            """, connections);

        Assert.Contains("The message from invoices isn't the one expected: wanted 1001, got Invoice 1002", view.StepFailures);
        // Unattended, stuck ends the run.
        Assert.NotEqual(CaseStatus.Passed, results.Cases.Single().Status);
        Assert.Contains(results.Cases.Single().Failures, failure => failure.Contains("receive a message from invoices", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_names_a_database_or_queue_that_isnt_in_suite_json_and_what_a_step_is_missing()
    {
        var error = Assert.Throws<SuiteException>(() => Scripting.Suite.Load(Suite("""
            { "query": "orders", "sql": "select 1" },
            { "query": "shop" },
            { "send": "newOrders" },
            { "receive": "outbox" },
            { "checkpoint": "Checks", "checks": [ { "queue": "newOrders" }, { "dbValue": "shop" } ] }
            """)));

        Assert.Equal(
        [
            "01-data.json, step 1: there is no database called orders in suite.json. It is one of: shop.",
            "01-data.json, step 2: query needs sql, what to run.",
            "01-data.json, step 3: send needs message, the text to send, or file, a file with it in.",
            "01-data.json, step 4: there is no queue called outbox in suite.json. It is one of: newOrders, invoices.",
            "01-data.json, step 5: the queue check needs count, how many messages are waiting, or has, text one of them has.",
            "01-data.json, step 5: dbValue needs sql, the query that reads the value.",
        ], error.Problems);
    }

    [Fact]
    public void Validate_says_what_a_database_or_queue_in_suite_json_is_missing()
    {
        var error = Assert.Throws<SuiteException>(() => Scripting.Suite.Load(Suite("""{ "say": "Nothing" }""", """
            "databases": { "a": { "kind": "access", "connection": "x" }, "b": { "kind": "oracle" } },
            "queues": { "c": { "kind": "ibmMq", "queue": "Q" }, "d": { "kind": "msmq", "queue": ".\\private$\\d", "format": "binary" } }
            """)));

        Assert.Equal(
        [
            "suite.json: database a needs kind, one of: sqlServer, postgres, oracle, mySql, sqlite.",
            "suite.json: database b needs connection, its connection string.",
            "suite.json: queue c needs connection, such as localhost(1414).",
            "suite.json: queue c needs channel, such as DEV.APP.SVRCONN.",
            "suite.json: queue c needs queueManager, such as QM1.",
            "suite.json: queue d's format is xml or text, not binary.",
        ], error.Problems);
    }

    [Theory]
    [InlineData("Server=.;User Id=sa;Password=p@ss;TrustServerCertificate=true", "Server=.;User Id=sa;Password=•••;TrustServerCertificate=true")]
    [InlineData("Data Source=db:1521/FREEPDB1;User Id=shop;PWD='a;b'", "Data Source=db:1521/FREEPDB1;User Id=shop;PWD=•••")]
    [InlineData("amqp://app:secret@rabbit:5672/vhost", "amqp://app:•••@rabbit:5672/vhost")]
    [InlineData("Data Source=orders.db", "Data Source=orders.db")]
    public void Passwords_are_hidden_wherever_a_connection_shows(string connection, string shown) =>
        Assert.Equal(shown, Sources.Hidden(connection));
}
