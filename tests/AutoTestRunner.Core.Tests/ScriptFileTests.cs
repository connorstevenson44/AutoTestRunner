using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>Script files a suite runs: .ps1 with PowerShell, and .cs with dotnet run.</summary>
public sealed class ScriptFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private string Suite(string steps, string reset = "")
    {
        var suite = Path.Combine(_folder, "suite");
        Directory.CreateDirectory(Path.Combine(suite, "cases"));
        Directory.CreateDirectory(Path.Combine(suite, "tools"));
        File.WriteAllText(Path.Combine(suite, "tools", "Hello.cs"), "Console.WriteLine($\"Hello {args[0]}\");");
        File.WriteAllText(Path.Combine(suite, "suite.json"), $$"""{ "name": "Scripts", "app": { "exe": "App.exe", "window": "App" }{{reset}} }""");
        File.WriteAllText(Path.Combine(suite, "cases", "01-scripts.json"), $$"""{ "name": "Scripts", "steps": [ {{steps}} ] }""");
        return suite;
    }

    private (FakeRunView View, Results Results) Run(string steps, FakeMachine machine)
    {
        var loaded = Scripting.Suite.Load(Suite(steps));
        var view = new FakeRunView();
        var results = new Results(Path.Combine(_folder, "results"), console: null);
        new Runner(loaded, new FakeDriver(), machine, view, new RunControl(unattended: true) { TimeoutScale = 0.01 }, results).Run(loaded.Cases);
        return (view, results);
    }

    [Fact]
    public void A_cs_script_step_runs_its_file_with_dotnet_run_and_its_args_after_two_dashes()
    {
        var (view, results) = Run("""{ "script": "tools/Hello.cs", "args": ["Ada"], "as": "hello" }""", new FakeMachine());

        var file = Path.Combine(_folder, "suite", "tools", "Hello.cs");
        Assert.Equal(ActivityKind.Command, view.Kinds.Single());
        Assert.Equal($"start dotnet run --file {(file.Contains(' ', StringComparison.Ordinal) ? $"\"{file}\"" : file)} -v q -- Ada", view.Commands[0]);
        Assert.Equal("Run Hello.cs", view.Shown[1]);
        Assert.Equal(CaseStatus.Passed, results.Cases.Single().Status);
    }

    [Fact]
    public void A_cs_script_on_a_machine_without_the_sdk_says_what_to_install()
    {
        var machine = new FakeMachine();
        machine.Programs["dotnet"] = new ProcessOutput(1, "The command could not be loaded.\nNo .NET SDKs were found.");
        var (view, _) = Run("""{ "script": "tools/Hello.cs", "args": ["Ada"] }""", machine);
        Assert.Contains(ScriptFile.NeedsSdk, Failures(view), StringComparison.Ordinal);

        var without = new FakeMachine();
        without.Missing.Add("dotnet");
        (view, _) = Run("""{ "script": "tools/Hello.cs", "args": ["Ada"] }""", without);
        Assert.Contains(ScriptFile.NeedsSdk, Failures(view), StringComparison.Ordinal);
    }

    private static string Failures(FakeRunView view) => string.Join("\n", view.StepFailures.Concat(view.Ended.SelectMany(result => result.Failures)));

    [Fact]
    public void A_script_is_a_ps1_or_a_cs_file()
    {
        var problem = Assert.Single(Assert.Throws<SuiteException>(() => Scripting.Suite.Load(Suite("""{ "script": "tools/Hello.py" }"""))).Problems);
        Assert.Contains("a script is a .ps1 or a .cs file, not tools/Hello.py", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reset_script_can_be_cs_under_its_new_name_and_powershell_still_works()
    {
        Assert.Equal("tools/Reset.cs", Scripting.Suite.Load(Suite("", """, "reset": { "script": "tools/Reset.cs" }""")).Reset.Script);
        Assert.Equal("tools/Reset.ps1", Scripting.Suite.Load(Suite("", """, "reset": { "powershell": "tools/Reset.ps1" }""")).Reset.Script);
    }

    [Fact]
    public void A_cs_script_really_runs_with_dotnet_and_gets_its_args()
    {
        var file = Path.Combine(Suite(""), "tools", "Hello.cs");
        var output = ScriptFile.Run(new WindowsMachine(), file, ["Ada Lovelace"], TimeSpan.FromMinutes(3));
        Assert.Equal(0, output.ExitCode);
        Assert.Equal("Hello Ada Lovelace", output.Output.Trim());
    }
}
