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
    public void A_script_step_runs_its_ps1_file_from_the_suite_with_its_args_and_a_wait_waits()
    {
        var tools = Path.Combine(_folder, "suite", "tools");
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(tools, "Hello.ps1"), "Write-Output \"Hello $($args[0])\"");

        var (view, results) = Run("""{ "script": "tools/Hello.ps1", "args": ["Ada"], "as": "hello" }, { "wait": 0.1 }""");

        var file = Path.Combine(tools, "Hello.ps1");
        Assert.Equal(ActivityKind.Command, view.Kinds[0]);
        Assert.Equal($"start powershell.exe -NoProfile -ExecutionPolicy Bypass -File {Quoted(file)} Ada", view.Commands[0]);
        Assert.Contains("Wait \"0.1 s\"", view.Waits);
        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
    }

    private static string Quoted(string path) => path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    [Fact]
    public void A_powershell_step_without_a_say_is_still_shown_with_what_it_printed()
    {
        var machine = new FakeMachine();
        machine.Scripts["Get-Thing"] = new ProcessOutput(1, "first\nit broke");

        var (view, results) = Run("""{ "powershell": ["Get-Thing", "Write-Output done"], "args": ["a b"] }""", machine);

        Assert.Equal("Run PowerShell", view.Shown[1]);
        Assert.Equal(ActivityKind.Command, view.Kinds.Single());
        Assert.Equal(["start powershell \"a b\"", "out first", "err it broke", "exit 1"], view.Commands);
        Assert.Equal(CaseStatus.Failed, view.Ended.Single().Status);
        Assert.Contains("OUTPUT  it broke", File.ReadAllText(results.LogPath));
        var summary = File.ReadAllText(Path.Combine(results.Folder, "summary.txt"));
        Assert.Contains("\r\n            it broke\r\n", summary);
        Assert.Contains("1 case: 0 passed, 1 failed", summary);
    }

    [Fact]
    public void A_step_that_fails_is_marked_but_a_failed_check_is_left_to_its_checkpoint()
    {
        var machine = new FakeMachine();
        machine.Scripts["Get-Thing"] = new ProcessOutput(2, "nothing there");

        var (view, results) = Run("""
            { "powershell": "Get-Thing" },
            { "checkpoint": "It's there", "checks": [ { "equals": "a", "to": "b", "label": "a is b" } ] }
            """, machine);

        Assert.Equal(["A PowerShell step failed with exit code 2: nothing there"], view.StepFailures);
        Assert.Equal(2, results.Cases.Single().Failures.Count);
    }

    [Fact]
    public void A_pick_step_names_the_file_it_picks_and_the_picker()
    {
        var (view, _) = Run("""{ "pickFile": "Open package", "path": "packages/Contoso.Orders-1.0.0.zip" }""");

        Assert.Equal("Pick Contoso.Orders-1.0.0.zip in Open package", view.Shown[1]);
        Assert.Equal([PartKind.Verb, PartKind.File, PartKind.Word, PartKind.Window], view.Steps[0].Parts.Select(part => part.Kind));
        Assert.Equal(ActivityKind.File, view.Kinds.Single());
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
        Assert.Equal(["Look for Save", "Wait statusText starts with \"Saved\""], view.Waits);
        var saved = Assert.Single(view.Waited);
        Assert.Equal("Saved.", saved.Detail);
        Assert.Equal(PartKind.Control, saved.Parts[1].Kind);
        Assert.Contains("AutomationId  StatusText", saved.Parts[1].Tip, StringComparison.Ordinal);
        Assert.Equal(CaseStatus.Passed, view.Ended.Single().Status);
    }
}
