using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// autotest preview-ui: the launcher, then a made-up run in the panel, with no app to drive: steps of each kind, a command
/// printing as it goes, one that fails, checkpoints, a stuck question and the end. With --shots it goes quickly and
/// saves a picture of each, which is how CI shows what the windows look like.
/// </summary>
internal static class PreviewUi
{
    public static int Run(string[] args)
    {
        var shots = args is ["--shots", var folder] ? Path.GetFullPath(folder) : null;
        if (args.Length > 0 && shots is null)
        {
            Console.Error.WriteLine("autotest preview-ui [--shots <folder>]");
            return (int)ExitCode.ScriptError;
        }
        if (shots is not null)
            Directory.CreateDirectory(shots);
        var suite = MadeUpSuite();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) =>
        {
            var launcher = new LauncherWindow(suite, new Options());
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
        panel.Closed += (_, _) => app.Shutdown();
        panel.Loaded += (_, _) =>
        {
            var worker = new Thread(() =>
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
            view.ShowStep("Start NetDeploy", ActivityKind.App);
            Pause(900);
            view.ShowStep("Click Open package", ActivityKind.Click);
            Pause(900);
            view.ShowStep("LauncherChoice Contoso.Orders-1.0.0.zip", ActivityKind.File);
            view.ShowWait("Wait for the status text to say \"Opened Contoso.Orders 1.0.0\"");
            Pause(1600);
            Shot("02-waiting");
            view.ShowStep("Check: The package is open", ActivityKind.Check);
            view.ShowCheckpoint("The package is open", [new("The window says Contoso.Orders 1.0.0", true, null)], ["The folder list shows C:\\Apps"], []);
            view.ShowCarryingOn(TimeSpan.FromSeconds(4));
            Pause(2000);
            view.CaseFinished(Ended(cases[0], CaseStatus.Passed, 14.2));

            view.CaseStarted(2, cases.Count, cases[1]);
            var line = "NETDEPLOY_PaymentsApiKey=••• NetDeploy.exe deploy C:\\AutoTestRunner\\packages\\Contoso.Orders-1.1.0.zip --install qa --yes";
            view.ShowStep("Deploy 1.1.0 to qa again from the command line, with the payments API key in an environment variable", ActivityKind.Command, line);
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
            view.ShowStep("Run a PowerShell script", ActivityKind.Command, "powershell C:\\Apps\\Contoso.Orders\\qa\n$folder = $args[0]\nGet-Content \"$folder\\heartbeat.txt\" -Tail 1");
            view.CommandStarted("powershell C:\\Apps\\Contoso.Orders\\qa", "$folder = $args[0]\nGet-Content \"$folder\\heartbeat.txt\" -Tail 1");
            Pause(600);
            view.CommandOutput("Get-Content : Cannot find path 'C:\\Apps\\Contoso.Orders\\qa\\heartbeat.txt' because it does not exist.", true);
            view.CommandFinished(1, TimeSpan.FromSeconds(0.8));
            view.ShowStep("Check: qa was deployed from the command line", ActivityKind.Check);
            view.ShowCheckpoint("qa was deployed from the command line",
            [
                new("The deploy ended with exit code 0", true, null),
                new("and said Deploy finished.", true, null),
                new("The API key never showed in what it printed", true, null),
                new("Service Contoso.Orders-qa is Running", false, "it is Stopped"),
            ],
            ["The newest deploy under HISTORY is by you, a minute ago."],
            [new("Open qa", () => { }, "openFolder"), new("Open the log", () => { }, "openFile"), new("Services", () => { }, "console")]);
            control.Hold(HoldReason.Checkpoint);
            Shot("04-checkpoint");
            control.CarryOn();
            view.CaseFinished(Ended(cases[1], CaseStatus.Failed, 41.7));

            view.CaseStarted(3, cases.Count, cases[2]);
            control.NextStep();
            control.Hold(HoldReason.Step);
            view.ShowStep("Click Stop removing, while the removal waits at its second step", ActivityKind.Click);
            Shot("05-step-by-step");
            control.PauseOrCarryOn();
            view.ShowWait("Look for the Stop removing button");
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
            view.ShowStep("Click Remove", ActivityKind.Click);
            control.PauseOrCarryOn();
            Shot("07-paused");
            control.PauseOrCarryOn();
            view.CaseFinished(Ended(cases[3], CaseStatus.Passed, 18.9));
            view.Finished("", results, allPassed: false);
            panel.RunEnded();
            Shot("08-finished");
            if (shots is not null)
                panel.Dispatcher.Invoke(panel.Close);
            }) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        };
        panel.Show();
    }

    // A suite of four cases, written to a temp folder, with a results folder from a run before so the launcher shows them.
    private static Suite MadeUpSuite()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTestRunner", "preview");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(Path.Combine(folder, "cases"));
        File.WriteAllText(Path.Combine(folder, "suite.json"), """{ "name": "NetDeploy manual tests (preview)", "app": { "exe": "NetDeploy.exe", "window": "NetDeploy" } }""");
        string[] names = ["Open a package", "Deploy from the command line", "Stop removing", "Remove a hand copy"];
        string Id(int index) => names[index].ToLowerInvariant().Replace(' ', '-');
        for (var index = 0; index < names.Length; index++)
            File.WriteAllText(Path.Combine(folder, "cases", $"{index + 1:00}-{Id(index)}.json"),
                $$"""{ "name": "{{names[index]}}", "covers": "What case {{index + 1}} tries"{{(index > 0 ? $", \"needs\": [\"{Id(index - 1)}\"]" : "")}}, "steps": [ { "say": "Hello" } ] }""");
        var last = Path.Combine(folder, "results", "2026-10-25 120000");
        Directory.CreateDirectory(last);
        File.WriteAllText(Path.Combine(last, "junit.xml"), """
            <testsuites><testsuite name="preview">
              <testcase name="1. Open a package" /><testcase name="2. Deploy from the command line"><failure message="x" /></testcase>
              <testcase name="3. Stop removing"><skipped /></testcase><testcase name="4. Remove a hand copy" />
            </testsuite></testsuites>
            """);
        return Suite.Load(folder);
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
