using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoTestRunner.Building;
using AutoTestRunner.Cli.Views;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// autotest preview-ui: the launcher on three made-up suites, then a made-up run in the panel, with no app to drive: steps
/// of each kind, a command printing as it goes, one that fails, checkpoints, a stuck question, a step left to the person
/// and the end; then the builder, with a case part built, a pick tool waiting for a control, and rolled back to a step.
/// With --shots it goes quickly and saves a picture of each, which is how CI shows what the windows look like.
/// </summary>
internal static class PreviewUi
{
    public static int Run(string? folder)
    {
        var shots = folder is null ? null : Path.GetFullPath(folder);
        if (shots is not null)
            Directory.CreateDirectory(shots);
        var suites = MadeUpSuites();
        var suite = suites[0].Suite!;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Crashes.Watch(app);
        app.Startup += (_, _) =>
        {
            var launcher = new LauncherWindow(suites, suites[0], new Options());
            launcher.Loaded += (_, _) =>
            {
                if (shots is null)
                    return;
                Save(launcher, Path.Combine(shots, "01-launcher.png"));
                launcher.Close();
            };
            launcher.Closed += (_, _) => Play(app, suite, shots);
            launcher.Show();
        };
        app.Run();
        return (int)ExitCode.Passed;
    }

    private static void Play(Application app, Suite suite, string? shots)
    {
        var control = new RunControl(unattended: false);
        var results = Path.Combine(Path.GetTempPath(), "AutoTestRunner", "preview", "results");
        var panel = new RunPanel(suite, suite.Cases, control, results, canRunAgain: true);
        panel.Closed += (_, _) => Build(app, suite, shots);
        panel.Loaded += (_, _) =>
        {
            var worker = new Thread(() => PlayCases(panel, control, suite, results, shots)) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        };
        panel.Show();
    }

