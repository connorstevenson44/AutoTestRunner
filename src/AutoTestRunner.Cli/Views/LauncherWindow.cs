using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// The window before a run you watch. Down the left, every suite found near the runner, such as the suites beside it
/// in a ready-to-run kit; on the right, the chosen suite: whether it can run, its cases to tick, New case to build one;
/// then how it runs and how fast, and Start. With one suite there is no list on the left.
/// </summary>
internal sealed class LauncherWindow : Window
{
    private const double RailWidth = 250;

    private static readonly (string Mode, string Glyph, string Text, string Tip)[] Modes =
    [
        ("auto", Icons.Play, "Automatic", "Runs by itself, showing each checkpoint for a few seconds. Space holds it there."),
        ("pause-at-checkpoints", Icons.CheckList, "Pause at checkpoints", "Runs by itself, and waits for you at each checkpoint."),
        ("step-by-step", Icons.Next, "Step by step", "Waits for you to press Enter before each step."),
    ];

    private readonly IReadOnlyList<FoundSuite> _suites;
    private readonly Options _options;
    // The suite the launcher opened on, which the cases given on the command line are for.
    private readonly FoundSuite _first;
    private readonly ContentControl _pane = new();
    private readonly List<(FoundSuite Suite, Border Row)> _railRows = [];
    private readonly ToggleButton[] _modes, _speeds;
    private readonly Button _start;
    private SuitePane? _suite;

