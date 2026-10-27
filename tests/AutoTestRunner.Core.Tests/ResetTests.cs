using System.Text.Json.Nodes;
using AutoTestRunner.Building;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>Cases that start after another's reset, such as everything after signing in, without starting the app again.</summary>
public sealed class ResetTests : IDisposable
{
    private const string LogIn = """
        { "name": "Log in", "steps": [ { "startApp": {} }, { "click": { "button": "Log in" } }, { "set": { "user": "ada" } }, { "checkpoint": "Signed in" } ],
          "reset": [ { "click": { "button": "Home" } } ] }
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private string Suite(params (string File, string Json)[] cases)
    {
        var suite = Path.Combine(_folder, "suite");
        Directory.CreateDirectory(Path.Combine(suite, "cases"));
        File.WriteAllText(Path.Combine(suite, "suite.json"), """{ "name": "Shop", "app": { "exe": "App.exe", "window": "App" } }""");
        foreach (var (file, json) in cases)
            File.WriteAllText(Path.Combine(suite, "cases", file), json);
        return suite;
    }

    private static string After(string name, string click) =>
        $$"""{ "name": "{{name}}", "afterReset": "log-in", "steps": [ { "click": { "button": "{{click}}" } }, { "checkpoint": "{{name}}" } ] }""";

    private (FakeDriver Driver, FakeRunView View, Results Results) Run(string suite, Func<Scripting.Suite, IReadOnlyList<Case>>? pick = null, Action<FakeDriver>? app = null)
    {
        var loaded = Scripting.Suite.Load(suite);
        var driver = new FakeDriver();
        foreach (var name in new[] { "Log in", "Home", "Orders", "Stock" })
            driver.Add("Button", name);
        app?.Invoke(driver);
        var view = new FakeRunView();
        var results = new Results(Path.Combine(_folder, "results"), console: null);
        new Runner(loaded, driver, new FakeMachine(), view, new Driving.RunControl(unattended: true) { TimeoutScale = 0.01 }, results)
            .Run(pick?.Invoke(loaded) ?? loaded.Cases);
        return (driver, view, results);
    }

    [Fact]
    public void The_cases_after_a_reset_start_from_it_without_starting_the_app_again()
    {
        var (driver, view, results) = Run(Suite(("01-log-in.json", LogIn), ("02-orders.json", After("Orders", "Orders")), ("03-stock.json", After("Stock", "Stock"))));

        Assert.All(results.Cases, result => Assert.Equal(CaseStatus.Passed, result.Status));
        // The reset plays once the case that has it is done, and again before each case after the first.
        Assert.Equal(["start App.exe", "click Log in", "click Home", "click Orders", "click Home", "click Stock"], driver.Done.Where(done => !done.StartsWith("point", StringComparison.Ordinal)));
        Assert.Equal(2, view.Steps.Count(step => step.Kind == ActivityKind.Reset));
        Assert.Contains("Reset to after \"1. Log in\"", view.Shown);
    }

    [Fact]
    public void A_case_picked_on_its_own_plays_the_case_it_starts_after_first_without_its_checkpoint()
    {
        var (driver, view, results) = Run(Suite(("01-log-in.json", LogIn), ("02-orders.json", After("Orders", "Orders"))), suite => [suite.Cases[1]]);

        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
        Assert.Equal(["close all", "start App.exe", "click Log in", "click Home", "click Orders"], driver.Done.Where(done => !done.StartsWith("point", StringComparison.Ordinal)));
        Assert.Contains("First \"1. Log in\"", view.Shown);
        Assert.DoesNotContain("Signed in", string.Join("\n", view.Shown), StringComparison.Ordinal);
    }

    [Fact]
    public void A_reset_that_gets_stuck_starts_the_app_again_and_the_case_carries_on()
    {
        // Orders opens a page with no way home, so the reset after it can't find Home.
        var (driver, view, results) = Run(Suite(("01-log-in.json", LogIn), ("02-orders.json", After("Orders", "Orders")), ("03-stock.json", After("Stock", "Stock"))),
            app: app => app.OnClick["Orders"] = () => app.Controls.RemoveAll(control => control.Name == "Home"));

        Assert.All(results.Cases.Take(2), result => Assert.Equal(CaseStatus.Passed, result.Status));
        // Stock starts again, and Home is still missing, so its start again fails it: the failure is Stock's.
        Assert.Contains("close all", driver.Done);
        Assert.Contains(view.Steps, step => step.Kind == ActivityKind.Reset && step.Text == "Start again from \"1. Log in\"" && step.Detail is not null);
        Assert.Equal(CaseStatus.Failed, results.Cases[2].Status);
    }

    [Fact]
    public void A_reset_that_gets_stuck_once_starts_again_and_passes()
    {
        var count = 0;
        var (driver, _, results) = Run(Suite(("01-log-in.json", LogIn), ("02-orders.json", After("Orders", "Orders")), ("03-stock.json", After("Stock", "Stock"))),
            app: app =>
            {
                app.OnClick["Orders"] = () => app.Controls.RemoveAll(control => control.Name == "Home");
                app.OnClick["Log in"] = () =>
                {
                    if (++count == 2)
                        app.Add("Button", "Home");
                };
            });

        Assert.All(results.Cases, result => Assert.Equal(CaseStatus.Passed, result.Status));
        Assert.Equal(["start App.exe", "click Log in", "click Home", "click Orders", "close all", "start App.exe", "click Log in", "click Home", "click Stock"],
            driver.Done.Where(done => !done.StartsWith("point", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_cases_after_a_reset_are_skipped_when_the_case_with_it_fails()
    {
        var (_, view, results) = Run(Suite(("01-log-in.json", LogIn.Replace("{ \"checkpoint\": \"Signed in\" }", "{ \"checkpoint\": \"Signed in\", \"checks\": [ { \"equals\": \"${user}\", \"to\": \"grace\" } ] }", StringComparison.Ordinal)), ("02-orders.json", After("Orders", "Orders"))),
            suite => suite.Cases);

        Assert.Equal(CaseStatus.Failed, results.Cases[0].Status);
        Assert.Equal(CaseStatus.Skipped, results.Cases[1].Status);
        Assert.Contains("It starts after 1. Log in, which didn't pass, so the app can't be got there.", view.Shown);
    }

    [Fact]
    public void A_case_after_a_reset_has_what_the_case_with_it_kept()
    {
        var orders = """{ "name": "Orders", "afterReset": "log-in", "steps": [ { "type": { "button": "Orders" }, "text": "${user}" } ] }""";
        var (driver, _, results) = Run(Suite(("01-log-in.json", LogIn), ("02-orders.json", orders)));

        Assert.Equal(CaseStatus.Passed, results.Cases[1].Status);
        Assert.Contains("type ada", driver.Done);
    }

    [Theory]
    [InlineData("\"afterReset\": \"sign-in\"", "02-orders.json: afterReset is sign-in, and there is no case with that id.")]
    [InlineData("\"afterReset\": \"orders\"", "02-orders.json: afterReset is orders, which runs after it. Cases run in file order, so give it a lower number.")]
    [InlineData("\"afterReset\": \"log-in\", \"needs\": [\"log-in\"]", "02-orders.json: has needs and afterReset. A case starts at the end of others, or after a reset, not both.")]
    public void A_case_after_a_reset_that_cant_be_says_why(string settings, string problem)
    {
        var suite = Suite(("01-log-in.json", LogIn), ("02-orders.json", $$"""{ "name": "Orders", {{settings}}, "steps": [ { "click": { "button": "Orders" } } ] }"""));

        Assert.Contains(problem, Assert.Throws<SuiteException>(() => Scripting.Suite.Load(suite)).Problems);
    }

    [Fact]
    public void A_case_after_one_with_no_reset_or_a_reset_with_a_checkpoint_says_why()
    {
        var suite = Suite(
            ("01-log-in.json", """{ "name": "Log in", "steps": [ { "click": { "button": "Log in" } } ], "reset": [ { "checkpoint": "Home" } ] }"""),
            ("02-start.json", """{ "name": "Start", "steps": [ { "startApp": {} } ] }"""),
            ("03-orders.json", """{ "name": "Orders", "afterReset": "start", "steps": [ { "click": { "button": "Orders" } } ] }"""));

        var problems = Assert.Throws<SuiteException>(() => Scripting.Suite.Load(suite)).Problems;
        Assert.Contains("01-log-in.json: a reset step can't be a checkpoint. The case's checkpoint goes at the end of its steps.", problems);
        Assert.Contains("03-orders.json: afterReset is start, which has no reset steps to take the app back.", problems);
    }

    [Fact]
    public void A_new_case_after_a_reset_is_written_with_it_and_its_own_reset_steps_go_after_its_steps()
    {
        var suite = Scripting.Suite.Load(Suite(("01-log-in.json", LogIn)));
        var draft = new CaseDraft { Name = "Orders", AfterReset = "log-in", CheckpointTitle = "Orders shown" };
        draft.Steps.Add(StepMaker.FromJson(new JsonObject { ["click"] = new JsonObject { ["button"] = "Orders" } }, DraftStepState.Played));
        var back = StepMaker.FromJson(new JsonObject { ["click"] = new JsonObject { ["button"] = "Back" } }, DraftStepState.Played);
        back.Reset = true;
        draft.Steps.Add(back);
        draft.Steps.Add(StepMaker.Key("Escape"));

        var text = File.ReadAllText(CaseWriter.Save(suite, draft)).ReplaceLineEndings("\n");

        Assert.Equal("""
            {
              "$schema": "https://raw.githubusercontent.com/connorstevenson44/AutoTestRunner/main/schema/case.schema.json",
              "name": "Orders",
              "afterReset": "log-in",
              "steps": [
                { "click": { "button": "Orders" } },
                { "checkpoint": "Orders shown" }
              ],
              "reset": [
                { "click": { "button": "Back" } },
                { "key": "Escape", "say": "Press Escape" }
              ]
            }