    /// <summary>Plays the made-up run in the panel, from a worker thread as a real run would, taking each picture.</summary>
    private static void PlayCases(RunPanel panel, RunControl control, Suite suite, string results, string? shots)
    {
        var view = (IRunView)panel;
        var fast = shots is not null;
        void Pause(int milliseconds) => Thread.Sleep(fast ? Math.Min(milliseconds, 250) : milliseconds);
        void Shot(string name)
        {
            if (shots is null)
                return;
            Thread.Sleep(900);
            panel.Dispatcher.Invoke(() => Save(panel, Path.Combine(shots, name + ".png")), DispatcherPriority.ApplicationIdle);
        }
        CaseResult Ended(Case item, CaseStatus status, double seconds) => new(item.Number, item.Name) { Status = status, Duration = TimeSpan.FromSeconds(seconds) };

        var cases = suite.Cases;
        view.CaseStarted(1, cases.Count, cases[0]);
        view.ShowStep(Activity.Of(ActivityKind.App, Part.Verb("Start"), new Part(PartKind.Window, "NetDeploy", @"C:\Tests\netdeploy\NetDeploy.exe")));
        Pause(900);
        view.ShowStep(Activity.Of(ActivityKind.Click, Part.Verb("Click"), Badge("Open package", "Button \"Open package\"\nAutomationId  OpenButton\nelements.json  openPackage\nFound by  id OpenButton")));
        Pause(900);
        view.ShowStep(Activity.Of(ActivityKind.File, Part.Verb("Pick"), Part.File(@"C:\Tests\packages\Contoso.Orders-1.0.0.zip"), Part.Word("in"), Part.Window("Open package")));
        var opened = Activity.Of(ActivityKind.Wait, Part.Verb("Wait"), Status, Part.Word("starts with"), Part.Value("Opened Contoso.Orders 1.0.0"));
        view.ShowWait(opened);
        Pause(1600);
        Shot("02-waiting");
        view.Waited(opened with { Detail = @"Opened Contoso.Orders 1.0.0 from C:\Tests\packages" }, TimeSpan.FromSeconds(1.6));
        view.ShowStep(Activity.Of(ActivityKind.Click, Part.Verb("Open"), Badge("Steps", "TabItem \"Steps\"\nFound by  TabItem \"Steps\"")));
        view.ShowStep(Activity.Of(ActivityKind.Check, Part.Verb("Check"), Part.Word("The package is open")));
        view.ShowCheckpoint("The package is open",
        [
            Checked(SubjectKind.Control, "header", null, "=", "Contoso.Orders 1.0.0", "Contoso.Orders 1.0.0", true),
            Checked(SubjectKind.Folder, "Contoso.Orders", null, "is", "missing", "missing", true),
            Checked(SubjectKind.Control, "Steps list", "rows", "=", "11", "11", true),
        ], ["Lock down production shows what it does in either environment, indented under it."], []);
        view.ShowCarryingOn(TimeSpan.FromSeconds(4));
        Pause(2000);
        view.CaseFinished(Ended(cases[0], CaseStatus.Passed, 14.2));

        view.CaseStarted(2, cases.Count, cases[1]);
        var line = "NETDEPLOY_PaymentsApiKey=••• NetDeploy.exe deploy C:\\AutoTestRunner\\packages\\Contoso.Orders-1.1.0.zip --install qa --yes";
        view.ShowStep(Activity.Of(ActivityKind.Command, Part.Verb("Run"), Part.File("NetDeploy.exe")) with { Command = line });
        Pause(700);
        view.CommandStarted(line, null);
        string[] printed =
        [
            "Reading Contoso.Orders-1.1.0.zip", "Package Contoso.Orders 1.1.0, deploying to C:\\Apps\\Contoso.Orders\\qa", "Stopping service Contoso.Orders-qa",
                "Waiting for Contoso.Orders.exe to exit", "Backing up 14 files to C:\\Apps\\Contoso.Orders\\qa\\.backup\\2026-10-25 1412",
                "Copying 16 files", "Writing appsettings.json: Payments:ApiKey from NETDEPLOY_PaymentsApiKey", "Starting service Contoso.Orders-qa",
            ];
        foreach (var text in printed)
        {
            view.CommandOutput(text, false);
            Pause(450);
        }
        Shot("03-command-running");
        view.CommandOutput("Deploy finished. Log: C:\\ProgramData\\NetDeploy\\logs\\deploy-qa.log", false);
        view.CommandFinished(0, TimeSpan.FromSeconds(6.4));
        var heartbeat = "$folder = $args[0]\nGet-Content \"$folder\\heartbeat.txt\" -Tail 1";
        view.ShowStep(Activity.Of(ActivityKind.Command, Part.Verb("Run"), Part.Word("PowerShell")) with { Command = "powershell C:\\Apps\\Contoso.Orders\\qa\n" + heartbeat });
        view.CommandStarted("powershell C:\\Apps\\Contoso.Orders\\qa", heartbeat);
        Pause(600);
        view.CommandOutput("Get-Content : Cannot find path 'C:\\Apps\\Contoso.Orders\\qa\\heartbeat.txt' because it does not exist.", true);
        view.CommandFinished(1, TimeSpan.FromSeconds(0.8));
        view.StepFailed(new StepFailure("A PowerShell step failed with exit code 1: Get-Content : Cannot find path 'C:\\Apps\\Contoso.Orders\\qa\\heartbeat.txt' because it does not exist.",
            "exit 0", "exit 1"));
        view.ShowStep(Activity.Of(ActivityKind.Check, Part.Verb("Check"), Part.Word("qa was deployed from the command line")));
        view.ShowCheckpoint("qa was deployed from the command line",
        [
            Checked(SubjectKind.Value, "deploy.code", null, "=", "0", "0", true),
            Checked(SubjectKind.Value, "deploy.output", null, "contains", "Deploy finished.", @"Deploy finished. Log: C:\ProgramData\NetDeploy\logs\deploy-qa.log", true),
            Checked(SubjectKind.Value, "deploy.output", null, "lacks", "pk-test-123", "not in it", true),
            Checked(SubjectKind.Service, "Contoso.Orders-qa", null, "is", "Running", "Stopped", false),
            Checked(SubjectKind.Setting, "appsettings.json", "Payments:ApiKey", "=", "pk-test-123", "pk-test-123", true),
        ],
        ["The newest deploy under HISTORY is by you, a minute ago."],
        [new("Open qa", () => { }, "openFolder"), new("Open the log", () => { }, "openFile"), new("Services", () => { }, "console")]);
        control.Hold(HoldReason.Checkpoint);
        Shot("04-checkpoint");
        control.CarryOn();
        view.CaseFinished(Ended(cases[1], CaseStatus.Failed, 41.7));

        view.CaseStarted(3, cases.Count, cases[2]);
        // What the app wrote to its database and sent on its queues: a query's rows, a message sent and one received.
        var orders = new Part(PartKind.Database, "orders", "SQL Server\nServer=.\\SQLEXPRESS;Database=Orders;User Id=qa;Password=•••");
        var invoices = new Part(PartKind.Queue, "invoices", "RabbitMQ\namqp://qa:•••@localhost:5672/\ninvoices.out");
        const string Sql = "select Status, Total from Orders where Id = @id";
        view.ShowStep(Activity.Of(ActivityKind.Database, Part.Verb("Query"), orders, Part.Value(Sql)) with { Command = Sql });
        view.CommandStarted("orders (SQL Server)", Sql + "\n-- @id = 1001");
        view.CommandOutput("Status\tTotal", false);
        view.CommandOutput("Shipped\t26.00", false);
        view.CommandFinished(0, TimeSpan.FromSeconds(0.2));
        view.ShowStep(Activity.Of(ActivityKind.Queue, Part.Verb("Send"), Part.Value("{\"order\":1001,\"action\":\"invoice\"}"), Part.Word("to"),
            new Part(PartKind.Queue, "billing", "RabbitMQ\namqp://qa:•••@localhost:5672/\nbilling.in")));
        view.ShowStep(Activity.Of(ActivityKind.Queue, Part.Verb("Receive"), Part.Word("from"), invoices) with { Command = "invoices (RabbitMQ invoices.out)" });
        view.CommandStarted("invoices (RabbitMQ invoices.out)", null);
        view.CommandOutput("{\"invoice\":\"INV-1001\",\"order\":1001,\"total\":26.00}", false);
        view.CommandFinished(0, TimeSpan.FromSeconds(1.3));
        view.ShowStep(Activity.Of(ActivityKind.Check, Part.Verb("Check"), Part.Word("Order 1001 is billed")));
        view.ShowCheckpoint("Order 1001 is billed",
        [
            Checked(SubjectKind.Database, "orders", "select Status from Orders where Id = 1001", "=", "Billed", "Billed", true),
            Checked(SubjectKind.Database, "orders", "rows", "=", "1", "1", true),
            Checked(SubjectKind.Queue, "invoices", "messages", "=", "0", "0", true),
            Checked(SubjectKind.Queue, "billing", "a message", "contains", "1001", "2 messages, none with it", false),
        ],
        [], []);
        control.Hold(HoldReason.Checkpoint);
        Shot("13-databases-and-queues");
        control.CarryOn();
        view.ShowStep(Activity.Of(ActivityKind.Point, Part.Verb("Point at"), Badge("main", "Button \"main\"\nelements.json  installRow main\nFound by  Button tooltip \"C:\\Apps\\Contoso.Orders\\main\"")));
        view.ShowStep(Activity.Of(ActivityKind.Click, Part.Verb("Click"), Badge("Remove", "Button \"Remove\"\nFound by  Button \"Remove\" beside Button \"main\"")));
        view.ShowStep(Activity.Of(ActivityKind.Dialog, Part.Verb("Answer"), Part.Window("Remove main?"), Part.Word("with"), Part.Key("Yes")) with { Detail = "Remove main? Its service, scheduled task and folder are removed." });
        view.ShowStep(Activity.Of(ActivityKind.Type, Part.Verb("Type"), Part.Value("main"), Part.Word("in"), Badge("confirmName", "Edit\nAutomationId  ConfirmNameBox\nelements.json  confirmName\nFound by  id ConfirmNameBox")));
        control.NextStep();
        control.Hold(HoldReason.Step);
        view.ShowStep(Activity.Of(ActivityKind.Click, Part.Verb("Click"), Badge("Stop removing", "Button \"Stop removing\"\nFound by  Button \"Stop removing\" in Window \"Remove install\"")));
        Shot("05-step-by-step");
        control.PauseOrCarryOn();
        view.ShowWait(Activity.Of(ActivityKind.Wait, Part.Verb("Look for"), Badge("Stop removing", "")));
        Pause(1500);
        var screenshot = Path.Combine(Path.GetTempPath(), "AutoTestRunner", "preview", "stuck.png");
        panel.Dispatcher.Invoke(() => Save(panel, screenshot));
        control.Hold(HoldReason.Stuck);
        var asked = new Thread(() => view.AskStuck("Couldn't click the Stop removing button: it isn't there.", screenshot)) { IsBackground = true };
        asked.Start();
        Shot("06-stuck");
        panel.Dispatcher.Invoke(() => panel.Answer(1));
        asked.Join();
        control.CarryOn();
        view.CaseFinished(Ended(cases[2], CaseStatus.Skipped, 25.0));

        view.CaseStarted(4, cases.Count, cases[3]);
        control.SetSpeed(Speed.Manual);
        var type = Activity.Of(ActivityKind.Type, Part.Verb("Type"), Part.Value("main"), Part.Word("in"), Badge("confirmName", "Edit\nAutomationId  ConfirmNameBox"));
        view.ShowStep(type);
        view.YourTurn(type with { Detail = "Type this in the outlined box. It carries on once the box holds just this, however it got there." });
        Pause(1600);
        Shot("07-manual-your-turn");
        view.YourTurn(null);
        control.SetSpeed(Speed.Modest);
        view.ShowStep(Activity.Of(ActivityKind.Click, Part.Verb("Click"), Badge("Remove", "Button \"Remove\"")));
        control.PauseOrCarryOn();
        Shot("07-paused");
        control.PauseOrCarryOn();
        view.CaseFinished(Ended(cases[3], CaseStatus.Passed, 18.9));
        view.Finished("", results, allPassed: false);
        panel.RunEnded();
        Shot("08-finished");
        if (shots is not null)
            panel.Dispatcher.Invoke(panel.Close);
    }

