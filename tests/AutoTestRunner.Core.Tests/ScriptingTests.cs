using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

public class ScriptingTests
{
    private static readonly string Demo = Path.Combine(AppContext.BaseDirectory, "samples", "demo");

    private static VariableScope VariableScope()
    {
        var scope = new VariableScope(helpers: new Dictionary<string, Helper> { ["logFile"] = new RegexHelper("logFile", new Regex("Log: (.+)$")) });
        scope.Set("name", "main");
        scope.Set("files", Value.Of("Files: 3 added", new Dictionary<string, string> { ["name"] = "Files: 3 added", ["helpText"] = "tip" }));
        scope.Set("status", @"Deploy finished. Log: C:\logs\1.log");
        return scope;
    }

    [Theory]
    [InlineData("Install ${name}", "Install main")]
    [InlineData("${files.helpText}", "tip")]
    [InlineData("${files}", "Files: 3 added")]
    [InlineData("${logFile(status)}", @"C:\logs\1.log")]
    [InlineData("${logFile('Log: here')}", "here")]
    [InlineData("write $${name} as it is", "write ${name} as it is")]
    [InlineData("no variables", "no variables")]
    public void Expressions_fill_in_variables_parts_and_helpers(string text, string expected) =>
        Assert.Equal(expected, VariableScope().Expand(text));

    [Fact]
    public void An_unknown_variable_or_part_says_what_there_is()
    {
        var scope = VariableScope();
        Assert.Contains("no variable called nope", Assert.Throws<ScriptException>(() => scope.Expand("${nope}")).Message);
        Assert.Contains("It has: name, helpText", Assert.Throws<ScriptException>(() => scope.Expand("${files.value}")).Message);
        Assert.Contains("no helper called size", Assert.Throws<ScriptException>(() => scope.Expand("${size(name)}")).Message);
    }

    [Fact]
    public void A_macro_scope_reads_the_case_but_keeps_its_own_parameters()
    {
        var scope = VariableScope();
        var inner = scope.Child();
        inner.Set("title", "Check the files");
        Assert.Equal("main: Check the files", inner.Expand("${name}: ${title}"));
        Assert.False(scope.TryGet("title", out _));
    }

    [Fact]
    public void Targets_resolve_elements_with_parameters_shorthands_and_fallbacks()
    {
        var elements = new Dictionary<string, JsonNode>
        {
            ["newInstallName"] = JsonNode.Parse("""[ { "id": "NewInstallName" }, { "type": "Edit", "name": "New install name" } ]""")!,
            ["folderRow"] = JsonNode.Parse("""{ "type": "Button", "helpText": "${path}" }""")!,
        };
        var scope = VariableScope();

        var named = Targets.Resolve(JsonValue.Create("newInstallName"), scope, elements);
        Assert.Equal(["NewInstallName", null], named.Selectors.Select(selector => selector.Id));
        Assert.Equal("the new install name", named.What);

        var row = Targets.Resolve(JsonNode.Parse("""{ "element": "folderRow", "with": { "path": "C:\\Apps\\${name}" } }"""), scope, elements);
        Assert.Equal(@"C:\Apps\main", row.Selectors.Single().HelpText);

        var shorthand = Targets.Resolve(JsonNode.Parse("""{ "text": "Files: ", "match": "startsWith", "within": { "button": "Add" } }"""), scope, elements);
        var selector = shorthand.Selectors.Single();
        Assert.Equal(("Text", "Files: ", NameMatch.StartsWith), (selector.Type, selector.Name, selector.Match));
        Assert.Equal("the Add button", selector.Within!.What);
        Assert.True(selector.Matches(null, "Files: 3 added, 0 replaced", "text", null, null));
    }

    [Fact]
    public void Every_control_a_step_names_is_found_however_deep()
    {
        var step = (JsonObject)JsonNode.Parse("""
            { "repeat": 2, "steps": [
                { "menu": ["Delete"], "on": "orderRow" },
                { "if": { "shows": "warning" }, "steps": [{ "click": "ok" }], "else": [{ "drag": "a", "to": "b" }] },
                { "checkpoint": "Done", "checks": [{ "says": "status", "equals": "Saved" }, { "equals": "${kept}", "to": "Saved" }] }
            ] }
            """)!;

        var found = ScriptVocabulary.Targets(step, "steps/0").Select(target => $"{target.Path}={target.Owner[target.Key]}");

        Assert.Equal([
            "steps/0/steps/0/on=orderRow", "steps/0/steps/1/if/shows=warning",
            "steps/0/steps/1/steps/0/click=ok", "steps/0/steps/1/else/0/drag=a", "steps/0/steps/1/else/0/to=b",
            "steps/0/steps/2/checks/0/says=status",
        ], found);
    }

