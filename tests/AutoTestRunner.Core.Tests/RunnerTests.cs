using System.Xml.Linq;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

public sealed class RunnerTests : IDisposable
{
    private static readonly string Demo = Path.Combine(AppContext.BaseDirectory, "samples", "demo");
    private readonly string _results = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_results))
            Directory.Delete(_results, recursive: true);
    }

    // The demo app: a name box, Save and a status bar. Save says where its log is and adds a row for the name.
    private static FakeDriver DemoApp()
    {
        var app = new FakeDriver();
        var status = app.Add("Text", "Ready", id: "StatusText");
        app.Add("Edit", "Name", id: "NameBox");
        app.Add("Button", "Save");
        app.OnClick["Save"] = () =>
        {
            status.Name = @"Saved. Log: C:\logs\save.log";
            app.Add("Button", "main", helpText: Path.Combine(Path.GetTempPath(), "AutoTestRunner") + "/demo-data/main");
        };
        return app;
    }

    private (Runner Runner, Results Results) Make(FakeDriver driver, IRunView? view = null, bool unattended = true)
    {
        var suite = Suite.Load(Demo);
        var results = new Results(_results, console: null);
        // Waits that would end in stuck end straight away.
        var control = new RunControl(unattended) { TimeoutScale = 0.01 };
        if (!unattended)
            control.SetSpeed(Speed.Fast);
        return (new Runner(suite, driver, new FakeMachine(), view ?? new ConsoleRunView(), control, results), results);
    }

    [Fact]
    public void A_case_runs_its_steps_and_passes_its_checkpoint()
    {
        var driver = DemoApp();
        var view = new FakeRunView();
        var (runner, results) = Make(driver, view);

        var failed = runner.Run(Suite.Load(Demo).Select("1"));

        Assert.Equal(0, failed);
        Assert.True(driver.Started);
        Assert.Contains("type main", driver.Done);
        Assert.Contains("click Save", driver.Done);
        Assert.All(view.Checks, check => Assert.True(check.Passed, $"{check.Label}: {check.Detail}"));
        Assert.Equal(4, view.Checks.Count);
        Assert.Contains("Type main as the name", view.Shown);
        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
    }

    [Fact]
    public void Results_are_written_as_the_AutoHotkey_runner_wrote_them_plus_junit()
    {
        var (runner, _) = Make(DemoApp());

        runner.Run(Suite.Load(Demo).Select("1"));

        Assert.Contains("PASSED  1. Save a name", File.ReadAllText(Path.Combine(_results, "summary.txt")));
        Assert.Contains("STEP    Click Save", File.ReadAllText(Path.Combine(_results, "run.log")));
        var junit = XDocument.Load(Path.Combine(_results, "junit.xml"));
        Assert.Equal("1", junit.Root!.Element("testsuite")!.Attribute("tests")!.Value);
        Assert.Empty(junit.Descendants("failure"));
    }

    [Fact]
    public void Waiting_for_new_status_ignores_the_text_from_before_the_click()
    {
        var driver = DemoApp();
        // Save does nothing this time, so the bar still says what it said before the click.
        driver.Controls.Single(control => control.AutomationId == "StatusText").Name = "Saved. Log: old";
        driver.OnClick.Remove("Save");
        var (runner, results) = Make(driver);

        runner.Run(Suite.Load(Demo).Select("1"));

        var result = results.Cases.Single();
        Assert.Equal(CaseStatus.Failed, result.Status);
        Assert.Contains("see \"Saved.\" in the status text", result.Failures.Single());
    }

    [Fact]
    public void Unattended_a_missing_control_ends_the_run_with_a_screenshot_and_a_tree_dump()
    {
        var (runner, results) = Make(DemoApp());

        var failed = runner.Run(Suite.Load(Demo).Select("2,1"));

        Assert.Equal(1, failed);
        var result = Assert.Single(results.Cases);
        Assert.Equal("I couldn't find the Delete button.", result.Failures.Single());
        Assert.True(File.Exists(Path.Combine(_results, "01-stuck.png")));
        Assert.Contains("Button \"Save\"", File.ReadAllText(Path.Combine(_results, "01-stuck-uia.txt")));
    }

    [Fact]
    public void Watched_the_person_can_skip_the_rest_of_a_stuck_case_and_carry_on()
    {
        var view = new FakeRunView(StuckAnswer.SkipCase);
        var (runner, results) = Make(DemoApp(), view, unattended: false);

        var failed = runner.Run(Suite.Load(Demo).Select("2,1"));

        Assert.Equal(1, view.StuckCount);
        Assert.Equal([CaseStatus.Skipped, CaseStatus.Passed], results.Cases.Select(result => result.Status));
        Assert.Equal(1, failed);
    }

    [Fact]
    public void Watched_try_again_retries_the_step_after_the_person_fixed_the_app()
    {
        var driver = DemoApp();
        var view = new RetryingView(() => driver.Add("Button", "Delete"));
        var (runner, results) = Make(driver, view, unattended: false);

        runner.Run(Suite.Load(Demo).Select("2"));

        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
        Assert.Contains("click Delete", driver.Done);
    }

    [Fact]
    public void A_greyed_out_button_is_stuck_rather_than_clicked()
    {
        var driver = DemoApp();
        driver.Controls.Single(control => control.Name == "Save").IsEnabled = false;
        var (runner, results) = Make(driver);

        runner.Run(Suite.Load(Demo).Select("1"));

        Assert.Contains("greyed out", results.Cases.Single().Failures.Single());
        Assert.DoesNotContain("click Save", driver.Done);
    }

    [Fact]
    public void Variables_take_the_first_path_that_exists_and_overrides_win()
    {
        var suite = Suite.Load(Demo);
        var scope = Runner.Variables(suite, _results, new Dictionary<string, string> { ["extra"] = "given" });

        Assert.Equal(Path.GetFullPath(Path.Combine(Demo, "bin/Demo.exe")), scope.Get("exe").Text);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "AutoTestRunner") + "/demo-data", scope.Get("dataDir").Text);
        Assert.Equal("given", scope.Get("extra").Text);
    }

    private sealed class RetryingView(Action fix) : IRunView
    {
        public void CaseStarted(int index, int count, Case item) { }
        public void CaseFinished(CaseResult result) { }
        public void ShowStep(string text, ActivityKind kind = ActivityKind.Say, string? command = null) { }
        public void ShowWait(string what) { }
        public void CommandStarted(string line, string? script) { }
        public void CommandOutput(string line, bool error) { }
        public void CommandFinished(int exitCode, TimeSpan took) { }
        public void StepFailed(string message) { }
        public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) { }
        public void ShowCarryingOn(TimeSpan after) { }

        public StuckAnswer AskStuck(string message, string screenshot)
        {
            fix();
            return StuckAnswer.TryAgain;
        }

        public bool AskScriptError(string message) => false;
        public void KeepClear(ScreenRect rect) { }
        public void Finished(string summary, string resultsFolder, bool allPassed) { }
    }
}
