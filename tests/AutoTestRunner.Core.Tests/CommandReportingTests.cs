using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>What a run shows the person: the kind of each step, what it waits for, and the commands it runs.</summary>
public sealed class CommandReportingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private (FakeRunView View, Results Results) Run(string steps, FakeMachine? machine = null, FakeDriver? driver = null)
    {
        var suite = Path.Combine(_folder, "suite");
        Directory.CreateDirectory(Path.Combine(suite, "cases"));
        File.WriteAllText(Path.Combine(suite, "suite.json"), """{ "name": "Shown", "app": { "exe": "App.exe", "window": "App", "status": "statusText" } }""");
        File.WriteAllText(Path.Combine(suite, "elements.json"), """{ "statusText": { "id": "StatusText" } }""");
        File.WriteAllText(Path.Combine(suite, "cases", "01-shown.json"), $$"""{ "name": "Shown", "steps": [ {{steps}} ] }""");
        var loaded = Suite.Load(suite);
        var view = new FakeRunView();
        var results = new Results(Path.Combine(_folder, "results"), console: null);
        var control = new RunControl(unattended: true) { TimeoutScale = 0.01 };
        new Runner(loaded, driver ?? new FakeDriver(), machine ?? new FakeMachine(), view, control, results).Run(loaded.Cases);
        return (view, results);
    }

    [Fact]
    public void A_run_step_shows_its_command_line_what_it_prints_and_how_it_ended()
    {
        var (view, results) = Run("""{ "run": "tool.exe", "args": ["deploy", "C:\\My Packages\\a.zip"], "env": { "API_KEY": "secret-123" }, "as": "out" }""");

        Assert.Equal(ActivityKind.Command, view.Kinds.Single());
        Assert.Equal(["start API_KEY=••• tool.exe deploy \"C:\\My Packages\\a.zip\"", "out tool.exe deploy C:\\My Packages\\a.zip", "exit 0"], view.Commands);
        var log = File.ReadAllText(results.LogPath);
        Assert.Contains("RUN     API_KEY=••• tool.exe deploy", log);
        Assert.Contains("OUTPUT  tool.exe deploy C:\\My Packages\\a.zip", log);
        Assert.Contains("EXIT    0, after", log);
        Assert.DoesNotContain("secret-123", log);
    }

    [Fact]
    public void A_powershell_step_without_a_say_is_still_shown_with_what_it_printed()
    {
        var machine = new FakeMachine();
        machine.Scripts["Get-Thing"] = new ProcessOutput(1, "first\nit broke");

        var (view, results) = Run("""{ "powershell": ["Get-Thing", "Write-Output done"], "args": ["a b"] }""", machine);

        Assert.Equal("Run a PowerShell script", view.Shown[1]);
        Assert.Equal(ActivityKind.Command, view.Kinds.Single());
        Assert.Equal(["start powershell \"a b\"", "out first", "err it broke", "exit 1"], view.Commands);
        Assert.Equal(CaseStatus.Failed, view.Ended.Single().Status);
        Assert.Contains("OUTPUT  it broke", File.ReadAllText(results.LogPath));
    }

    [Fact]
    public void Steps_say_what_kind_they_are_and_waits_say_what_they_wait_for()
    {
        var app = new FakeDriver();
        var status = app.Add("Text", "Ready", id: "StatusText");
        app.Add("Button", "Save");
        app.OnClick["Save"] = () => status.Name = "Saved.";

        var (view, _) = Run("""
            { "click": { "button": "Save" } },
            { "waitText": "statusText", "startsWith": "Saved" },
            { "checkpoint": "Saved", "checks": [ { "equals": "a", "to": "a" } ] }
            """, driver: app);

        Assert.Equal([ActivityKind.Click, ActivityKind.Check], view.Kinds);
        Assert.Equal(["Look for the Save button", "Wait for the status text to say \"Saved\""], view.Waits);
        Assert.Equal(CaseStatus.Passed, view.Ended.Single().Status);
    }
}