            """, text);
        var saved = Scripting.Suite.Load(suite.Folder).Cases[1];
        Assert.Equal("log-in", saved.AfterReset);
        Assert.Equal(2, saved.Reset.Count);
    }

    [Fact]
    public void A_case_opened_to_change_it_has_its_reset_steps_last_and_saves_them_where_they_were()
    {
        var folder = Suite(("01-log-in.json", LogIn));
        var suite = Scripting.Suite.Load(folder);
        var draft = CaseDraft.Load(suite.Cases[0]);
        Assert.Equal([false, false, false, true], draft.Steps.Select(step => step.Reset));

        // Unchanged, the file stays as it was; a reset step added goes in its reset.
        var before = File.ReadAllText(suite.Cases[0].File);
        CaseWriter.Save(suite, draft);
        Assert.Equal(before, File.ReadAllText(suite.Cases[0].File));
        draft.Steps.Add(StepMaker.Key("Escape"));
        CaseWriter.Save(suite, draft);
        var saved = Scripting.Suite.Load(folder).Cases[0];
        Assert.Equal(4, saved.Steps.Count);
        Assert.Equal("""[{"click":{"button":"Home"}},{"key":"Escape","say":"Press Escape"}]""", saved.Reset.ToJsonString());

        // With the mark taken off, the steps are the case's own, and its reset is empty.
        draft = CaseDraft.Load(saved);
        draft.Steps.ForEach(step => step.Reset = false);
        CaseWriter.Save(suite, draft);
        saved = Scripting.Suite.Load(folder).Cases[0];
        Assert.Equal(6, saved.Steps.Count);
        Assert.Empty(saved.Reset);
    }
}
