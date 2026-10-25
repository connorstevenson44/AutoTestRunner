using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Walkthrough.Driving;
using Walkthrough.FlaUI;
using Walkthrough.Running;
using Walkthrough.Scripting;

namespace Walkthrough.Cli;

/// <summary>The cases picked in the launcher, and how to run them.</summary>
internal sealed record Pick(IReadOnlyList<Case> Cases, Options Options);

/// <summary>
/// The window before a run you watch: whether the app and its files are there and the machine is clean, with a button
/// that cleans it; the cases to tick, each with how it went last time; how it runs and how fast; and Start.
/// </summary>
internal sealed class Launcher : Window
{
    private static readonly (string Mode, string Glyph, string Text, string Tip)[] Modes =
    [
        ("itself", Icons.Play, "By itself", "Runs by itself, showing each checkpoint for a few seconds. Space holds it there."),
        ("checks", Icons.CheckList, "Wait at checks", "Runs by itself, and waits for you at each checkpoint."),
        ("steps", Icons.Next, "Step by step", "Waits for you to press Enter before each step."),
    ];

    private readonly Suite _suite;
    private readonly Options _options;
    private readonly List<(Case Case, CheckBox Box, Border Needs)> _rows = [];
    private readonly ToggleButton[] _modes, _speeds;
    private readonly Button _start;
    private readonly Border _files, _machine;
    private readonly Button _clean;
    private readonly Feed _cleaning;
    private readonly Border _cleaningBox;
    private readonly Dictionary<int, CaseStatus> _last;
    private bool _missing, _busy;

