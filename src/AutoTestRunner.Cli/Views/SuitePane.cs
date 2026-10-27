using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// One suite in the launcher: whether the app and its files are there, the machine is clean, with a button that
/// cleans it, and the software it needs is installed; then its cases to tick, each with how it went last time, a
/// warning when it needs looking at since a case it carries on from changed, a pencil to change it, and New case.
/// </summary>
internal sealed class SuitePane : Grid
{
    private readonly Suite _suite;
    private readonly Options _options;
    private readonly Action<BuildRequest> _build;
    private readonly Action _reload;
    private readonly List<(Case Case, CheckBox Box, Border Needs)> _rows = [];
    private readonly Border _files, _machine;
    private readonly Button _clean;
    private readonly ActivityFeed _cleaning;
    private readonly Border _cleaningBox;
    private readonly Dictionary<int, CaseStatus> _last;

    // The software the suite needs, each a pill that says whether it is there.
    private readonly WrapPanel _software = new() { Visibility = Visibility.Collapsed };

    // The suites whose missing software was offered this session, so it is asked once, not each time the suite is picked.
    private static readonly HashSet<string> Offered = new(StringComparer.OrdinalIgnoreCase);
    private bool _missing, _busy;

    /// <param name="suite">The suite to show.</param>
    /// <param name="options">The options it opened with: the cases given are ticked, and where its results go.</param>
    /// <param name="build">Called with a case to build, from New case or a case's pencil.</param>
    /// <param name="reload">Called after the suite's files changed here, to load it again.</param>
    /// <param name="newSuite">Called by New suite; null where there is none.</param>
    public SuitePane(Suite suite, Options options, Action<BuildRequest> build, Action? reload = null, Action? newSuite = null)
    {
        (_suite, _options, _build, _reload) = (suite, options, build, reload ?? (() => { }));
        _last = LastResults(options.ResultsFolder(suite));
        for (var row = 0; row < 3; row++)
            RowDefinitions.Add(new RowDefinition { Height = row == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // ===== The suite, and whether it can run =====
        var state = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        _files = Pill(new TextBlock(), Ui.Surface);
        _machine = Pill(new TextBlock(), Ui.Surface);
        _clean = Ui.LabelledButton(Icons.Clean, "Clean up", CleanUp);
        _clean.Visibility = Visibility.Collapsed;
        _clean.ToolTip = "Close the app and run the suite's reset script, so the machine is ready for case 1";
        state.Children.Add(_files);
        if (suite.Reset.Leftovers is not null)
            state.Children.Add(_machine);
        if (suite.App.RunAsAdmin && !Elevation.IsAdministrator)
        {
            var admin = Pill(Ui.IconText(Icons.Admin, "Asks for administrator when it starts", Ui.Muted), Ui.Surface);
            admin.ToolTip = "The app runs as administrator, so AutoTestRunner restarts as administrator to control it";
            state.Children.Add(admin);
        }
        state.Children.Add(_clean);
        state.Children.Add(_software);
        _cleaning = new ActivityFeed("Clean up");
        // At most so tall, so that folding the log away gives the space back to the cases.
        var cleaningLog = new Grid();
        cleaningLog.Children.Add(_cleaning);
        var hide = Ui.IconButton(Icons.Cross, "Hide the clean up log", () => _cleaningBox!.Visibility = Visibility.Collapsed, 11, new Thickness(5, 3, 5, 3));
        hide.HorizontalAlignment = HorizontalAlignment.Right;
        hide.VerticalAlignment = VerticalAlignment.Top;
        hide.Margin = new Thickness(0, 2, 22, 0);
        cleaningLog.Children.Add(hide);
        _cleaningBox = new Border { MaxHeight = 170, Margin = new Thickness(0, 4, 0, 6), Child = cleaningLog, Visibility = Visibility.Collapsed };
        var tools = new List<Button>
        {
            TitleTool(Icons.Controls, "Named controls: where each is used, point it out in the app, rename it everywhere",
                () => ControlsWindow.Open(Window.GetWindow(this), _suite, _options, _reload, ids: false)),
            TitleTool(Icons.Ids, "Controls without ids: the controls in the app a test would use that have no AutomationId, with the XAML to add",
                () => ControlsWindow.Open(Window.GetWindow(this), _suite, _options, _reload, ids: true)),
        };
        if (newSuite is not null)
            tools.Add(TitleTool(Icons.NewFolder, "New suite: pick an app, read its window and point at its status bar", newSuite));
        var top = new StackPanel();
        top.Children.Add(TitleRow(suite.Name, suite.Folder, tools));
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
        if (suite.Tags.Count > 0)
            pick.Children.Add(TagFilter(suite.Tags));
        var add = Ui.LabelledButton(Icons.Add, "New case", NewCase);
        add.ToolTip = "Record a new case: use the app, and each click and entry becomes a step";
        add.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(add, Dock.Right);
        pick.Children.Add(add);
        SetRow(pick, 1);
        Children.Add(pick);

        var list = new StackPanel();
        foreach (var item in suite.Cases)
            list.Children.Add(CaseRow(item));
        if (suite.Cases.Count == 0)
            list.Children.Add(new TextBlock { Text = "No cases yet. Click New case to record one.", Foreground = Ui.Muted, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap });
        var cases = new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 4, 0, 0),
            Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4) },
        };
        SetRow(cases, 2);
        Children.Add(cases);

        Tick(item => options.Cases is null && options.Tags.Count == 0 || options.Pick(suite).Contains(item));
        CheckFiles();
        Loaded += (_, _) =>
        {
            CheckMachine();
            CheckSoftware();
        };
    }

    public Suite Suite => _suite;

    /// <summary>Whether it is cleaning up, when loading the suite again would lose what that shows.</summary>
    public bool Busy => _busy;

    /// <summary>
    /// A suite's name, with icons beside it: its own tools, then those that open its files to change by hand, suite.json
    /// in Notepad++, or Notepad when there is none, and its folder in Explorer. The launcher loads the suite again once
    /// they are changed.
    /// </summary>
    public static DockPanel TitleRow(string name, string folder, IEnumerable<Button> tools)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in tools.Concat(FileTools(folder)))
            bar.Children.Add(button);
        var title = Ui.Words(name, 20, Ui.Ink, FontWeights.Bold);
        title.ToolTip = folder;
        var row = new DockPanel();
        DockPanel.SetDock(bar, Dock.Right);
        row.Children.Add(bar);
        row.Children.Add(title);
        return row;
    }

    private static IEnumerable<Button> FileTools(string folder)
    {
        var config = Path.Combine(folder, "suite.json");
        var editor = ScriptEditor.NotepadPlusPlus() is null ? "Notepad" : "Notepad++";
        yield return TitleTool(Icons.Setting, $"Edit suite.json in {editor}\n{config}", () =>
        {
            if (!ScriptEditor.Open(config))
                MessageBox.Show($"Couldn't start Notepad++ or Notepad to open {config}.", "AutoTestRunner", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
        yield return TitleTool(Icons.Folder, $"Open the suite's folder\n{folder}", () => ScriptEditor.ShowFolder(folder));
    }

    // An icon beside the suite's name.
    private static Button TitleTool(string glyph, string tip, Action click)
    {
        var button = Ui.IconButton(glyph, tip, click, 15, new Thickness(7, 4, 7, 4));
        // The tip's second line is a path, which a screen reader or a test needn't hear.
        AutomationProperties.SetName(button, tip.Split('\n')[0]);
        return button;
    }

    /// <summary>The cases ticked, in order.</summary>
    public IReadOnlyList<Case> Ticked => [.. _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Case)];

    /// <summary>Whether it can start: the app and its files are there, and nothing is being cleaned up.</summary>
    public bool Ready => !_missing && !_busy;

    /// <summary>The ticks changed, or whether it can start.</summary>
    public event Action? Changed;

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

    // The tags chosen tick the cases with any of them, as --tags picks them.
    private TagPicker TagFilter(IReadOnlyList<string> tags)
    {
        var picker = new TagPicker(tags, _options.Tags);
        picker.Changed += () => Tick(item => picker.On.Count == 0 || item.Tags.Any(picker.On.Contains));
        return picker;
    }

    private Grid CaseRow(Case item)
    {
        var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1), ToolTip = item.Covers };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
        if (item.Reset.Count > 0)
        {
            name.Inlines.Add(new System.Windows.Documents.Run("  "));
            var reset = Ui.GlyphInline(Icons.Reset, 11, Ui.Faint);
            reset.ToolTip = $"Has {(item.Reset.Count == 1 ? "a reset step" : $"{item.Reset.Count} reset steps")}: the cases that start after it go back to where it leaves the app, without starting it again.";
            name.Inlines.Add(reset);
        }
        if (item.Tags.Count > 0)
            name.Inlines.Add(new System.Windows.Documents.Run("   " + string.Join("  ", item.Tags.Select(tag => "#" + tag))) { Foreground = Ui.Faint, FontSize = 11.5 });
        var needs = Ui.Pill(new TextBlock(), Ui.Surface, 8, new Thickness(6, 1, 7, 1));
        needs.Margin = new Thickness(0, 0, 6, 0);
        needs.Visibility = item.Needs.Count > 0 || item.AfterReset is not null ? Visibility.Visible : Visibility.Collapsed;
        needs.MouseLeftButtonUp += (_, e) =>
        {
            TickWithNeeds(item);
            e.Handled = true;
        };
        needs.MouseEnter += (_, _) => needs.BorderBrush = Ui.Accent;
        needs.MouseLeave += (_, _) => needs.BorderBrush = Brushes.Transparent;
        (needs.BorderBrush, needs.BorderThickness, needs.Cursor) = (Brushes.Transparent, new Thickness(1), Cursors.Hand);
        // The pencil shows while the mouse is over the row, as the app's own row buttons do.
        var pencil = Ui.IconButton(Icons.Edit, "Edit this case in the builder", () => _build(new BuildRequest(_suite, item.Name, Edit: item)), 14, new Thickness(6, 3, 6, 3));
        Button? more = null;
        more = Ui.IconButton(Icons.More, "Copy, rename, move or delete this case", () => CaseMenu(item, more!).IsOpen = true, 14, new Thickness(6, 3, 6, 3));
        var edit = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Hidden, Children = { pencil, more } };
        row.ContextMenu = CaseMenu(item, row);
        var last = _last.TryGetValue(item.Number, out var status) ? Ui.Glyph(Icons.Of(status), 14, Ui.Of(status)) : new TextBlock();
        last.HorizontalAlignment = HorizontalAlignment.Center;
        last.ToolTip = _last.ContainsKey(item.Number) ? $"Last time: {status.ToString().ToLowerInvariant()}" : null;
        var review = Review(item);
        SetColumn(number, 1);
        SetColumn(name, 2);
        SetColumn(review, 3);
        SetColumn(needs, 4);
        SetColumn(edit, 5);
        SetColumn(last, 6);
        row.Children.Add(review);
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
            if (e.OriginalSource is not CheckBox && !edit.IsMouseOver && !review.IsMouseOver && !needs.IsMouseOver)
                box.IsChecked = box.IsChecked != true;
        };
        _rows.Add((item, box, needs));
        return row;
    }

    // What can be done to a case's file: a copy, a new name, a place up or down, or deleting it.
    private ContextMenu CaseMenu(Case item, UIElement under)
    {
        var menu = new ContextMenu { PlacementTarget = under, Placement = PlacementMode.Bottom };
        void Item(string glyph, string text, string? cant, Action act)
        {
            var entry = new MenuItem { Header = text, Icon = Ui.Glyph(glyph, 13, Ui.Accent), IsEnabled = cant is null, ToolTip = cant };
            ToolTipService.SetShowOnDisabled(entry, true);
            entry.Click += (_, _) => Change(text, act);
            menu.Items.Add(entry);
        }
        Item(Icons.Copy, "Copy", null, () => CaseFiles.Duplicate(_suite, item));
        Item(Icons.Name, "Rename…", null, () =>
        {
            if (Ui.Prompt(Window.GetWindow(this), "Rename", "New name for the case. Its file and the cases that carry on from it stay as they are.", item.Name, "Rename", Icons.Name, tall: false) is { } name
                && name.Trim().Length > 0 && name.Trim() != item.Name)
                CaseFiles.Rename(item, name);
        });
        Item(Icons.Up, "Move up", CaseFiles.CantMove(_suite, item, -1), () => CaseFiles.Move(_suite, item, -1));
        Item(Icons.Down, "Move down", CaseFiles.CantMove(_suite, item, 1), () => CaseFiles.Move(_suite, item, 1));
        menu.Items.Add(new Separator());
        Item(Icons.Delete, "Delete…", null, () =>
        {
            var after = CaseFiles.Dependents(_suite, item);
            var warning = after.Count == 0 ? "" : $"\n\n{string.Join(", ", after.Select(other => $"{other.Number}. {other.Name}"))} carry on from it. They will carry on from what it carried on from instead, and be marked as needing a look.";
            if (MessageBox.Show(Window.GetWindow(this), $"Delete case {item.Number}, {item.Name}? Its file is deleted.{warning}", "Delete case", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                CaseFiles.Delete(_suite, item);
        });
        return menu;
    }

    // Changes the suite's files, then loads it again; or says why not.
    private void Change(string what, Action act)
    {
        try
        {
            act();
            _reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, what.TrimEnd('…'), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // An amber warning on a case that needs looking at, saying why; clicked once it has been looked at, it goes.
    private Button Review(Case item)
    {
        Button? warning = null;
        warning = Ui.IconButton(Icons.Warning, $"Needs checking: {item.Review}\n\nClick to clear this warning once the case has been checked.", () =>
        {
            if (MessageBox.Show(Window.GetWindow(this), $"Clear the warning on case {item.Number}, {item.Name}?\n\n{item.Review}", "Needs checking",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                CaseWriter.ClearReview(item);
                warning!.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(Window.GetWindow(this), ex.Message, "Needs checking", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }, 14);
        ((TextBlock)warning.Content).Foreground = Ui.Hold;
        warning.Padding = new Thickness(6, 3, 6, 3);
        warning.Margin = new Thickness(0, 0, 4, 0);
        warning.Visibility = item.Review is null ? Visibility.Collapsed : Visibility.Visible;
        return warning;
    }

    private void Tick(Func<Case, bool> which)
    {
        foreach (var (item, box, _) in _rows)
            box.IsChecked = which(item);
        OnTicked();
    }

    // Ticks a case and every case it runs after, from its needs pill.
    private void TickWithNeeds(Case item)
    {
        var leading = _suite.Leading(item);
        foreach (var (other, box, _) in _rows)
            if (leading.Contains(other))
                box.IsChecked = true;
        OnTicked();
    }

    // Each case's needs show what it runs after, in amber when that case isn't ticked, so the app may not be ready for it.
    private void OnTicked()
    {
        var ticked = _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Case.Id).ToHashSet();
        foreach (var (item, box, needs) in _rows)
        {
            // A case after another's reset still runs when that case isn't ticked, as the run plays it first; it is amber
            // all the same, as the needs are, so a case left out shows.
            if (item.AfterReset is { } id && _suite.WithId(id) is { } setup)
            {
                var leftOut = box.IsChecked == true && !ticked.Contains(id);
                needs.Background = leftOut ? Ui.HoldSoft : Ui.Surface;
                needs.Child = Ui.IconText(Icons.Reset, $"{setup.Number}", leftOut ? Ui.Hold : Ui.Muted, 11);
                needs.ToolTip = $"Starts after the reset of {setup.Number}. {setup.Name}, without starting the app again"
                    + (ticked.Contains(id) ? "." : ". It isn't ticked, so the run plays it first.") + "\nClick to tick it too.";
                continue;
            }
            if (item.Needs.Count == 0)
                continue;
            var numbers = item.Needs.Select(_suite.WithId).OfType<Case>().ToList();
            var ready = box.IsChecked != true || item.Needs.All(ticked.Contains);
            needs.Background = ready ? Ui.Surface : Ui.HoldSoft;
            needs.Child = Ui.IconText(Icons.Back, string.Join(", ", numbers.Select(other => other.Number)), ready ? Ui.Muted : Ui.Hold, 11);
            needs.ToolTip = "Runs after " + string.Join(" and ", numbers.Select(other => $"{other.Number}. {other.Name}"))
                + (ready ? "" : ", which isn't ticked, so the app may not be ready for it") + ".\nClick to tick this case and every case it needs.";
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
        var globals = Globals();
        var missing = Runner.Missing(_suite, globals);
        _missing = missing.Count > 0;
        _files.Background = _missing ? Ui.FailSoft : Ui.PassSoft;
        _files.Child = _missing
            ? Ui.IconText(Icons.Failed, "Can't find " + string.Join(", ", missing.Select(path => Path.GetFileName(path.TrimEnd('\\', '/')))), Ui.Fail)
            : Ui.IconText(Icons.Passed, "App and files found", Ui.Pass);
        _files.ToolTip = _missing ? string.Join("\n", missing) : globals.Expand(_suite.App.Exe);
        OnTicked();
    }

    // Runs the suite's leftovers script in the background, as it takes a few seconds.
    private void CheckMachine()
    {
        if (_suite.Reset.Leftovers is null)
            return;
        _machine.Background = Ui.Surface;
        _machine.Child = Looking("Checking for leftovers from earlier runs");
        _clean.Visibility = Visibility.Collapsed;
        Task.Run<(IReadOnlyList<string> Leftovers, string? Problem)>(() =>
        {
            try
            {
                return (Runner.Leftovers(_suite, new WindowsMachine()), null);
            }
            catch (Exception ex)
            {
                Crashes.Log(ex, $"Checking {_suite.Name} for leftovers.");
                return ([], ex.Message);
            }
        }).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            var (leftovers, problem) = task.Result;
            var clean = leftovers.Count == 0 && problem is null;
            _machine.Background = clean ? Ui.PassSoft : Ui.HoldSoft;
            _machine.Child = problem is not null ? Ui.IconText(Icons.Warning, "Couldn't check for leftovers", Ui.Hold)
                : clean ? Ui.IconText(Icons.Passed, "Machine is clean", Ui.Pass)
                : Ui.IconText(Icons.Warning, "Left over: " + string.Join(", ", leftovers.Take(3)) + (leftovers.Count > 3 ? $" and {leftovers.Count - 3} more" : ""), Ui.Hold);
            _machine.ToolTip = problem ?? (clean ? "Nothing left over from an earlier run" : string.Join("\n", leftovers) + "\n\nClean up removes these. Running from case 1 also does.");
            _clean.Visibility = clean ? Visibility.Collapsed : Visibility.Visible;
        }));
    }

    // Looks for the suite's software in the background, as winget takes a few seconds, and shows a pill for each: a tick
    // when it is there, an amber arrow when it isn't. Clicking one offers to install; the first time this session that
    // something is missing, the offer comes by itself.
    private void CheckSoftware()
    {
        if (_suite.Software.Count == 0)
            return;
        _software.Visibility = Visibility.Visible;
        _software.Children.Clear();
        _software.Children.Add(Pill(Looking("Looking for the software it needs"), Ui.Surface));
        var globals = Globals();
        Task.Run(() => Software.Detect(_suite, new WindowsMachine(), globals)).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            _software.Children.Clear();
            if (task.Exception is { } failed)
            {
                Crashes.Log(failed, $"Looking for {_suite.Name}'s software.");
                _software.Children.Add(Pill(Ui.IconText(Icons.Warning, "Couldn't look for its software", Ui.Hold), Ui.HoldSoft));
                return;
            }
            var states = task.Result;
            foreach (var state in states)
            {
                var (glyph, color, background) = state.Status switch
                {
                    SoftwareStatus.Installed => (Icons.Passed, Ui.Pass, Ui.PassSoft),
                    SoftwareStatus.Unknown => (Icons.Warning, Ui.Muted, Ui.Surface),
                    _ => (Icons.Download, Ui.AmberInk, Ui.AmberSoft),
                };
                var pill = Pill(Ui.IconText(glyph, state.Item.Title, color), background);
                pill.ToolTip = state.Line + (state.Wanted ? "\nClick to install it." : "");
                pill.Cursor = Cursors.Hand;
                pill.MouseLeftButtonUp += (_, _) => OfferSoftware(states);
                _software.Children.Add(pill);
            }
            if (states.Any(state => state.Wanted) && Offered.Add(_suite.Folder))
                OfferSoftware(states);
        }));
    }

    private void OfferSoftware(IReadOnlyList<SoftwareState> states)
    {
        if (SoftwareWindow.Ask(Window.GetWindow(this), _suite, _options, states))
            CheckSoftware();
    }

    private VariableScope Globals() => Runner.Variables(_suite, Path.GetTempPath(), _options.Variables);

    // A spinner and what is being looked at, for a pill while it takes a few seconds.
    private static StackPanel Looking(string text)
    {
        var looking = new StackPanel { Orientation = Orientation.Horizontal };
        looking.Children.Add(Ui.Spinner(12, Ui.Muted));
        looking.Children.Add(new TextBlock { Text = text, Foreground = Ui.Muted, FontSize = 12, Margin = new Thickness(6, 0, 0, 0) });
        return looking;
    }

    private static Border Pill(UIElement child, Brush background)
    {
        var pill = Ui.Pill(child, background, 12, new Thickness(10, 4, 12, 4));
        pill.Margin = new Thickness(0, 0, 8, 6);
        return pill;
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
                Crashes.Log(ex, $"Cleaning up after {_suite.Name}.");
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
        public void CommandStarted(string line, string? script) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandStarted(line, script));
        public void CommandOutput(string line, bool error) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandOutput(line, error));
        public void CommandFinished(int exitCode, TimeSpan took) => dispatcher.BeginInvoke(DispatcherPriority.Normal, () => feed.CommandFinished(exitCode, took));
    }
}