    // NetDeploy's status bar, as a step that waits on it shows it.
    private static readonly Part Status = Badge("status bar", "Text\nAutomationId  StatusText\nelements.json  statusText\nFound by  id StatusText");

    private static Part Badge(string label, string tip) => new(PartKind.Control, label, tip.Length == 0 ? null : tip);

    private static CheckResult Checked(SubjectKind kind, string name, string? detail, string how, string expected, string actual, bool passed)
    {
        var subject = new Subject(kind, name, detail);
        return new CheckResult(Checks.Label(subject, how, expected), subject, how, expected, actual, passed);
    }

    private static void Build(Application app, Suite suite, string? shots)
    {
        var request = new BuildRequest(suite, "Deploy a package to uat", After: suite.Cases[0]);
        var draft = new CaseDraft { Name = request.Name, Needs = { suite.Cases[0].Id } };
        var panel = new BuilderPanel(request, draft);
        panel.Closed += (_, _) => app.Shutdown();
        panel.Loaded += (_, _) => new Thread(() => PlayBuilder(panel, draft, shots)) { IsBackground = true }.Start();
        panel.Show();
    }

    /// <summary>Fills the builder as recording would: steps of each kind, finding their controls well and badly, then a pick tool.</summary>
    private static void PlayBuilder(BuilderPanel panel, CaseDraft draft, string? shots)
    {
        void Shot(string name)
        {
            if (shots is null)
                return;
            Thread.Sleep(900);
            panel.Dispatcher.Invoke(() => Save(panel, Path.Combine(shots, name + ".png")), DispatcherPriority.ApplicationIdle);
        }
        TargetChoice Found(JsonNode json, string shown, TargetQuality quality, string why) => new(json, shown, quality, why);
        var open = new ControlFacts("Button", "Open package", Id: "OpenButton");
        var folder = new ControlFacts("Edit", "Install folder", Id: "FolderBox");
        var environment = new ControlFacts("ComboBox", "Environment", Id: "EnvironmentBox");
        var start = new ControlFacts("CheckBox", "Start the service");
        var deploy = new ControlFacts("Button", "Deploy");
        var status = Found(JsonValue.Create("status"), "status", TargetQuality.Sturdy, "The app's status text, status in elements.json.");
        DraftStep[] steps =
        [
            StepMaker.Click(Found(new JsonObject { ["id"] = "OpenButton" }, "id OpenButton", TargetQuality.Sturdy, "Found by its AutomationId."), open),
            StepMaker.PickFile(new DialogFacts("Open package", IsPicker: true, Path: @"C:\AutoTestRunner\packages\Contoso.Orders-1.1.0.zip")),
            new(new JsonObject { ["waitText"] = "status", ["startsWith"] = "Opened Contoso.Orders 1.1.0", ["new"] = true }, ActivityKind.Say, status),
            StepMaker.Type(Found(new JsonObject { ["id"] = "FolderBox" }, "id FolderBox", TargetQuality.Sturdy, "Found by its AutomationId."), folder, @"C:\Apps\Contoso.Orders\uat"),
            StepMaker.Choose(Found(new JsonObject { ["id"] = "EnvironmentBox" }, "id EnvironmentBox", TargetQuality.Sturdy, "Found by its AutomationId."), environment, "uat"),
            StepMaker.Tick(Found(new JsonObject { ["type"] = "CheckBox", ["name"] = "Start the service" }, "CheckBox \"Start the service\"", TargetQuality.Wording, "Found by what it says, which changes if the wording does."), start, true),
            StepMaker.Click(Found(new JsonObject { ["type"] = "Button", ["name"] = "Deploy" }, "Button \"Deploy\"", TargetQuality.Ambiguous, "2 controls match this: the first one is used. Pick it to name it."), deploy),
        ];
        draft.Steps.AddRange(steps);
        panel.Ready();
        panel.Add(steps);
        Shot("09-builder-recording");

        // The Wait for its text tool picked up, waiting for a control to be clicked, which is outlined as the mouse goes.
        var row = new ControlFacts("DataItem", "uat") { Ancestors = [new("Table", "History", Id: "HistoryList")], Bounds = new ScreenRect(80, 300, 520, 324) };
        panel.Dispatcher.Invoke(() => panel.PickUp(PickTools.WaitText));
        panel.ShowHover(row, Found(new JsonObject { ["type"] = "DataItem", ["name"] = "uat", ["within"] = new JsonObject { ["id"] = "HistoryList" } },
            "DataItem \"uat\" within id HistoryList", TargetQuality.Wording, "Found by what it says, in the list it is in."));
        Shot("10-builder-picking");
        panel.Dispatcher.Invoke(panel.CancelPick);

        // Gone back to step 3, to edit from it: the steps after it wait to be played again.
        foreach (var later in steps.Skip(3))
            later.State = DraftStepState.NotPlayed;
        panel.Refresh();
        panel.Ready();
        Shot("11-builder-rolled-back");

        // A step's details folded out, and two steps picked to make a macro of or repeat.
        panel.Dispatcher.Invoke(() => panel.Show(steps[3], [steps[4], steps[5]]));
        Shot("12-builder-details-and-picked");
        if (shots is null)
            return;
        // The made-up steps aren't kept, so closing asks nothing.
        draft.Steps.Clear();
        panel.Dispatcher.Invoke(panel.Close);
    }