    public LauncherWindow(IReadOnlyList<FoundSuite> suites, FoundSuite chosen, Options options)
    {
        (_suites, _options, _first) = (suites, options, chosen);
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = suites.Count > 1 ? "AutoTestRunner" : chosen.Name;
        var area = SystemParameters.WorkArea;
        var rail = suites.Count > 1;
        Width = Math.Min(780 + (rail ? RailWidth : 0), area.Width - 40);
        Height = Math.Min(760, area.Height - 40);
        MinWidth = 560 + (rail ? RailWidth : 0);
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;
        FontFamily = Ui.Text;
        Foreground = Ui.Ink;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = rail ? new GridLength(RailWidth) : new GridLength(0) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        if (rail)
            root.Children.Add(Rail());

        var main = new Grid { Margin = new Thickness(22, 18, 22, 18) };
        for (var row = 0; row < 3; row++)
            main.RowDefinitions.Add(new RowDefinition { Height = row == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        main.Children.Add(_pane);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        // ===== How it runs =====
        var how = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        how.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        how.ColumnDefinitions.Add(new ColumnDefinition());
        how.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        _modes = [.. Modes.Select(mode =>
        {
            var toggle = Tile(mode.Glyph, mode.Text, mode.Tip);
            toggle.IsChecked = options.Mode == mode.Mode;
            modes.Children.Add(toggle);
            return toggle;
        })];
        foreach (var toggle in _modes)
            toggle.Click += (_, _) => Only(_modes, toggle);
        var speeds = new StackPanel { Orientation = Orientation.Horizontal };
        _speeds = [.. new[] { (Icons.SpeedSlow, "Slow"), (Icons.SpeedModest, "Modest"), (Icons.SpeedFast, "Fast") }.Select((speed, index) =>
        {
            var toggle = Tile(speed.Item1, speed.Item2, null);
            toggle.IsChecked = (int)options.Speed == index;
            speeds.Children.Add(toggle);
            return toggle;
        })];
        foreach (var toggle in _speeds)
            toggle.Click += (_, _) => Only(_speeds, toggle);
        Grid.SetColumn(speeds, 2);
        how.Children.Add(modes);
        how.Children.Add(speeds);
        Grid.SetRow(how, 1);
        main.Children.Add(how);

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
        Grid.SetRow(go, 2);
        main.Children.Add(go);
        Content = root;

        Choose(chosen);
    }

    /// <summary>What to run, once Start was clicked; null when the launcher was closed instead.</summary>
    public LauncherChoice? Picked { get; private set; }

    /// <summary>The case to build, once New case or a case's pencil was clicked.</summary>
    public BuildRequest? Build { get; private set; }

    /// <summary>True when autotest started again as administrator to do what was picked, so this one ends.</summary>
    public bool Restarted { get; private set; }

    // ===== The suites =====

    private Border Rail()
    {
        var list = new StackPanel { Margin = new Thickness(10, 16, 10, 16) };
        var heading = Ui.Heading("Suites");
        heading.Margin = new Thickness(8, 0, 0, 8);
        list.Children.Add(heading);
        foreach (var found in _suites)
            list.Children.Add(RailRow(found));
        return new Border
        {
            Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
    }

    // A suite down the left: its name, how many cases and how the last run went, or that it has mistakes.
    private Border RailRow(FoundSuite found)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = Ui.Glyph(found.Suite is null ? Icons.Failed : Icons.Suites, 15, found.Suite is null ? Ui.Fail : Ui.Muted);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 2, 0, 0);
        var words = new StackPanel();
        words.Children.Add(Ui.Words(found.Name, 13, Ui.Ink, FontWeights.SemiBold, wrap: false));
        var (summary, color) = Summary(found);
        words.Children.Add(Ui.Words(summary, 11.5, color, wrap: false));
        Grid.SetColumn(words, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(words);
        if (found.Suite?.App.RunAsAdmin == true && !Elevation.IsAdministrator)
        {
            var admin = Ui.Glyph(Icons.Admin, 13, Ui.Faint);
            admin.ToolTip = "Asks for administrator when it starts";
            admin.VerticalAlignment = VerticalAlignment.Top;
            admin.Margin = new Thickness(6, 3, 0, 0);
            Grid.SetColumn(admin, 2);
            grid.Children.Add(admin);
        }
        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = found.Folder,
        };
        row.MouseEnter += (_, _) =>
        {
            if (!ReferenceEquals(_pane.Tag, found))
                row.Background = Ui.Line;
        };
        row.MouseLeave += (_, _) => row.Background = ReferenceEquals(_pane.Tag, found) ? Ui.AccentSoft : Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => Choose(found);
        _railRows.Add((found, row));
        return row;
    }

    private (string Text, Brush Color) Summary(FoundSuite found)
    {
        if (found.Suite is not { } suite)
            return ($"{found.Problems.Count} {(found.Problems.Count == 1 ? "mistake" : "mistakes")} in its scripts", Ui.Fail);
        var cases = $"{suite.Cases.Count} {(suite.Cases.Count == 1 ? "case" : "cases")}";
        var last = SuitePane.LastResults(SuitePane.ResultsFolder(suite, _options));
        if (last.Count == 0)
            return (cases, Ui.Muted);
        var passed = last.Values.Count(status => status == CaseStatus.Passed);
        return passed == last.Count ? ($"{cases} · all passed last time", Ui.Pass) : ($"{cases} · {passed} of {last.Count} passed last time", Ui.Hold);
    }

    private void Choose(FoundSuite found)
    {
        _pane.Tag = found;
        foreach (var (each, row) in _railRows)
            row.Background = each == found ? Ui.AccentSoft : Brushes.Transparent;
        if (found.Suite is { } suite)
        {
            _suite = new SuitePane(suite, found == _first ? _options : _options with { Cases = null }, Building);
            _suite.Changed += Changed;
            _pane.Content = _suite;
        }
        else
        {
            _suite = null;
            _pane.Content = Problems(found);
        }
        if (_suites.Count > 1)
            Title = found.Name;
        Changed();
    }

    // A suite that can't load: what is wrong with its scripts, as validate says it.
    private static StackPanel Problems(FoundSuite found)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Words(found.Name, 20, Ui.Ink, FontWeights.Bold));
        var pill = Ui.Pill(Ui.IconText(Icons.Failed, $"Can't run: {found.Problems.Count} {(found.Problems.Count == 1 ? "mistake" : "mistakes")} in its scripts", Ui.Fail),
            Ui.FailSoft, 12, new Thickness(10, 4, 12, 4));
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        pill.Margin = new Thickness(0, 10, 0, 10);
        panel.Children.Add(pill);
        var list = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
        foreach (var problem in found.Problems)
        {
            var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var mark = Ui.Glyph(Icons.Cross, 11, Ui.Fail);
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, 3, 8, 0);
            line.Children.Add(mark);
            line.Children.Add(new TextBlock { Text = problem, FontFamily = Ui.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Ink });
            list.Children.Add(line);
        }
        panel.Children.Add(new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), MaxHeight = 420,
            Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        });
        panel.Children.Add(Ui.Words($"Fix them in {found.Folder}, and open the launcher again. autotest validate lists them too.", 12, Ui.Muted));
        ((FrameworkElement)panel.Children[^1]).Margin = new Thickness(0, 10, 0, 0);
        return panel;
    }

    // ===== How it runs, and Start =====

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

    private void Changed()
    {
        var count = _suite?.Ticked.Count ?? 0;
        var all = _suite is not null && count == _suite.Suite.Cases.Count;
        var content = (StackPanel)_start.Content;
        ((TextBlock)content.Children[0]).Text = NeedsElevating(_suite?.Suite) ? Icons.Admin : Icons.Play;
        ((TextBlock)content.Children[1]).Text = all ? "Start all" : $"Start {count}";
        _start.ToolTip = NeedsElevating(_suite?.Suite) ? "Starts again as administrator, which this suite needs, then runs" : null;
        _start.IsEnabled = count > 0 && _suite?.Ready == true;
    }

    private static bool NeedsElevating(Suite? suite) => suite?.App.RunAsAdmin == true && !Elevation.IsAdministrator;

    private Options HowItRuns(IReadOnlyList<Case> cases) => _options with
    {
        Mode = Modes[Array.FindIndex(_modes, toggle => toggle.IsChecked == true) is var index and >= 0 ? index : 0].Mode,
        Speed = (Speed)Math.Max(0, Array.FindIndex(_speeds, toggle => toggle.IsChecked == true)),
        Cases = string.Join(",", cases.Select(item => item.Number)),
    };

    private void Start()
    {
        if (_suite is not { Ready: true } pane || pane.Ticked is not [_, ..] cases)
            return;
        var options = HowItRuns(cases);
        if (NeedsElevating(pane.Suite))
        {
            Elevate(["launch", pane.Suite.Folder, .. (options with { Start = true }).Arguments()]);
            return;
        }
        Picked = new LauncherChoice(pane.Suite, cases, options);
        Close();
    }

    private void Building(BuildRequest request)
    {
        if (NeedsElevating(request.Suite))
        {
            string[] which = request.AddTo is { } addTo ? ["--add-to", addTo.Id]
                : request.After is { } after ? ["--name", request.Name, "--after", after.Id]
                : ["--name", request.Name];
            Elevate(["record", request.Suite.Folder, .. which, .. (_options with { Cases = null }).Arguments()]);
            return;
        }
        Build = request;
        Close();
    }

    private void Elevate(IEnumerable<string> args)
    {
        if (Elevation.Restart(args))
        {
            Restarted = true;
            Close();
        }
        else
            MessageBox.Show(this, "This suite drives an app that runs as administrator, and starting as administrator was declined.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>The launcher's suites, found near the runner and loaded, for when it opens again after a run.</summary>
    public static IReadOnlyList<FoundSuite> Reload(IReadOnlyList<FoundSuite> suites, string folder) =>
        [.. suites.Select(found => string.Equals(found.Folder, folder, StringComparison.OrdinalIgnoreCase) ? FoundSuite.Load(found.Folder) : found)];
}
