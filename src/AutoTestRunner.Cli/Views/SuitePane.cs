using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// One suite in the launcher: whether the app and its files are there and the machine is clean, with a button that
/// cleans it; then its cases to tick, each with how it went last time, a pencil to add steps to it, and New case.
/// </summary>
internal sealed class SuitePane : Grid
{
    private readonly Suite _suite;
    private readonly Options _options;
    private readonly Action<BuildRequest> _build;
    private readonly List<(Case Case, CheckBox Box, Border Needs)> _rows = [];
    private readonly Border _files, _machine;
    private readonly Button _clean;
    private readonly ActivityFeed _cleaning;
    private readonly Border _cleaningBox;
    private readonly Dictionary<int, CaseStatus> _last;
    private bool _missing, _busy;

    /// <param name="suite">The suite to show.</param>
    /// <param name="options">The options it opened with: the cases given are ticked, and where its results go.</param>
    /// <param name="build">Called with a case to build, from New case or a case's pencil.</param>
    public SuitePane(Suite suite, Options options, Action<BuildRequest> build)
    {
        (_suite, _options, _build) = (suite, options, build);
        _last = LastResults(ResultsFolder(suite, options));
        for (var row = 0; row < 3; row++)
            RowDefinitions.Add(new RowDefinition { Height = row == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // ===== The suite, and whether it can run =====
        var title = Ui.Words(suite.Name, 20, Ui.Ink, FontWeights.Bold);
        title.ToolTip = suite.Folder;
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
        if (suite.App.RunAsAdmin && !Elevation.IsAdministrator)
        {
            var admin = Ui.Pill(Ui.IconText(Icons.Admin, "Asks for administrator when it starts", Ui.Muted), Ui.Surface, 12, new Thickness(10, 4, 12, 4));
            admin.Margin = new Thickness(0, 0, 8, 6);
            admin.ToolTip = "The app runs as administrator, so autotest restarts itself as administrator to drive it";
            state.Children.Add(admin);
        }
        state.Children.Add(_clean);
        _cleaning = new ActivityFeed("Clean up");
        _cleaningBox = new Border { Height = 170, Margin = new Thickness(0, 4, 0, 6), Child = _cleaning, Visibility = Visibility.Collapsed };
        var top = new StackPanel();
        top.Children.Add(title);
        top.Children.Add(state);
        top.Children.Add(_cleaningBox);
        Children.Add(top);

        // ===== The cases =====
        var pick = new DockPanel { Margin = new Thickness(0, 6, 0, 2), LastChildFill = false };
        pick.Children.Add(Ui.LabelledButton(Icons.Choose, "All", () => Tick(_ => true)));
        pick.Children.Add(Ui.LabelledButton(Icons.Cross, "None", () => Tick(_ => false)));
        if (_last.Values.Any(status => status != CaseStatus.Passed))
            pick.Children.Add(Ui.LabelledButton(Icons.Failed, "Didn't pass last time", () => Tick(item => _last.TryGetValue(item.Number, out var status) && status != CaseStatus.Passed),
                glyphColor: Ui.Fail));
        var add = Ui.LabelledButton(Icons.Add, "New case", NewCase);
        add.ToolTip = "Build a case by using the app: each click and bit of typing becomes a step";
        add.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(add, Dock.Right);
        pick.Children.Add(add);
        SetRow(pick, 1);
        Children.Add(pick);

        var list = new StackPanel();
        foreach (var item in suite.Cases)
            list.Children.Add(CaseRow(item));
        if (suite.Cases.Count == 0)
            list.Children.Add(new TextBlock { Text = "No cases yet. New case builds one by using the app.", Foreground = Ui.Muted, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap });
        var cases = new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 4, 0, 0),
            Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4) },
        };
        SetRow(cases, 2);
        Children.Add(cases);

        Tick(item => options.Cases is null || suite.Select(options.Cases).Contains(item));
        CheckFiles();
        Loaded += (_, _) => CheckMachine();
    }

    public Suite Suite => _suite;

    /// <summary>The cases ticked, in order.</summary>
    public IReadOnlyList<Case> Ticked => [.. _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Case)];

    /// <summary>Whether it can start: the app and its files are there, and nothing is being cleaned up.</summary>
    public bool Ready => !_missing && !_busy;

    /// <summary>The ticks changed, or whether it can start.</summary>
    public event Action? Changed;

    /// <summary>Where a suite's results go, as autotest run puts them.</summary>
    public static string ResultsFolder(Suite suite, Options options) => options.Results ?? Path.Combine(suite.Folder, "results");

    /// <summary>How each case went in the newest results folder, from its junit.xml.</summary>
    public static Dictionary<int, CaseStatus> LastResults(string parent)
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

    private Grid CaseRow(Case item)
    {
        var row = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 1, 0, 1), ToolTip = item.Covers };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Focusable = false };
        box.Checked += (_, _) => OnTicked();
        box.Unchecked += (_, _) => OnTicked();
        var number = Ui.Pill(Ui.Words($"{item.Number}", 11, Ui.Accent, FontWeights.Bold, wrap: false), Ui.AccentSoft, 8, new Thickness(6, 1, 6, 1));
        number.HorizontalAlignment = HorizontalAlignment.Left;
        var name = Ui.Words(item.Name, 13.5, Ui.Ink, wrap: false);
        name.Margin = new Thickness(0, 6, 8, 6);
        var needs = Ui.Pill(new TextBlock(), Ui.Surface, 8, new Thickness(6, 1, 7, 1));
        needs.Margin = new Thickness(0, 0, 6, 0);
        needs.Visibility = item.Needs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // The pencil shows while the mouse is over the row, as the app's own row buttons do.
        var edit = Ui.IconButton(Icons.Edit, "Add steps to this case, by using the app", () => _build(new BuildRequest(_suite, item.Name, AddTo: item)), 14);
        edit.Padding = new Thickness(6, 3, 6, 3);
        edit.Visibility = Visibility.Hidden;
        var last = _last.TryGetValue(item.Number, out var status) ? Ui.Glyph(Icons.Of(status), 14, Ui.Of(status)) : new TextBlock();
        last.HorizontalAlignment = HorizontalAlignment.Center;
        last.ToolTip = _last.ContainsKey(item.Number) ? $"Last time: {status.ToString().ToLowerInvariant()}" : null;
        SetColumn(number, 1);
        SetColumn(name, 2);
        SetColumn(needs, 3);
        SetColumn(edit, 4);
        SetColumn(last, 5);
        row.Children.Add(box);
        row.Children.Add(number);
        row.Children.Add(name);
        row.Children.Add(needs);
        row.Children.Add(edit);
        row.Children.Add(last);
        row.MouseEnter += (_, _) =>
        {
            row.Background = Ui.Surface;
            edit.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            edit.Visibility = Visibility.Hidden;
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is not CheckBox && !edit.IsMouseOver)
                box.IsChecked = box.IsChecked != true;
        };
        _rows.Add((item, box, needs));
        return row;
    }

    private void Tick(Func<Case, bool> which)
    {
        foreach (var (item, box, _) in _rows)
            box.IsChecked = which(item);
        OnTicked();
    }

    // Each case's needs show what it runs after, in amber when that case isn't ticked, so the app may not be ready for it.
    private void OnTicked()
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
        Changed?.Invoke();
    }

    private void NewCase()
    {
        if (NewCaseWindow.Ask(Window.GetWindow(this), _suite) is { } request)
            _build(request);
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
        OnTicked();
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
        OnTicked();
        _cleaningBox.Visibility = Visibility.Visible;
        var view = new CommandsOnlyView(_cleaning, Dispatcher);
        var worker = new Thread(() =>
        {
            try
            {
                var control = new RunControl(unattended: true);
                var results = new Results(Path.Combine(Path.GetTempPath(), "AutoTestRunner", "cleanup"), console: null);
                using var driver = new FlaUiDriver(control, () => null, _ => { }, _ => { });
                new Runner(_suite, driver, new WindowsMachine(), view, control, results, _options.Variables).ResetMachine();
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() => _cleaning.Note("Clean up failed: " + ex.Message));
            }
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                _clean.IsEnabled = true;
                OnTicked();
                CheckMachine();
            });
        });
        worker.SetApartmentState(ApartmentState.MTA);
        worker.IsBackground = true;
        worker.Start();
    }

    /// <summary>A view that shows only the commands, in a feed, for running a script outside a run.</summary>
    private sealed class CommandsOnlyView(ActivityFeed feed, Dispatcher dispatcher) : IRunView
    {
        public void CaseStarted(int index, int count, Case item) { }
        public void CaseFinished(CaseResult result) { }
        public void ShowStep(string text, ActivityKind kind = ActivityKind.Say, string? command = null) { }
        public void ShowWait(string what) { }
        public void CommandStarted(string line, string? script) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandStarted(line, script));
        public void CommandOutput(string line, bool error) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandOutput(line, error));
        public void CommandFinished(int exitCode, TimeSpan took) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandFinished(exitCode, took));
        public void StepFailed(string message) { }
        public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) { }
        public void ShowCarryingOn(TimeSpan after) { }
        public StuckAnswer AskStuck(string message, string screenshot) => StuckAnswer.Stop;
        public bool AskScriptError(string message) => false;
        public void KeepClear(ScreenRect rect) { }
        public void Finished(string summary, string resultsFolder, bool allPassed) { }
    }
}
