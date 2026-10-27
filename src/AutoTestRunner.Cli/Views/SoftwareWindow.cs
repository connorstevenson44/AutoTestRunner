using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// The software a suite needs, each with a tick box: what is missing is ticked, what is there is greyed with its
/// version. Install runs autotest setup on what is ticked, as administrator when one of them needs it, and shows what
/// it prints as it goes. Nothing is installed until Install is clicked.
/// </summary>
internal static class SoftwareWindow
{
    /// <summary>Asks, and installs what was ticked. True when something was installed, so the launcher looks again.</summary>
    public static bool Ask(Window? owner, Suite suite, Options options, IReadOnlyList<SoftwareState> states)
    {
        var window = Make(owner, suite, options, states, out var installed);
        window.ShowDialog();
        return installed();
    }

    /// <summary>The window, not shown yet, as preview-ui takes a picture of it; installed says whether Install ran.</summary>
    internal static Window Make(Window? owner, Suite suite, Options options, IReadOnlyList<SoftwareState> states, out Func<bool> installed)
    {
        var ran = false;
        installed = () => ran;
        var window = Ui.Dialog($"Software for {suite.Name}", owner, 600);
        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        var missing = states.Count(state => state.Wanted);
        stack.Children.Add(Ui.IconText(Icons.Package, missing == 0 ? "Everything is installed" : $"{suite.Name} needs {(missing == 1 ? "1 more" : $"{missing} more")}", missing == 0 ? Ui.Pass : Ui.AmberInk, 15, FontWeights.SemiBold));

        var rows = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var boxes = new List<(SoftwareState State, CheckBox Box)>();
        foreach (var state in states)
        {
            var (box, row) = Row(state);
            boxes.Add((state, box));
            rows.Children.Add(row);
        }
        stack.Children.Add(rows);

        // What autotest setup prints, as the panel shows commands.
        var log = new TextBox
        {
            IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, Background = Ui.Terminal, Foreground = Ui.TerminalText,
            BorderThickness = new Thickness(0), Padding = new Thickness(10, 8, 10, 8), Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0),
        };
        stack.Children.Add(log);

        var buttons = new UniformGrid { Columns = 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var later = Ui.LabelledButton(Icons.Cross, "Not now", window.Close);
        later.IsCancel = true;
        later.Margin = new Thickness(0, 0, 4, 0);
        Button? install = null;
        install = Ui.LabelledButton(Icons.Download, "Install", () =>
        {
            var picked = boxes.Where(pair => pair.Box.IsChecked == true).Select(pair => pair.State).ToList();
            if (picked.Count == 0)
                return;
            foreach (var (_, box) in boxes)
                box.IsEnabled = false;
            install!.IsEnabled = false;
            log.Visibility = Visibility.Visible;
            Install(suite, options, picked, log, ended =>
            {
                ran = true;
                later.Content = Ui.IconText(Icons.Tick, "Close", Ui.Ink);
                log.AppendText(ended);
                log.ScrollToEnd();
            });
        }, "MainButton");
        install.IsDefault = true;
        install.Margin = new Thickness(4, 0, 0, 0);
        void Shield()
        {
            var admin = !Elevation.IsAdministrator && boxes.Any(pair => pair.Box.IsChecked == true && NeedsAdministrator(pair.State.Item));
            install.Content = Ui.IconText(admin ? Icons.Admin : Icons.Download, "Install", Brushes.White);
            install.ToolTip = admin ? "Asks for administrator, which installing a Windows feature needs" : null;
            install.IsEnabled = boxes.Any(pair => pair.Box.IsChecked == true);
        }
        foreach (var (_, box) in boxes)
        {
            box.Checked += (_, _) => Shield();
            box.Unchecked += (_, _) => Shield();
        }
        Shield();
        foreach (var button in new[] { later, install })
        {
            button.MinWidth = 110;
            button.Padding = new Thickness(14, 8, 14, 8);
        }
        buttons.Children.Add(later);
        buttons.Children.Add(install);
        stack.Children.Add(buttons);
        window.Content = stack;
        return window;
    }

