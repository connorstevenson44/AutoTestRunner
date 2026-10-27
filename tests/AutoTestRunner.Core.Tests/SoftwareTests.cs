using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>The software a suite needs: found without changing anything, and installed only as asked.</summary>
public sealed class SoftwareTests : IDisposable
{
    private const string Software = """
        "software": {
          "sqlExpress": { "title": "SQL Server Express", "winget": "Microsoft.SQLServer.2022.Express", "version": "16.0" },
          "rabbitMq": { "title": "RabbitMQ", "winget": "RabbitMQ.RabbitMQServer", "version": "3.13", "present": { "service": "RabbitMQ", "state": "Running" } },
          "msmq": { "title": "MSMQ", "windowsFeature": "MSMQ-Server" },
          "ibmMq": { "title": "IBM MQ", "script": "install-mq.ps1", "args": ["-Version", "${version}"], "version": "9.4", "present": { "fileExists": "C:/IBM/MQ/bin/dspmq.exe" } }
        }
        """;

    // What winget list prints, as it lays it out.
    private const string Listed = """
        Name                        Id                               Version       Source
        -------------------------------------------------------------------------------
        Microsoft SQL Server 2022   Microsoft.SQLServer.2022.Express 16.0.1000.6   winget
        RabbitMQ Server             RabbitMQ.RabbitMQServer          3.12.1        winget
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private Suite Load(string software = Software)
    {
        var suite = Path.Combine(_folder, "suite");
        Directory.CreateDirectory(Path.Combine(suite, "cases"));
        File.WriteAllText(Path.Combine(suite, "suite.json"), $$"""{ "name": "Shop", "app": { "exe": "App.exe", "window": "App" }, {{software}} }""");
        File.WriteAllText(Path.Combine(suite, "cases", "01-start.json"), """{ "name": "Start", "steps": [ { "startApp": {} } ] }""");
        return Suite.Load(suite);
    }

    [Fact]
    public void Each_is_found_by_winget_windows_or_its_present_check_without_installing_anything()
    {
        var suite = Load();
        var machine = new FakeMachine();
        machine.Programs["winget list"] = new(0, Listed);
        machine.Scripts["(Get-CimInstance Win32_OptionalFeature -Filter \"Name='MSMQ-Server'\").InstallState"] = new(0, "2\r\n");

        var states = Running.Software.Detect(suite, machine, new VariableScope());

        Assert.Equal(["sqlExpress", "rabbitMq", "msmq", "ibmMq"], states.Select(state => state.Item.Name));
        Assert.Equal([SoftwareStatus.Installed, SoftwareStatus.OtherVersion, SoftwareStatus.Missing, SoftwareStatus.Missing], states.Select(state => state.Status));
        Assert.Equal("SQL Server Express: installed, 16.0.1000.6", states[0].Line);
        Assert.Equal("RabbitMQ: 3.12.1 installed, wants 3.13", states[1].Line);
        Assert.DoesNotContain(machine.Ran, line => line.StartsWith("winget install", StringComparison.Ordinal));
    }

    [Fact]
    public void A_package_winget_doesnt_list_is_missing_unless_its_present_check_passes()
    {
        var suite = Load();
        var machine = new FakeMachine();
        machine.Programs["winget list"] = new(-1978335212, "No installed package found matching input criteria.");
        machine.Services["RabbitMQ"] = "Running";

        var states = Running.Software.Detect(suite, machine, new VariableScope());

        Assert.Equal(SoftwareStatus.Missing, states[0].Status);
        Assert.Equal("SQL Server Express: not installed, wants 16.0", states[0].Line);
        Assert.Equal(SoftwareStatus.Installed, states[1].Status);
    }

    [Fact]
    public void Without_winget_a_package_cant_be_told_and_says_why()
    {
        var machine = new FakeMachine { Missing = { "winget" } };

        var state = Running.Software.Detect(Load(), machine, new VariableScope())[0];

        Assert.Equal(SoftwareStatus.Unknown, state.Status);
        Assert.Contains("winget isn't on this computer", state.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void Installing_uses_winget_at_the_version_wanted_the_feature_and_the_script_with_its_version()
    {
        var suite = Load();
        var machine = new FakeMachine();
        var lines = new List<string>();

        foreach (var item in suite.Software.Values)
            Running.Software.Install(item, machine, suite.Folder, new VariableScope(), (line, _) => lines.Add(line));

        Assert.Equal("winget install --id Microsoft.SQLServer.2022.Express --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity --version 16.0", machine.Ran[0]);
        Assert.EndsWith("-Version 9.4", machine.Ran[^1], StringComparison.Ordinal);
        Assert.Contains("install-mq.ps1", machine.Ran[^1], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith("winget install --id RabbitMQ.RabbitMQServer", StringComparison.Ordinal));
    }

    [Fact]
    public void Picking_by_name_missing_or_all_and_a_name_it_doesnt_have_says_what_it_has()
    {
        var states = Load().Software.Values.Select((item, index) => new SoftwareState(item, index % 2 == 0 ? SoftwareStatus.Installed : SoftwareStatus.Missing)).ToList();

        Assert.Equal(["rabbitMq", "ibmMq"], Running.Software.Picked(states, "missing").Select(state => state.Item.Name));
        Assert.Equal(4, Running.Software.Picked(states, "all").Count);
        Assert.Equal(["sqlExpress", "msmq"], Running.Software.Picked(states, "msmq, sqlExpress").Select(state => state.Item.Name));
        var problem = Assert.Throws<SuiteException>(() => Running.Software.Picked(states, "oracle")).Message;
        Assert.Contains("There is no software called oracle in suite.json. It is one of: sqlExpress, rabbitMq, msmq, ibmMq.", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "title": "X" }""", "suite.json: software x needs one of winget, a package id; windowsFeature, such as MSMQ-Server; or script, a .ps1 or .cs file that installs it.")]
    [InlineData("""{ "winget": "A.B", "windowsFeature": "C" }""", "suite.json: software x needs one of winget")]
    [InlineData("""{ "script": "install.ps1" }""", "suite.json: software x is installed by a script, so it needs present")]
    [InlineData("""{ "windowsFeature": "MSMQ-Server", "version": "1" }""", "suite.json: software x is a Windows feature, which has no version.")]
    [InlineData("""{ "winget": "A.B", "present": { "servce": "A" } }""", "suite.json: software x's present")]
    public void Software_that_cant_be_found_or_installed_says_why(string item, string problem)
    {
        var problems = Assert.Throws<SuiteException>(() => Load($$"""  "software": { "x": {{item}} }""")).Problems;

        Assert.Contains(problems, line => line.StartsWith(problem, StringComparison.Ordinal));
    }
}
