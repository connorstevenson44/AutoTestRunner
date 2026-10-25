using Walkthrough.Driving;
using Walkthrough.Running;
using Walkthrough.Scripting;

namespace Walkthrough.Core.Tests;

/// <summary>The steps that go round more than once or choose: whileShows, if and click's whileEnabled.</summary>
public class FlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "WalkthroughTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    // A suite of one case with these steps, in a folder of its own.
    private Suite OneCase(string steps, string macros = "")
    {
        var suite = Path.Combine(_folder, "suite");
        Directory.CreateDirectory(Path.Combine(suite, "cases"));
        File.WriteAllText(Path.Combine(suite, "suite.json"), """
            { "name": "Flow", "app": { "exe": "App.exe", "window": "App", "status": "statusText" },
              "helpers": { "stepNumber": { "regex": "(\\d+) of" }, "stepCount": { "regex": "of (\\d+)" } } }
            """);
        File.WriteAllText(Path.Combine(suite, "elements.json"), """{ "statusText": { "id": "StatusText" } }""");
        File.WriteAllText(Path.Combine(suite, "cases", "01-flow.json"), $$"""{ "name": "Flow", "steps": [ {{steps}} ] }""");
        if (macros.Length > 0)
        {
            Directory.CreateDirectory(Path.Combine(suite, "macros"));
            File.WriteAllText(Path.Combine(suite, "macros", "step.json"), macros);
        }
        return Suite.Load(suite);
    }

    private (CaseResult Result, FakeHost Host) Run(Suite suite, FakeDriver driver)
    {
        var host = new FakeHost();
        var results = new Results(Path.Combine(_folder, "results"), console: null);
        var control = new RunControl(unattended: true) { TimeoutScale = 0.01 };
        new Runner(suite, driver, new FakeMachine(), host, control, results).Run(suite.Cases);
        return (results.Cases.Single(), host);
    }

    // A removal that asks about each of its steps in a window of its own, then says so in the status bar.
    private static FakeDriver Removal(int steps, List<string> answers)
    {
        var app = new FakeDriver();
        var status = app.Add("Text", "Ready", id: "StatusText");
        var window = new List<FakeControl>();
        var number = 1;
        void Ask()
        {
            window.Add(app.Add("Window", "Remove install"));
            window.Add(app.Add("Text", $"{number} of {steps}", id: "Counter"));
        }
        void Answer(string answer)
        {
            answers.Add($"{number} {answer}");
            app.Controls.RemoveAll(window.Contains);
            window.Clear();
            if (answer == "stop" || ++number > steps)
                status.Name = answer == "stop" ? "Removal stopped." : "Removed C:\\Apps\\spare.";
            else
                Ask();
        }
        app.Add("Button", "Remove");
        app.Add("Button", "Run this step");
        app.Add("Button", "Skip this step");
        app.Add("Button", "Stop removing");
        app.OnClick["Remove"] = Ask;
        app.OnClick["Run this step"] = () => Answer("run");
        app.OnClick["Skip this step"] = () => Answer("skip");
        app.OnClick["Stop removing"] = () => Answer("stop");
        return app;
    }

    private const string RemoveLastOnly = """
        { "click": { "button": "Remove" } },
        { "whileShows": { "window": "Remove install" }, "untilText": "statusText", "matches": "^(Removed |Removal stopped)", "as": "done",
          "steps": [
            { "find": { "id": "Counter" }, "as": "counter" },
            { "if": { "equals": "${stepNumber(counter)}", "to": "${stepCount(counter)}" },
              "steps": [ { "click": { "button": "Run this step" } } ],
              "else": [ { "click": { "button": "Skip this step" } } ] }
          ] },
        { "checkpoint": "Removed", "checks": [ { "equals": "${done}", "to": "Removed C:\\Apps\\spare." } ] }
        """;

    [Fact]
    public void WhileShows_runs_its_steps_for_each_window_until_the_status_says_it_is_done()
    {
        var answers = new List<string>();

        var (result, host) = Run(OneCase(RemoveLastOnly), Removal(3, answers));

        Assert.Equal(CaseStatus.Passed, result.Status);
        Assert.Equal(["1 skip", "2 skip", "3 run"], answers);
        Assert.True(host.Checks.Single().Passed);
    }

    [Fact]
    public void WhileShows_is_stuck_when_nothing_happens()
    {
        var driver = Removal(3, []);
        driver.OnClick.Remove("Remove");

        var (result, _) = Run(OneCase(RemoveLastOnly), driver);

        Assert.Equal(CaseStatus.Failed, result.Status);
        Assert.Contains("^(Removed |Removal stopped)", result.Failures.Single());
    }

    [Fact]
    public void If_can_stop_at_the_first_step_through_a_macro()
    {
        var answers = new List<string>();
        var suite = OneCase("""
            { "click": { "button": "Remove" } },
            { "whileShows": { "window": "Remove install" }, "untilText": "statusText", "startsWith": "Removal stopped",
              "steps": [ { "call": "step", "with": { "choice": "stop" } } ] }
            """, """
            { "params": { "choice": {} },
              "steps": [
                { "if": { "equals": "${choice}", "to": "stop" }, "steps": [ { "click": { "button": "Stop removing" } } ],
                  "else": [ { "click": { "button": "Run this step" } } ] } ] }
            """);

        var (result, _) = Run(suite, Removal(4, answers));

        Assert.Equal(CaseStatus.Passed, result.Status);
        Assert.Equal(["1 stop"], answers);
    }

    [Fact]
    public void WhileEnabled_clicks_until_the_control_is_greyed_out()
    {
        var app = new FakeDriver();
        var up = app.Add("Button", "Up", helpText: "Up one folder");
        var depth = 3;
        app.OnClick["Up"] = () => up.IsEnabled = --depth > 0;

        var (result, _) = Run(OneCase("""{ "click": { "button": "Up" }, "whileEnabled": true }"""), app);

        Assert.Equal(CaseStatus.Passed, result.Status);
        Assert.Equal(0, depth);
        Assert.Equal(3, app.Done.Count(done => done == "click Up"));
    }

    [Fact]
    public void WhileEnabled_does_nothing_when_the_control_isnt_there()
    {
        var (result, _) = Run(OneCase("""{ "click": { "button": "Up" }, "whileEnabled": true }"""), new FakeDriver());

        Assert.Equal(CaseStatus.Passed, result.Status);
    }

    [Fact]
    public void WaitText_reads_what_is_in_a_text_box_and_can_match_a_regex()
    {
        var app = new FakeDriver();
        app.Add("Edit", "Log", id: "LogText").Value = "09:00 heartbeat region West";

        var (result, _) = Run(OneCase("""{ "waitText": { "id": "LogText" }, "matches": "region \\w+$" }"""), app);

        Assert.Equal(CaseStatus.Passed, result.Status);
    }

    [Fact]
    public void Lacks_and_shows_checks_pass_and_fail_as_they_should()
    {
        var app = new FakeDriver();
        app.Add("Button", "heartbeat.1.log, no matches");

        var (result, host) = Run(OneCase("""
            { "set": { "printed": "Deploy finished. Key: ****" } },
            { "checkpoint": "Checked", "checks": [
              { "lacks": "${printed}", "text": "pk-test-123" },
              { "lacks": "${printed}", "text": "****" },
              { "shows": { "button": "heartbeat.1.log, no matches" } },
              { "shows": { "button": "heartbeat.2.log, 2 matches" } } ] }
            """), app);

        Assert.Equal([true, false, true, false], host.Checks.Select(check => check.Passed));
        Assert.Equal("It shows the heartbeat.2.log, 2 matches button: it isn't there", result.Failures[1]);
    }

    [Fact]
    public void Match_compares_help_text_as_it_compares_names()
    {
        var selector = new Selector(Name: "Keep the server's", HelpText: "appSettings:BatchSize", Type: "Button", Match: NameMatch.Contains);

        Assert.True(selector.Matches(null, "Keep the server's", "Button", "Keep the server's value for appSettings:BatchSize: 750", null));
        Assert.False(selector.Matches(null, "Keep the server's", "Button", "Keep the server's value for appSettings:Format: csv", null));
    }

    [Fact]
    public void A_check_whose_properties_are_named_like_other_checks_is_still_one_check()
    {
        var problem = Record.Exception(() => OneCase("""
            { "checkpoint": "Checked", "checks": [
              { "jsonValue": "a.json", "path": "Orders:Region", "equals": "west" },
              { "output": "a.exe", "args": ["--check"], "contains": "ok" },
              { "equals": "a", "to": "a" } ] }
            """));

        Assert.Null(problem);
        Assert.Equal("jsonValue", Kinds.Of(new System.Text.Json.Nodes.JsonObject { ["jsonValue"] = "a", ["equals"] = "b" }, Vocabulary.Checks));
        Assert.Null(Kinds.Of(new System.Text.Json.Nodes.JsonObject { ["equals"] = "a", ["contains"] = "b" }, Vocabulary.Checks));
    }

    [Fact]
    public void The_loader_checks_inside_whileShows_and_if()
    {
        var problem = Assert.Throws<SuiteException>(() => OneCase("""
            { "whileShows": { "window": "Remove install" }, "steps": [ { "clik": "x" } ] },
            { "if": { "equalz": "a" }, "steps": [], "else": [ { "find": "noSuchElement" } ] }
            """));

        Assert.Contains(problem.Problems, text => text.Contains("needs untilText"));
        Assert.Contains(problem.Problems, text => text.Contains("\"clik\""));
        Assert.Contains(problem.Problems, text => text.Contains("a check is one of"));
        Assert.Contains(problem.Problems, text => text.Contains("noSuchElement"));
    }
}