    // A row: its tick box, its name with how it is installed, and what is there now, as an icon and a few words.
    private static (CheckBox Box, Grid Row) Row(SoftwareState state)
    {
        var item = state.Item;
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = state.Line };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new CheckBox { IsChecked = state.Wanted, IsEnabled = state.Status != SoftwareStatus.Installed, VerticalAlignment = VerticalAlignment.Center };
        var words = new StackPanel { Opacity = state.Status == SoftwareStatus.Installed ? 0.6 : 1 };
        words.Children.Add(Ui.Words(item.Title, 13.5, Ui.Ink, FontWeights.SemiBold, wrap: false));
        var how = item.Winget is { } id ? $"winget  {id}" : item.WindowsFeature is { } feature ? $"Windows feature  {feature}" : $"script  {item.Script}";
        words.Children.Add(Ui.Words(how + (item.Version is { } version ? $"  ·  {version}" : ""), 11, Ui.Muted, wrap: false));
        words.MouseLeftButtonUp += (_, _) =>
        {
            if (box.IsEnabled)
                box.IsChecked = box.IsChecked != true;
        };
        var (glyph, text, color, background) = state.Status switch
        {
            SoftwareStatus.Installed => (Icons.Passed, state.Found ?? "Installed", Ui.Pass, Ui.PassSoft),
            SoftwareStatus.Missing => (Icons.Download, "Not installed", Ui.AmberInk, Ui.AmberSoft),
            SoftwareStatus.OtherVersion => (Icons.Download, $"{state.Found} → {item.Version}", Ui.AmberInk, Ui.AmberSoft),
            _ => (Icons.Warning, "Can't tell", Ui.Muted, Ui.Surface),
        };
        var status = Ui.Pill(Ui.IconText(glyph, text, color, 11.5), background, 10, new Thickness(8, 2, 10, 2));
        status.VerticalAlignment = VerticalAlignment.Center;
        status.ToolTip = state.Detail ?? state.Line;
        Grid.SetColumn(words, 1);
        Grid.SetColumn(status, 2);
        row.Children.Add(box);
        row.Children.Add(words);
        row.Children.Add(status);
        return (box, row);
    }

    internal static bool NeedsAdministrator(SoftwareSettings item) => item.Administrator || item.WindowsFeature is not null;

    // Runs autotest setup on what was picked, as administrator when one needs it, and shows its log as it is written.
    private static void Install(Suite suite, Options options, IReadOnlyList<SoftwareState> picked, TextBox log, Action<string> ended)
    {
        var path = Path.Combine(Path.GetTempPath(), "AutoTestRunner", $"setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        var admin = !Elevation.IsAdministrator && picked.Any(state => NeedsAdministrator(state.Item));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = admin, CreateNoWindow = !admin, WorkingDirectory = Environment.CurrentDirectory };
        if (admin)
            start.Verb = "runas";
        foreach (var arg in (string[])["setup", suite.Folder, "--install", string.Join(',', picked.Select(state => state.Item.Name)), "--yes", "--log", path])
            start.ArgumentList.Add(arg);
        foreach (var (name, value) in options.Variables)
        {
            start.ArgumentList.Add("--var");
            start.ArgumentList.Add($"{name}={value}");
        }
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Win32Exception)
        {
            ended("Installing needs administrator, and that was declined.\n");
            return;
        }
        // The log is read from where it was read up to, a few times a second, until autotest setup has ended.
        long read = 0;
        void Read()
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            file.Seek(read, SeekOrigin.Begin);
            using var reader = new StreamReader(file);
            var text = reader.ReadToEnd();
            read = file.Position;
            if (text.Length == 0)
                return;
            log.AppendText(text);
            log.ScrollToEnd();
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => Read();
        timer.Start();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => log.Dispatcher.BeginInvoke(() =>
        {
            timer.Stop();
            Read();
            ended(process.ExitCode == 0 ? "\nDone.\n" : $"\nautotest setup ended with exit code {process.ExitCode}.\n");
            process.Dispose();
        });
    }
}