    [Fact]
    public void The_demo_suite_loads()
    {
        var suite = Suite.Load(Demo);
        Assert.Equal(["save-a-name", "delete-it"], suite.Cases.Select(item => item.Id));
        Assert.Equal(["save-a-name"], suite.Cases[1].Needs);
        Assert.Equal("30", suite.Macros["waitForStatusBar"].Parameters["within"]);
        Assert.Equal(["delete-it", "save-a-name"], suite.Select("2,save-a-name").Select(item => item.Id));
        Assert.Equal(2, suite.Select("1-2").Count);
    }

    [Fact]
    public void Loading_reports_every_mistake_in_the_scripts_at_once()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(folder, "cases"));
        File.WriteAllText(Path.Combine(folder, "suite.json"), """{ "name": "Broken", "app": { "exe": "x.exe", "window": "X" } }""");
        File.WriteAllText(Path.Combine(folder, "cases", "01-one.json"), """
            {
              "name": "One",
              "needs": ["zero"],
              "steps": [
                { "click": "saveButton" },
                { "clik": { "button": "Save" } },
                { "click": { "button": "Save" }, "type": "nameBox" },
                { "call": "openPackage" },
                { "type": { "edit": "Name" }, "txt": "main" },
                { "checkpoint": "Done", "checks": [ { "fileExist": "C:\\x" } ] }
              ]
            }
            """);
        try
        {
            var problems = Assert.Throws<SuiteException>(() => Suite.Load(folder)).Problems;
            Assert.Equal(8, problems.Count);
            Assert.Contains(problems, problem => problem.Contains("needs zero"));
            Assert.Contains(problems, problem => problem.Contains("step 1: there is no element called saveButton"));
            Assert.Contains(problems, problem => problem.Contains("step 2: doesn't say what to do"));
            Assert.Contains(problems, problem => problem.Contains("step 2: has \"clik\""));
            Assert.Contains(problems, problem => problem.Contains("step 3: has click and type"));
            Assert.Contains(problems, problem => problem.Contains("step 4: there is no macro called openPackage"));
            Assert.Contains(problems, problem => problem.Contains("step 5: type doesn't take \"txt\""));
            Assert.Contains(problems, problem => problem.Contains("step 6: a check is one of"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Loading_reports_what_would_only_go_wrong_in_the_middle_of_a_run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(folder, "cases"));
        Directory.CreateDirectory(Path.Combine(folder, "macros"));
        File.WriteAllText(Path.Combine(folder, "suite.json"), """{ "name": "Broken", "app": { "exe": "x.exe", "window": "X" } }""");
        File.WriteAllText(Path.Combine(folder, "elements.json"), """{ "saveButton": { "type": "Buton", "name": "Save" } }""");
        File.WriteAllText(Path.Combine(folder, "macros", "addOrder.json"), """{ "params": { "customer": {}, "quantity": { "default": "1" } }, "steps": [] }""");
        File.WriteAllText(Path.Combine(folder, "cases", "01-one.json"), """
            {
              "name": "One",
              "needs": ["two"],
              "steps": [
                { "click": { "type": "splitbutton", "name": "Export" } },
                { "click": { "type": "${kind}", "name": "Export" } },
                { "click": { "button": "Save", "match": "begins" } },
                { "call": "addOrder", "with": { "quantity": "2" } },
                { "call": "addOrder", "with": { "customer": "Ada" } }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(folder, "cases", "02-two.json"), """{ "name": "Two", "steps": [] }""");
        try
        {
            var problems = Assert.Throws<SuiteException>(() => Suite.Load(folder)).Problems;
            Assert.Equal(4, problems.Count);
            Assert.Contains(problems, problem => problem.Contains("elements.json, saveButton: there is no control type Buton"));
            Assert.Contains(problems, problem => problem.Contains("needs two, which runs after it"));
            Assert.Contains(problems, problem => problem.Contains("step 3: match is exact, startsWith or contains, not begins"));
            Assert.Contains(problems, problem => problem.Contains("step 4: addOrder needs customer"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