    // Three suites, as a kit puts them beside the runner: two that load, one with mistakes in its scripts.
    private static List<FoundSuite> MadeUpSuites()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoTestRunner", "preview");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        var desk = Path.Combine(root, "order-desk");
        Directory.CreateDirectory(Path.Combine(desk, "cases"));
        File.WriteAllText(Path.Combine(desk, "suite.json"), """{ "name": "Order Desk tour (preview)", "app": { "exe": "OrderDesk.exe", "window": "Order Desk" } }""");
        File.WriteAllText(Path.Combine(desk, "cases", "01-take-an-order.json"), """{ "name": "Take an order", "covers": "Adding an order", "steps": [ { "say": "Hello" } ] }""");
        var broken = Path.Combine(root, "inventory");
        Directory.CreateDirectory(Path.Combine(broken, "cases"));
        File.WriteAllText(Path.Combine(broken, "suite.json"), """{ "name": "Inventory", "app": { "exe": "Inventory.exe" } }""");
        File.WriteAllText(Path.Combine(broken, "cases", "01-count-stock.json"), """{ "name": "Count stock", "steps": [ { "clik": "countButton" } ] }""");
        return [FoundSuite.Load(MadeUpSuite(Path.Combine(root, "netdeploy"))), FoundSuite.Load(desk), FoundSuite.Load(broken)];
    }

    // A suite of four cases, written to a folder, with a results folder from a run before so the launcher shows them.
    private static string MadeUpSuite(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "cases"));
        File.WriteAllText(Path.Combine(folder, "suite.json"), """{ "name": "NetDeploy manual tests (preview)", "app": { "exe": "NetDeploy.exe", "window": "NetDeploy" } }""");
        string[] names = ["Open a package", "Deploy from the command line", "Stop removing", "Remove a hand copy"];
        string Id(int index) => names[index].ToLowerInvariant().Replace(' ', '-');
        for (var index = 0; index < names.Length; index++)
            File.WriteAllText(Path.Combine(folder, "cases", $"{index + 1:00}-{Id(index)}.json"),
                $$"""{ "name": "{{names[index]}}", "covers": "What case {{index + 1}} tries", "tags": ["{{(index < 2 ? "smoke" : "remove")}}"]{{(index > 0 ? $", \"needs\": [\"{Id(index - 1)}\"]" : "")}}{{(index == 2 ? ", \"review\": \"Case 2, Deploy from the command line, was changed from step 4: check this case still starts where it should.\"" : "")}}, "steps": [ { "say": "Hello" } ] }""");
        var last = Path.Combine(folder, "results", "2026-10-25 120000");
        Directory.CreateDirectory(last);
        File.WriteAllText(Path.Combine(last, "junit.xml"), """
            <testsuites><testsuite name="preview">
              <testcase name="1. Open a package" /><testcase name="2. Deploy from the command line"><failure message="x" /></testcase>
              <testcase name="3. Stop removing"><skipped /></testcase><testcase name="4. Remove a hand copy" />
            </testsuite></testsuites>
            """);
        return folder;
    }

    // A picture of a window's inside, on white, at the screen's scale.
    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var scale = VisualTreeHelper.GetDpi(window);
        var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content)
            {
                // The content's own box, where the window puts it, not everything drawn in it: a control row pulled left by
                // a margin would move it.
                Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight),
            },
                null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)(width * scale.DpiScaleX), (int)(height * scale.DpiScaleY), scale.PixelsPerInchX, scale.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
