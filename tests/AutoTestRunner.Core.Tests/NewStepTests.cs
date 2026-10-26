using System.IO.Compression;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>
/// The steps that set up the computer without PowerShell, the checks of controls and of the computer, data rows,
/// tags, and the mouse, menu, tree and window steps.
/// </summary>
public sealed class NewStepTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());
    private readonly FakeMachine _machine = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private string Suite => Path.Combine(_folder, "suite");

    // A suite of one case with these steps, and these cases besides when given.
    private Suite OneCase(string steps, params (string File, string Json)[] more)
    {
        Directory.CreateDirectory(Path.Combine(Suite, "cases"));
        File.WriteAllText(Path.Combine(Suite, "suite.json"), """{ "name": "New", "app": { "exe": "App.exe", "window": "App" } }""");
        File.WriteAllText(Path.Combine(Suite, "elements.json"), """{ "orders": { "id": "Orders" }, "bin": { "id": "Bin" } }""");
        File.WriteAllText(Path.Combine(Suite, "cases", "01-one.json"), $$"""{ "name": "One", "steps": [ {{steps}} ] }""");
        foreach (var (file, json) in more)
            File.WriteAllText(Path.Combine(Suite, "cases", file), json);
        return AutoTestRunner.Scripting.Suite.Load(Suite);
    }

    private (CaseResult Result, FakeRunView View) Run(Suite suite, FakeDriver? driver = null)
    {
        var view = new FakeRunView();
        var results = new Results(Path.Combine(_folder, "results"), console: null);
        var control = new RunControl(unattended: true) { TimeoutScale = 0.01 };
        new Runner(suite, driver ?? new FakeDriver(), _machine, view, control, results).Run([suite.Cases[0]]);
        return (results.Cases.Single(), view);
    }

    private static string Json(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    [Fact]
    public void Set_up_steps_copy_delete_write_unzip_and_change_settings()
    {
        var work = Path.Combine(_folder, "work");
        Directory.CreateDirectory(Path.Combine(work, "main", "logs"));
        File.WriteAllText(Path.Combine(work, "main", "README.txt"), "read me");
        File.WriteAllText(Path.Combine(work, "main", "logs", "today.log"), "ok");
        File.WriteAllText(Path.Combine(work, "appsettings.json"), """{ "Orders": { "Region": "West", "Batch": 10 } }""");
        File.WriteAllText(Path.Combine(work, "App.exe.config"), """<?xml version="1.0"?><configuration><appSettings><add key="Title" value="Old" /></appSettings></configuration>""");
        using (var zip = ZipFile.Open(Path.Combine(work, "package.zip"), ZipArchiveMode.Create))
        using (var entry = new StreamWriter(zip.CreateEntry("deploy.json").Open()))
            entry.Write("{}");
        var w = Json(work);

        var (result, view) = Run(OneCase($$"""
            { "copy": "{{w}}\\main", "to": "{{w}}\\spare" },
            { "delete": "{{w}}\\spare\\README.txt" },
            { "delete": "{{w}}\\nothing-here" },
            { "writeFile": "{{w}}\\spare\\notes.txt", "text": ["Left here by hand", "by support"] },
            { "unzip": "{{w}}\\package.zip", "to": "{{w}}\\editor" },
            { "setJsonValue": "{{w}}\\appsettings.json", "path": "Orders:Region", "to": "East" },
            { "setJsonValue": "{{w}}\\appsettings.json", "path": "Orders:Batch", "to": "25" },
            { "setJsonValue": "{{w}}\\appsettings.json", "path": "Payments:Key", "to": "pk" },
            { "setAppSetting": "{{w}}\\App.exe.config", "key": "Title", "to": "New" },
            { "setAppSetting": "{{w}}\\App.exe.config", "key": "KeepReports", "to": "45" },
            { "checkpoint": "Set up", "checks": [
              { "fileExists": "{{w}}\\spare\\logs\\today.log" },
              { "fileMissing": "{{w}}\\spare\\README.txt" },
              { "fileExists": "{{w}}\\main\\README.txt" },
              { "fileMatches": "{{w}}\\spare\\notes.txt", "regex": "^by support$" },
              { "fileExists": "{{w}}\\editor\\deploy.json" },
              { "jsonValue": "{{w}}\\appsettings.json", "path": "Orders:Region", "equals": "East" },
              { "jsonValue": "{{w}}\\appsettings.json", "path": "Payments:Key", "equals": "pk" },
              { "appSetting": "{{w}}\\App.exe.config", "key": "Title", "equals": "New" },
              { "appSetting": "{{w}}\\App.exe.config", "key": "KeepReports", "equals": "45" } ] }
            """.Replace("\\\\", "/", StringComparison.Ordinal)));

        Assert.All(view.Checks, check => Assert.True(check.Passed, $"{check.Label}: {check.Detail}"));
        Assert.Equal(CaseStatus.Passed, result.Status);
        Assert.Contains("25", File.ReadAllText(Path.Combine(work, "appsettings.json")).Replace(" ", "", StringComparison.Ordinal).Split("\"Batch\":")[1][..2]);
        Assert.Contains("Copy main to", view.Shown.First(shown => shown.StartsWith("Copy", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_set_up_step_that_fails_fails_the_case_and_carries_on()
    {
        var (result, view) = Run(OneCase("""
            { "copy": "no-such-folder/x.txt", "to": "somewhere/y.txt" },
            { "say": "Still here" }
            """));

        Assert.Equal(CaseStatus.Failed, result.Status);
        Assert.Contains("Still here", view.Shown);
        Assert.Single(view.StepFailures);
    }

    [Fact]
    public void Services_start_and_stop()
    {
        _machine.Services["Contoso.Orders-main"] = "Running";

        var (result, view) = Run(OneCase("""
            { "stopService": "Contoso.Orders-main" },
            { "checkpoint": "Stopped", "checks": [ { "service": "Contoso.Orders-main", "state": "Stopped" } ] }
            """));

        Assert.Equal(CaseStatus.Passed, result.Status);
        Assert.Contains("Stop the service Contoso.Orders-main", view.Shown);
    }

    [Fact]
    public void Checks_look_at_what_a_control_is_like_now()
    {
        var app = new FakeDriver();
        app.Add("Button", "Add order").IsEnabled = false;
        app.Add("CheckBox", "Gift wrap").IsChecked = true;
        app.Add("CheckBox", "Express delivery").IsChecked = false;
        app.Add("TabItem", "Orders").IsSelected = true;
        app.Add("Edit", "Customer").HasFocus = true;
        app.Add("Edit", "Quantity").Value = "2";
        app.Add("List", "Orders list", id: "Orders").Rows = 3;

        var (result, view) = Run(OneCase("""
            { "checkpoint": "Looked", "checks": [
              { "disabled": { "button": "Add order" } },
              { "enabled": { "button": "Add order" } },
              { "ticked": { "checkBox": "Gift wrap" } },
              { "notTicked": { "checkBox": "Express delivery" } },
              { "selected": { "tabItem": "Orders" } },
              { "focused": { "edit": "Customer" } },
              { "says": { "edit": "Quantity" }, "equals": "2" },
              { "says": { "edit": "Quantity" }, "startsWith": "3" },
              { "rows": "orders", "equals": "3" },
              { "ticked": { "checkBox": "Nothing like it" } } ] }
            """), app);

        Assert.Equal([true, false, true, true, true, true, true, false, true, false], view.Checks.Select(check => check.Passed));
        Assert.Equal("The Add order button can be used: it is greyed out", result.Failures[0]);
        Assert.Equal("The Quantity box starts with \"3\": it says: 2", result.Failures[1]);
        Assert.EndsWith("it isn't there", result.Failures[2]);
    }

    [Fact]
    public void Checks_look_at_the_registry_programs_event_logs_web_pages_and_the_clipboard()
    {
        _machine.Registry[@"HKLM\SOFTWARE\Contoso|Region"] = "West";
        _machine.Processes.Add("Contoso.Orders");
        _machine.Events.Add("Contoso.Orders started");
        _machine.Pages["http://localhost:5000/health"] = (200, "Healthy");
        _machine.ClipboardText = "ORD-1001";

        var (_, view) = Run(OneCase("""
            { "checkpoint": "Looked", "checks": [
              { "registry": "HKLM\\SOFTWARE\\Contoso", "value": "Region", "equals": "West" },
              { "registry": "HKLM\\SOFTWARE\\Contoso", "value": "Missing", "equals": "<absent>" },
              { "process": "Contoso.Orders.exe" },
              { "process": "Fabrikam.Reports", "running": false },
              { "eventLog": "Application", "source": "Contoso.Orders", "contains": "started" },
              { "web": "http://localhost:5000/health", "contains": "Healthy" },
              { "web": "http://localhost:5000/missing" },
              { "clipboard": "ORD-1001" } ] }
            """));

        Assert.Equal([true, true, true, true, true, true, false, true], view.Checks.Select(check => check.Passed));
        Assert.Equal("it answered 404", view.Checks[6].Detail);
    }

    [Fact]
    public void A_step_keeps_how_long_it_took_and_numbers_can_be_compared()
    {
        var app = new FakeDriver();
        app.Add("Button", "Deploy");

        var (_, view) = Run(OneCase("""
            { "click": { "button": "Deploy" }, "as": "deploy" },
            { "set": { "count": "12" } },
            { "checkpoint": "Fast enough", "checks": [
              { "atMost": "${deploy.seconds}", "to": "60" },
              { "atLeast": "${count}", "to": "20" },
              { "atMost": "lots", "to": "1" } ] }
            """), app);

        Assert.Equal([true, false, false], view.Checks.Select(check => check.Passed));
        Assert.Equal("it is 12", view.Checks[1].Detail);
        Assert.Equal("lots isn't a number", view.Checks[2].Detail);
    }

    [Fact]
    public void Repeat_runs_its_steps_for_each_row_of_a_data_file()
    {
        Directory.CreateDirectory(Path.Combine(Suite, "data"));
        File.WriteAllText(Path.Combine(Suite, "data", "orders.csv"), "Customer,Product,Quantity\r\nAda Lovelace,Coffee beans,2\r\n\"Hopper, Grace\",\"Tea, \"\"green\"\"\",1\r\n");
        var app = new FakeDriver();
        app.Add("Edit", "Customer");

        var (result, view) = Run(OneCase("""
            { "repeat": { "rows": "data/orders.csv" }, "item": "order",
              "steps": [ { "type": { "edit": "Customer" }, "text": "${order.Customer}", "say": "${order}: ${order.Quantity} of ${order.Product}" } ] }
            """), app);

        Assert.Equal(CaseStatus.Passed, result.Status);
        Assert.Equal(["type Ada Lovelace", "type Hopper, Grace"], app.Done.Where(done => done.StartsWith("type", StringComparison.Ordinal)));
        Assert.Contains("Hopper, Grace: 1 of Tea, \"green\"", view.Shown);
    }

    [Fact]
    public void Json_data_files_give_each_object_as_a_row()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "data"));
        var file = Path.Combine(_folder, "data", "rows.json");
        File.WriteAllText(file, """[ { "name": "main", "port": 5000 }, { "name": "blue", "port": 5001 } ]""");

        var rows = DataRows.Read(file);

        Assert.Equal(["main", "blue"], rows.Select(row => row.Text));
        Assert.Equal("5001", rows[1].Part("port"));
    }

    [Fact]
    public void Mouse_menu_tree_slider_and_window_steps_drive_the_app()
    {
        var app = new FakeDriver();
        app.Add("DataItem", "ORD-1001");
        app.Add("Button", "Bin", id: "Bin");
        app.Add("List", "Orders list", id: "Orders");
        app.Add("TreeItem", "Contoso");
        app.Add("TreeItem", "Orders");
        app.Add("Slider", "Zoom");
        app.Add("Button", "Export").IsEnabled = true;

        var (result, _) = Run(OneCase("""
            { "click": { "dataItem": "ORD-1001" }, "how": "double" },
            { "click": { "dataItem": "ORD-1001" }, "how": "right" },
            { "drag": { "dataItem": "ORD-1001" }, "to": "bin" },
            { "menu": ["File", "Export orders…"] },
            { "menu": ["Copy"], "on": "orders" },
            { "expand": ["Contoso", "Orders"] },
            { "setValue": { "type": "Slider", "name": "Zoom" }, "to": "150" },
            { "waitEnabled": { "button": "Export" } },
            { "useWindow": "Order Desk" }
            """), app);

        Assert.Equal(CaseStatus.Passed, result.Status);
        Assert.Equal(
            ["double-click ORD-1001", "right-click ORD-1001", "drag ORD-1001 onto Bin", "menu File > Export orders…", "right-click Orders list", "menu Copy",
             "expand Contoso", "click Orders", "set Zoom 150", "use Order Desk"],
            app.Done.Where(done => !done.StartsWith("point", StringComparison.Ordinal)));
    }

    [Fact]
    public void WaitEnabled_is_stuck_while_the_control_stays_greyed_out()
    {
        var app = new FakeDriver();
        app.Add("Button", "Export").IsEnabled = false;

        var (result, _) = Run(OneCase("""{ "waitEnabled": { "button": "Export" } }"""), app);

        Assert.Equal(CaseStatus.Failed, result.Status);
        Assert.Contains("still greyed out", result.Failures.Single());
    }

    [Fact]
    public void Cases_can_be_picked_by_their_tags()
    {
        var suite = OneCase("""{ "say": "one" }""",
            ("02-two.json", """{ "name": "Two", "tags": ["smoke", "nightly"], "steps": [] }"""),
            ("03-three.json", """{ "name": "Three", "tags": ["Nightly"], "steps": [] }"""));

        Assert.Equal(["smoke", "nightly"], suite.Tags);
        Assert.Equal(["Two"], AutoTestRunner.Scripting.Suite.Tagged(suite.Cases, ["SMOKE"]).Select(item => item.Name));
        Assert.Equal(["Two", "Three"], AutoTestRunner.Scripting.Suite.Tagged(suite.Cases, ["nightly"]).Select(item => item.Name));
        Assert.Equal(3, AutoTestRunner.Scripting.Suite.Tagged(suite.Cases, []).Count);
    }

    [Fact]
    public void The_loader_checks_the_new_steps_controls_and_data_files()
    {
        var problems = Assert.Throws<SuiteException>(() => OneCase("""
            { "click": "orders", "how": "triple" },
            { "drag": "orders", "to": "noSuchBin" },
            { "menu": ["Copy"], "on": "noSuchList" },
            { "repeat": { "rows": "data/missing.csv" }, "steps": [] },
            { "checkpoint": "Done", "checks": [ { "enabled": "noSuchButton" } ] }
            """)).Problems;

        Assert.Equal(5, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("how is mouse, right or double, not triple"));
        Assert.Contains(problems, problem => problem.Contains("noSuchBin"));
        Assert.Contains(problems, problem => problem.Contains("noSuchList"));
        Assert.Contains(problems, problem => problem.Contains("no data file data/missing.csv"));
        Assert.Contains(problems, problem => problem.Contains("noSuchButton"));
    }
}
