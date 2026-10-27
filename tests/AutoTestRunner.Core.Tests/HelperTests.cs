using System.Text.Json.Nodes;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>Helpers that read a setting out of the app's own files: XPath, JSONPath, and readFile to give them the file.</summary>
public sealed class HelperTests : IDisposable
{
    private const string AppConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration xmlns="urn:contoso:orders">
          <database>
            <type>SqlServer</type>
            <connectionString>Server=.\SQLEXPRESS;Database=Orders</connectionString>
          </database>
          <appSettings>
            <add key="Mode" value="Test" />
            <add key="Pause" value="3" />
          </appSettings>
        </configuration>
        """;

    private const string AppSettings = """
        {
          // As .NET apps write them: comments, and a comma after the last item.
          "ConnectionStrings": { "Orders": "Data Source=orders.db", "Audit db": "Data Source=audit.db" },
          "Queues": [ { "Name": "orders.new" }, { "Name": "orders.done" }, ],
          "Retries": 3,
          "Tracing": null
        }
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static Helper Make(string way, string pattern) => Helper.From("pick", new JsonObject { [way] = pattern });

    [Theory]
    [InlineData("//database/connectionString/text()", @"Server=.\SQLEXPRESS;Database=Orders")]
    [InlineData("//database/type", "SqlServer")]
    [InlineData("//add[@key='Mode']/@value", "Test")]
    [InlineData("//add/@value", "Test")]
    [InlineData("count(//add)", "2")]
    [InlineData("//add[@key='Pause']/@value > 2", "true")]
    [InlineData("//nothing", "")]
    public void An_xpath_helper_gives_the_first_thing_it_finds_whatever_the_namespace(string path, string expected) =>
        Assert.Equal(expected, Make("xPath", path).Apply(AppConfig));

    [Theory]
    [InlineData("$.ConnectionStrings.Orders", "Data Source=orders.db")]
    [InlineData("$.connectionStrings.orders", "Data Source=orders.db")]
    [InlineData("$['ConnectionStrings']['Audit db']", "Data Source=audit.db")]
    [InlineData("$.Queues[1].Name", "orders.done")]
    [InlineData("$.Queues[-1].Name", "orders.done")]
    [InlineData("$.Queues[*].Name", "orders.new")]
    [InlineData("$..Name", "orders.new")]
    [InlineData("$.Queues[0]", """{"Name":"orders.new"}""")]
    [InlineData("$.Retries", "3")]
    [InlineData("$.Tracing", "")]
    [InlineData("$.Missing.Orders", "")]
    [InlineData("$.Queues[5]", "")]
    public void A_jsonpath_helper_gives_the_first_thing_it_finds(string path, string expected) =>
        Assert.Equal(expected, Make("jsonPath", path).Apply(AppSettings));

    [Theory]
    [InlineData("jsonPath", "ConnectionStrings.Orders", "starts with $")]
    [InlineData("jsonPath", "$.Queues[", "doesn't read from")]
    [InlineData("jsonPath", "$.Queues[first]", "doesn't read from")]
    [InlineData("jsonPath", "$.Queues.", "no name after it")]
    [InlineData("xPath", "//add[", "xPath doesn't read")]
    public void A_path_that_doesnt_read_says_why_when_the_suite_loads(string way, string pattern, string said) =>
        Assert.Contains(said, Assert.Throws<ArgumentException>(() => Make(way, pattern)).Message);

    [Fact]
    public void A_helper_needs_one_way_to_pick_and_text_that_is_what_it_says()
    {
        Assert.Contains("one of regex, xPath or jsonPath", Assert.Throws<ArgumentException>(() => Helper.From("pick", new JsonObject())).Message);
        Assert.Contains("one of regex, xPath or jsonPath", Assert.Throws<ArgumentException>(() => Helper.From("pick", new JsonObject { ["regex"] = "a", ["xPath"] = "//b" })).Message);
        Assert.Contains("isn't", Assert.Throws<ScriptException>(() => Make("xPath", "//a").Apply("{ }")).Message);
        Assert.Contains("isn't", Assert.Throws<ScriptException>(() => Make("jsonPath", "$.a").Apply("<a/>")).Message);
    }

    [Fact]
    public void ReadFile_gives_a_file_from_the_suite_folder_to_a_helper()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "app"));
        File.WriteAllText(Path.Combine(_folder, "app", "Orders.exe.config"), AppConfig);
        var scope = new VariableScope(helpers: new Dictionary<string, Helper>
        {
            ["databaseType"] = Helper.From("databaseType", new JsonObject { ["xPath"] = "//database/type/text()" }),
            ["mode"] = Helper.From("mode", new JsonObject { ["regex"] = "key=\"Mode\" value=\"(\\w+)\"" }),
        });
        scope.Set("suiteDir", _folder);
        scope.Set("appConfig", Path.Combine("app", "Orders.exe.config"));

        Assert.Equal("SqlServer", scope.Expand("${databaseType(readFile(appConfig))}"));
        // ReadFile, whatever its case, and a path given as it is.
        Assert.Equal("SqlServer", scope.Expand("${databaseType(ReadFile(appConfig))}"));
        Assert.Equal("Test", scope.Expand("${mode(readFile('app/Orders.exe.config'))}"));
        Assert.Equal(AppConfig, scope.Expand("${readFile(appConfig)}"));
        Assert.Contains("couldn't read", Assert.Throws<ScriptException>(() => scope.Expand("${readFile('app/nothing.config')}")).Message);
    }

    [Fact]
    public void A_variable_read_from_a_file_that_isnt_there_yet_is_read_when_it_is_used()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "suite.json"), """
            { "name": "Helpers", "app": { "exe": "App.exe", "window": "App" },
              "variables": { "shopName": "${shopName(readFile('bin/App.dll.config'))}", "title": "Orders" },
              "helpers": { "shopName": { "xPath": "//add[@key='ShopName']/@value" } } }
            """);
        var globals = Runner.Variables(Suite.Load(_folder), _folder, new Dictionary<string, string>());

        Assert.Contains("shopName: readFile couldn't read", Assert.Throws<ScriptException>(() => globals.Expand("${shopName}")).Message);
        Assert.Contains(globals.All(), value => value.Name == "shopName" && value.Text.Contains("couldn't read", StringComparison.Ordinal));
        Directory.CreateDirectory(Path.Combine(_folder, "bin"));
        File.WriteAllText(Path.Combine(_folder, "bin", "App.dll.config"), """<configuration><appSettings><add key="ShopName" value="Contoso" /></appSettings></configuration>""");
        Assert.Equal("Contoso: Orders", globals.Expand("${shopName}: ${title}"));
    }

    [Fact]
    public void A_suite_with_a_helper_called_readFile_or_one_that_doesnt_read_says_so()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "suite.json"), """
            { "name": "Helpers", "app": { "exe": "App.exe", "window": "App" },
              "helpers": { "readFile": { "regex": "." }, "port": { "jsonPath": "Port" }, "host": { "xPath": "//host" } } }
            """);
        var problems = Assert.Throws<SuiteException>(() => Suite.Load(_folder)).Problems;
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("readFile is built in", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("helper port: its jsonPath starts with $", StringComparison.Ordinal));
    }
}