    public Launcher(Suite suite, Options options)
    {
        (_suite, _options) = (suite, options);
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = suite.Name;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(780, area.Width - 40);
        Height = Math.Min(760, area.Height - 40);
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;
        FontFamily = Ui.Text;
        Foreground = Ui.Ink;
        _last = LastResults(options.Results ?? Path.Combine(suite.Folder, "results"));

        var root = new Grid { Margin = new Thickness(22, 18, 22, 18) };
        for (var row = 0; row < 6; row++)
            root.RowDefinitions.Add(new RowDefinition { Height = row == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // ===== The suite, and whether it can run =====
        var title = Ui.Words(suite.Name, 20, Ui.Ink, FontWeights.Bold);
        var state = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        _files = Ui.Pill(new TextBlock(), Ui.Surface, 12, new Thickness(10, 4, 12, 4));
        _files.Margin = new Thickness(0, 0, 8, 6);
        _machine = Ui.Pill(new TextBlock(), Ui.Surface, 12, new Thickness(10, 4, 12, 4));
        _machine.Margin = new Thickness(0, 0, 8, 6);
        _clean = Ui.LabelledButton(Icons.Clean, "Clean up", CleanUp);
        _clean.Visibility = Visibility.Collapsed;
        _clean.ToolTip = "Closes the app and runs the suite's reset script, so the machine is as the first case expects";
        state.Children.Add(_files);
        if (suite.Reset.Leftovers is not null)
            state.Children.Add(_machine);
        state.Children.Add(_clean);
        _cleaning = new Feed("Clean up");
        _cleaningBox = new Border { Height = 170, Margin = new Thickness(0, 4, 0, 6), Child = _cleaning, Visibility = Visibility.Collapsed };
        var top = new StackPanel();
        top.Children.Add(title);
        top.Children.Add(state);
        top.Children.Add(_cleaningBox);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // ===== The cases =====
        var pick = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
        pick.Children.Add(Ui.LabelledButton(Icons.Choose, "All", () => Tick(_ => true)));
        pick.Children.Add(Ui.LabelledButton(Icons.Cross, "None", () => Tick(_ => false)));
        if (_last.Values.Any(status => status != CaseStatus.Passed))
            pick.Children.Add(Ui.LabelledButton(Icons.Failed, "Didn't pass last time", () => Tick(item => _last.TryGetValue(item.Number, out var status) && status != CaseStatus.Passed),
                glyphColor: Ui.Fail));
        Grid.SetRow(pick, 1);
        root.Children.Add(pick);

        var list = new StackPanel();
        foreach (var item in suite.Cases)
            list.Children.Add(CaseRow(item));
        var cases = new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 4, 0, 0),
            Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4) },
        };
        Grid.SetRow(cases, 2);
        root.Children.Add(cases);

        // ===== How it runs =====
        var how = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        how.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        how.ColumnDefinitions.Add(new ColumnDefinition());
        how.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        _modes = Modes.Select(mode =>
        {
            var toggle = Tile(mode.Glyph, mode.Text, mode.Tip);
            toggle.IsChecked = options.Mode == mode.Mode;
            modes.Children.Add(toggle);
            return toggle;
        }).ToArray();
        foreach (var toggle in _modes)
            toggle.Click += (_, _) => Only(_modes, toggle);
        var speeds = new StackPanel { Orientation = Orientation.Horizontal };
        _speeds = new[] { (Icons.SpeedSlow, "Slow"), (Icons.SpeedModest, "Modest"), (Icons.SpeedFast, "Fast") }.Select((speed, index) =>
        {
            var toggle = Tile(speed.Item1, speed.Item2, null);
            toggle.IsChecked = (int)options.Speed == index;
            speeds.Children.Add(toggle);
            return toggle;
        }).ToArray();
        foreach (var toggle in _speeds)
            toggle.Click += (_, _) => Only(_speeds, toggle);
        Grid.SetColumn(speeds, 2);
        how.Children.Add(modes);
        how.Children.Add(speeds);
        Grid.SetRow(how, 3);
        root.Children.Add(how);

        // ===== Start =====
        var keys = Ui.Words("Space pauses · Enter does a step · Esc stops", 11.5, Ui.Faint, wrap: false);
        _start = Ui.LabelledButton(Icons.Play, "Start", Start, "MainButton");
        _start.IsDefault = true;
        _start.FontSize = 14;
        var go = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        go.ColumnDefinitions.Add(new ColumnDefinition());
        go.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_start, 1);
        go.Children.Add(keys);
        go.Children.Add(_start);
        Grid.SetRow(go, 5);
        root.Children.Add(go);
        Content = root;

        Tick(item => options.Cases is null || suite.Select(options.Cases).Contains(item));
        CheckFiles();
        Loaded += (_, _) => CheckMachine();
    }

    /// <summary>What to run, once Start was clicked; null when the launcher was closed instead.</summary>
    public Pick? Picked { get; private set; }

    private Grid CaseRow(Case item)
    {
        var row = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 1, 0, 1), ToolTip = item.Covers };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Focusable = false };
        box.Checked += (_, _) => Changed();
        box.Unchecked += (_, _) => Changed();
        var number = Ui.Pill(Ui.Words($"{item.Number}", 11, Ui.Accent, FontWeights.Bold, wrap: false), Ui.AccentSoft, 8, new Thickness(6, 1, 6, 1));
        number.HorizontalAlignment = HorizontalAlignment.Left;
        var name = Ui.Words(item.Name, 13.5, Ui.Ink, wrap: false);
        name.Margin = new Thickness(0, 6, 8, 6);
        var needs = Ui.Pill(new TextBlock(), Ui.Surface, 8, new Thickness(6, 1, 7, 1));
        needs.Margin = new Thickness(0, 0, 6, 0);
        needs.Visibility = item.Needs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var last = _last.TryGetValue(item.Number, out var status) ? Ui.Glyph(Icons.Of(status), 14, Ui.Of(status)) : new TextBlock();
        last.HorizontalAlignment = HorizontalAlignment.Center;
        last.ToolTip = _last.ContainsKey(item.Number) ? $"Last time: {status.ToString().ToLowerInvariant()}" : null;
        Grid.SetColumn(number, 1);
        Grid.SetColumn(name, 2);
        Grid.SetColumn(needs, 3);
        Grid.SetColumn(last, 4);
        row.Children.Add(box);
        row.Children.Add(number);
        row.Children.Add(name);
        row.Children.Add(needs);
        row.Children.Add(last);
        row.MouseEnter += (_, _) => row.Background = Ui.Surface;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is not CheckBox)
                box.IsChecked = box.IsChecked != true;
        };
        _rows.Add((item, box, needs));
        return row;
    }

    // A big toggle: an icon over a word, for how the run goes and its speed.
    private static ToggleButton Tile(string glyph, string text, string? tip)
    {
        var stack = new StackPanel { MinWidth = 64 };
        var icon = Ui.Glyph(glyph, 20);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock { Text = text, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        return new ToggleButton { Content = stack, ToolTip = tip, Style = (Style)Ui.Theme["ToggleIcon"], Padding = new Thickness(10, 8, 10, 6), Margin = new Thickness(0, 0, 4, 0) };
    }

    private static void Only(ToggleButton[] group, ToggleButton picked)
    {
        foreach (var toggle in group)
            toggle.IsChecked = toggle == picked;
    }

    private void Tick(Func<Case, bool> which)
    {
        foreach (var (item, box, _) in _rows)
            box.IsChecked = which(item);
        Changed();
    }

    // Each case's needs show what it runs after, in amber when that case isn't ticked, so the app may not be ready for it.
    private void Changed()
    {
        var ticked = _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Case.Id).ToHashSet();
        foreach (var (item, box, needs) in _rows)
        {
            if (item.Needs.Count == 0)
                continue;
            var numbers = item.Needs.Select(id => _suite.Cases.FirstOrDefault(other => other.Id == id)).OfType<Case>().ToList();
            var ready = box.IsChecked != true || item.Needs.All(ticked.Contains);
            needs.Background = ready ? Ui.Surface : Ui.HoldSoft;
            needs.Child = Ui.IconText(Icons.Back, string.Join(", ", numbers.Select(other => other.Number)), ready ? Ui.Muted : Ui.Hold, 11);
            needs.ToolTip = "Carries on from " + string.Join(" and ", numbers.Select(other => $"{other.Number}. {other.Name}"))
                + (ready ? "" : ", which isn't ticked. The app needs to be as that case leaves it.");
        }
        var count = ticked.Count;
        if (_start is not null)
        {
            ((TextBlock)((StackPanel)_start.Content).Children[1]).Text = count == _rows.Count ? "Start all" : $"Start {count}";
            _start.IsEnabled = count > 0 && !_missing && !_busy;
        }
    }

    private void CheckFiles()
    {
        var globals = Runner.Variables(_suite, Path.GetTempPath(), _options.Variables);
        var missing = Runner.Missing(_suite, globals);
        _missing = missing.Count > 0;
        _files.Background = _missing ? Ui.FailSoft : Ui.PassSoft;
        _files.Child = _missing
            ? Ui.IconText(Icons.Failed, "Can't find " + string.Join(", ", missing.Select(path => Path.GetFileName(path.TrimEnd('\\', '/')))), Ui.Fail)
            : Ui.IconText(Icons.Passed, "The app and its files are here", Ui.Pass);
        _files.ToolTip = _missing ? string.Join("\n", missing) : globals.Expand(_suite.App.Exe);
        Changed();
    }

    // Runs the suite's leftovers script in the background, as it takes a few seconds.
    private void CheckMachine()
    {
        if (_suite.Reset.Leftovers is null)
            return;
        _machine.Background = Ui.Surface;
        var checking = new StackPanel { Orientation = Orientation.Horizontal };
        checking.Children.Add(Ui.Spinner(12, Ui.Muted));
        checking.Children.Add(new TextBlock { Text = "Looking for leftovers", Foreground = Ui.Muted, FontSize = 12, Margin = new Thickness(6, 0, 0, 0) });
        _machine.Child = checking;
        _clean.Visibility = Visibility.Collapsed;
        Task.Run<(IReadOnlyList<string> Leftovers, string? Problem)>(() =>
        {
            try
            {
                return (Runner.Leftovers(_suite, new WindowsMachine()), null);
            }
            catch (Exception ex)
            {
                return ([], ex.Message);
            }
        }).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            var (leftovers, problem) = task.Result;
            var clean = leftovers.Count == 0 && problem is null;
            _machine.Background = clean ? Ui.PassSoft : Ui.HoldSoft;
            _machine.Child = problem is not null ? Ui.IconText(Icons.Warning, "Couldn't look for leftovers", Ui.Hold)
                : clean ? Ui.IconText(Icons.Passed, "Nothing left over", Ui.Pass)
                : Ui.IconText(Icons.Warning, "Left over: " + string.Join(", ", leftovers.Take(3)) + (leftovers.Count > 3 ? $" and {leftovers.Count - 3} more" : ""), Ui.Hold);
            _machine.ToolTip = problem ?? (clean ? null : string.Join("\n", leftovers) + "\n\nStarting from case 1 cleans these up first.");
            _clean.Visibility = clean ? Visibility.Collapsed : Visibility.Visible;
        }));
    }

    // Closes the app and runs the reset script, showing it run as the panel shows commands.
    private void CleanUp()
    {
        _busy = true;
        _clean.IsEnabled = false;
        Changed();
        _cleaningBox.Visibility = Visibility.Visible;
        var host = new CommandsTo(_cleaning, Dispatcher);
        var worker = new Thread(() =>
        {
            try
            {
                var control = new RunControl(unattended: true);
                var results = new Results(Path.Combine(Path.GetTempPath(), "Walkthrough", "cleanup"), console: null);
                using var driver = new FlaUiDriver(control, () => null, _ => { }, _ => { });
                new Runner(_suite, driver, new WindowsMachine(), host, control, results, _options.Variables).ResetMachine();
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() => _cleaning.Note("Clean up failed: " + ex.Message));
            }
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                _clean.IsEnabled = true;
                Changed();
                CheckMachine();
            });
        });
        worker.SetApartmentState(ApartmentState.MTA);
        worker.IsBackground = true;
        worker.Start();
    }

    private void Start()
    {
        var cases = _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Case).ToList();
        if (cases.Count == 0 || _missing || _busy)
            return;
        var mode = Modes[Array.FindIndex(_modes, toggle => toggle.IsChecked == true) is var index and >= 0 ? index : 0].Mode;
        var speed = (Speed)Math.Max(0, Array.FindIndex(_speeds, toggle => toggle.IsChecked == true));
        var numbers = string.Join(",", cases.Select(item => item.Number));
        Picked = new Pick(cases, _options with { Mode = mode, Speed = speed, Cases = numbers });
        Close();
    }

    // How each case went in the newest results folder, from its junit.xml.
    private static Dictionary<int, CaseStatus> LastResults(string parent)
    {
        var found = new Dictionary<int, CaseStatus>();
        try
        {
            var junit = Directory.Exists(parent)
                ? Directory.GetDirectories(parent).Order(StringComparer.Ordinal).Reverse().Select(folder => Path.Combine(folder, "junit.xml")).FirstOrDefault(File.Exists)
                : null;
            if (junit is null)
                return found;
            foreach (var test in XDocument.Load(junit).Descendants("testcase"))
            {
                var name = (string?)test.Attribute("name") ?? "";
                if (!int.TryParse(name.Split('.', 2)[0], out var number))
                    continue;
                found[number] = test.Element("skipped") is not null ? CaseStatus.Skipped : test.Element("failure") is not null ? CaseStatus.Failed : CaseStatus.Passed;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            // No last results to show, then.
        }
        return found;
    }

    /// <summary>A host that shows only the commands, in a feed, for running a script outside a run.</summary>
    private sealed class CommandsTo(Feed feed, Dispatcher dispatcher) : IRunHost
    {
        public void CaseStarted(int index, int count, Case item) { }
        public void CaseFinished(CaseResult result) { }
        public void ShowStep(string text, Doing kind = Doing.Say, string? command = null) { }
        public void ShowWait(string what) { }
        public void CommandStarted(string line, string? script) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandStarted(line, script));
        public void CommandOutput(string line, bool error) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandOutput(line, error));
        public void CommandFinished(int exitCode, TimeSpan took) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandFinished(exitCode, took));
        public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) { }
        public void ShowCarryingOn(TimeSpan after) { }
        public StuckAnswer AskStuck(string message, string screenshot) => StuckAnswer.Stop;
        public bool AskScriptError(string message) => false;
        public void KeepClear(ScreenRect rect) { }
        public void Finished(string summary, string resultsFolder, bool allPassed) { }
    }
}
