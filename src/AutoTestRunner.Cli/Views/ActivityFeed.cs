using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using AutoTestRunner.Running;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Everything the run has done, newest at the bottom: each case with its steps, waits, commands and checkpoints. A case
/// that passed folds away when it ends. It keeps the newest in view unless you scroll up.
/// </summary>
internal sealed class ActivityFeed : Grid
{
    // Long outputs keep their last lines here; all of it is in run.log.
    private const int MaxLines = 1500;

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _items = new() { Margin = new Thickness(0, 0, 4, 8) };
    private readonly Button _latest;
    private readonly string _before;
    private Group? _group;
    private CommandRow? _command, _ran;
    private StepRow? _step;
    // The last step shown, which a failure is about.
    private Activity? _last;
    private bool _follow = true;

    /// <param name="before">The name of the group for what happens before the first case.</param>
    public ActivityFeed(string before = "Getting ready")
    {
        _before = before;
        _scroll = new ScrollViewer { Content = _items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _scroll.ScrollChanged += (_, e) =>
        {
            // Content growing moves the end away; only the person scrolling decides whether to keep up with it.
            if (e.ExtentHeightChange == 0)
                _follow = _scroll.VerticalOffset >= _scroll.ScrollableHeight - 4;
            else if (_follow)
                _scroll.ScrollToEnd();
            _latest!.Visibility = _follow ? Visibility.Collapsed : Visibility.Visible;
        };
        _latest = Ui.IconButton(Icons.Down, "Back to the newest", () =>
        {
            _follow = true;
            _scroll.ScrollToEnd();
        }, 14);
        _latest.Background = Ui.Accent;
        _latest.Foreground = Brushes.White;
        _latest.HorizontalAlignment = HorizontalAlignment.Right;
        _latest.VerticalAlignment = VerticalAlignment.Bottom;
        _latest.Margin = new Thickness(0, 0, 20, 10);
        _latest.Visibility = Visibility.Collapsed;
        Children.Add(_scroll);
        Children.Add(_latest);
    }

    public void CaseStarted(int number, string name) => Add(_group = new Group($"{number}", name));

    public void CaseFinished(CaseResult result)
    {
        if (_group is null)
            return;
        _group.Finish(result);
        _group = null;
    }

    /// <summary>
    /// A step: its line, with the time. A checkpoint's line isn't added, as its card follows, nor a command's, as the
    /// command's own row does.
    /// </summary>
    public void Step(Activity step)
    {
        _last = step;
        if (step.Kind == ActivityKind.Check || step.Command is not null)
        {
            _step = null;
            return;
        }
        Body.Children.Add(_step = new StepRow(step, step.Kind == ActivityKind.Say ? Icons.Info : Icons.Of(step.Kind), Time()));
    }

    /// <summary>A wait step that is over: what it waited for, what it saw, and how long it took.</summary>
    public void Waited(Activity step, TimeSpan took)
    {
        _last = step;
        Body.Children.Add(_step = new StepRow(step, Icons.Wait, Ui.Took(took), quiet: true));
    }

    /// <summary>The last step failed, though the case carries on: it turns red, with what it wanted and what it found.</summary>
    public void StepFailed(StepFailure failure)
    {
        var group = _group ??= AddGroup();
        group.Failed(_last, failure);
        if (_last?.Command is not null && _ran is { } ran)
        {
            // A command's row shows its exit code in red already.
            ran.ToolTip = new ToolTip { Content = Ui.Words(failure.Message, 12, Ui.Ink), MaxWidth = 380 };
            return;
        }
        if (_step is null || !group.Body.Children.Contains(_step))
            group.Body.Children.Add(_step = new StepRow(Activity.Words(failure.Message), Icons.Cross, Time()));
        _step.Fail(failure);
    }

    public void Note(string text) => Body.Children.Add(new StepRow(Activity.Words(text), Icons.Note, Time()));

    public void CommandStarted(string line, string? script)
    {
        _ran = _command = new CommandRow(line, script, _last?.Say);
        Body.Children.Add(_command);
    }

    public void CommandOutput(string line, bool error) => _command?.Add(line, error);

    public void CommandFinished(int exitCode, TimeSpan took)
    {
        _command?.Finish(exitCode, took);
        _command = null;
    }

    /// <summary>Adds a checkpoint's card, red if a check failed or a step since the last checkpoint did.</summary>
    public bool Checkpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools, Action<ToolButton> open)
    {
        var group = _group ??= AddGroup();
        var card = new CheckpointCard(title, group.TakeFailures(), checks, look, tools, open);
        group.Body.Children.Add(card);
        return card.Failed;
    }

    // Steps before the first case, such as cleaning up first, go in a group of their own.
    private StackPanel Body => (_group ??= AddGroup()).Body;

    private Group AddGroup()
    {
        var group = new Group(null, _before);
        Add(group);
        return group;
    }

    private void Add(UIElement item) => _items.Children.Add(item);

    private static string Time() => DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>What a step wanted beside what it found, as the checkpoint's table has them, for a step that failed.</summary>
    private static Grid Compare(string expected, string actual)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Put(grid, Ui.Heading("Expected"), 0, 0);
        Put(grid, Ui.Heading("Actual"), 0, 1);
        Put(grid, Value(expected, Ui.Ink), 1, 0);
        Put(grid, Value(actual, Ui.Fail), 1, 1);
        return grid;
    }

    private static void Put(Grid grid, UIElement item, int row, int column, int span = 1)
    {
        Grid.SetRow(item, row);
        Grid.SetColumn(item, column);
        Grid.SetColumnSpan(item, span);
        grid.Children.Add(item);
    }

    // A value in a table: a few lines of it, the rest when pointed at.
    private static TextBlock Value(string text, Brush color, string? full = null)
    {
        var value = Ui.Words(text, 12, color);
        value.MaxHeight = 50;
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        value.Margin = new Thickness(0, 0, 8, 0);
        if (full is not null || text.Length > 40)
            value.ToolTip = new ToolTip { Content = new TextBlock { Text = full ?? text, FontFamily = Ui.Mono, FontSize = 11, TextWrapping = TextWrapping.Wrap }, MaxWidth = 520 };
        return value;
    }

    /// <summary>
    /// A step's row: an icon for what it does, its line, what it saw under it, and on the right the time, or how long a
    /// wait took. Pointing at it shows the script's own words for it. A step that failed turns red.
    /// </summary>
    private sealed class StepRow : Grid
    {
        private readonly TextBlock _icon;
        private readonly StackPanel _words;

        public StepRow(Activity step, string glyph, string right, bool quiet = false)
        {
            Margin = new Thickness(0, 1, 0, 1);
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _icon = Ui.Glyph(glyph, 14, quiet ? Ui.Faint : Ui.Muted);
            _icon.VerticalAlignment = VerticalAlignment.Top;
            _icon.Margin = new Thickness(2, 4, 0, 0);
            _words = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            _words.Children.Add(StepLine.Of(step, 12.5, quiet));
            if (step.Detail is { } detail)
            {
                var saw = Value(detail, Ui.Muted);
                saw.FontSize = 11.5;
                saw.FontFamily = Ui.Mono;
                saw.Margin = new Thickness(0, 2, 0, 0);
                _words.Children.Add(saw);
            }
            var time = Ui.Words(right, 10.5, Ui.Faint, wrap: false);
            time.VerticalAlignment = VerticalAlignment.Top;
            time.Margin = new Thickness(8, 5, 0, 0);
            SetColumn(_words, 1);
            SetColumn(time, 2);
            Children.Add(_icon);
            Children.Add(_words);
            Children.Add(time);
            if (step.Say is { } say)
            {
                ToolTip = new ToolTip { Content = Ui.Words(say, 12, Ui.Ink), MaxWidth = 380 };
                ToolTipService.SetInitialShowDelay(this, 400);
            }
        }

        public void Fail(StepFailure failure)
        {
            _icon.Text = Icons.Cross;
            _icon.Foreground = Ui.Fail;
            Background = Ui.FailSoft;
            _words.Children.Add(failure.Expected is { } expected
                ? Compare(expected, failure.Actual ?? "")
                : Ui.Words(failure.Message, 11.5, Ui.Fail));
            ToolTip = new ToolTip { Content = Ui.Words(failure.Message, 12, Ui.Ink), MaxWidth = 380 };
            ToolTipService.SetInitialShowDelay(this, 150);
            ToolTipService.SetShowDuration(this, 60_000);
        }
    }

    /// <summary>A case: a header with its number, name and how it went, over its steps.</summary>
    private sealed class Group : StackPanel
    {
        private readonly List<(Activity? Step, StepFailure Failure)> _failures = [];
        private readonly Border _badge;
        private readonly TextBlock _chevron, _took;

        public Group(string? number, string name)
        {
            Margin = new Thickness(0, 6, 0, 2);
            var header = new Grid { Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _chevron = Ui.Glyph(Icons.Expand, 10, Ui.Muted);
            _badge = Ui.Pill(number is null ? Ui.Glyph(Icons.Clean, 11, Ui.Accent) : Ui.Words(number, 11, Ui.Accent, FontWeights.Bold, wrap: false), Ui.AccentSoft, 8, new Thickness(7, 1, 7, 1));
            _badge.MinWidth = 26;
            var title = Ui.Words(name, 13, Ui.Ink, FontWeights.SemiBold);
            title.Margin = new Thickness(8, 0, 0, 0);
            _took = Ui.Words("", 10.5, Ui.Faint, wrap: false);
            Grid.SetColumn(_badge, 1);
            Grid.SetColumn(title, 2);
            Grid.SetColumn(_took, 3);
            header.Children.Add(_chevron);
            header.Children.Add(_badge);
            header.Children.Add(title);
            header.Children.Add(_took);
            Body = new StackPanel { Margin = new Thickness(18, 4, 0, 0) };
            header.MouseLeftButtonUp += (_, _) => Fold(Body.Visibility == Visibility.Visible);
            Children.Add(header);
            Children.Add(Body);
            Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 6, 0, 0) });
        }

        public StackPanel Body { get; }

        public void Failed(Activity? step, StepFailure failure) => _failures.Add((step, failure));

        /// <summary>The steps that failed since the last checkpoint, for the next one to show.</summary>
        public List<(Activity? Step, StepFailure Failure)> TakeFailures()
        {
            var failures = _failures.ToList();
            _failures.Clear();
            return failures;
        }

        public void Finish(CaseResult result)
        {
            var color = Ui.Of(result.Status);
            _badge.Background = color;
            _badge.Child = Ui.IconText(Icons.Of(result.Status), $"{result.Number}", Brushes.White, 11, FontWeights.Bold);
            _badge.ToolTip = result.Failures.Count == 0 ? result.Status.ToString() : $"{result.Status}:\n{string.Join("\n", result.Failures)}";
            _took.Text = Ui.Took(result.Duration);
            if (result.Status == CaseStatus.Passed)
                Fold(true);
        }

        private void Fold(bool fold)
        {
            Body.Visibility = fold ? Visibility.Collapsed : Visibility.Visible;
            _chevron.Text = fold ? Icons.Collapse : Icons.Expand;
        }
    }

    /// <summary>
    /// A command the run started: its command line, a turning icon and the time while it runs, then a tick or its exit
    /// code; below, what it printed, as a terminal shows it, with errors in red.
    /// </summary>
    private sealed class CommandRow : StackPanel
    {
        private readonly Border _status, _terminal;
        private readonly Button _copy;
        private readonly TextBlock _echo, _output, _clock, _chevron;
        private readonly ScrollViewer _scroll;
        private readonly DispatcherTimer _timer;
        private readonly DateTime _started = DateTime.Now;
        private readonly System.Text.StringBuilder _text = new();
        private int _lines;

        public CommandRow(string line, string? script, string? say)
        {
            Margin = new Thickness(0, 3, 0, 5);
            var header = new Grid { Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Ui.Glyph(Icons.Console, 14, Ui.Accent);
            icon.Margin = new Thickness(2, 0, 0, 0);
            var words = new TextBlock { Text = line, FontFamily = Ui.Mono, FontSize = 11.5, Foreground = Ui.Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            _clock = Ui.Words("", 10.5, Ui.Muted, wrap: false);
            _clock.Margin = new Thickness(5, 0, 0, 0);
            var running = new StackPanel { Orientation = Orientation.Horizontal };
            running.Children.Add(Ui.Spinner(11, Ui.Accent));
            running.Children.Add(_clock);
            _status = Ui.Pill(running, Ui.AccentSoft, 8, new Thickness(6, 1, 6, 1));
            _status.Margin = new Thickness(8, 0, 0, 0);
            _chevron = Ui.Glyph(Icons.Expand, 9, Ui.Muted);
            _chevron.Margin = new Thickness(6, 0, 0, 0);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(_status);
            right.Children.Add(_chevron);
            Grid.SetColumn(words, 1);
            Grid.SetColumn(right, 2);
            header.Children.Add(icon);
            header.Children.Add(words);
            header.Children.Add(right);
            Children.Add(header);

            _echo = Terminal();
            _output = Terminal();
            Echo(_echo, "> " + line, Ui.TerminalCommand);
            foreach (var scriptLine in (script ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Echo(_echo, "  " + scriptLine, Ui.TerminalScript);
            var lines = new StackPanel();
            lines.Children.Add(_echo);
            lines.Children.Add(_output);
            _scroll = new ScrollViewer { Content = lines, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 8, 10, 8) };
            _copy = Ui.IconButton(Icons.Copy, "Copy the command and what it printed", () =>
            {
                try
                {
                    Clipboard.SetText(Text);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Another program has the clipboard open; a second click will do.
                }
            }, 12);
            _copy.Foreground = Ui.Muted;
            _copy.Padding = new Thickness(4, 2, 4, 2);
            _copy.Margin = new Thickness(4, 0, 0, 0);
            right.Children.Insert(1, _copy);
            _terminal = new Border { Background = Ui.Terminal, CornerRadius = new CornerRadius(6), Margin = new Thickness(26, 4, 0, 0), Child = _scroll };
            Children.Add(_terminal);
            header.MouseLeftButtonUp += (_, _) => Open(_terminal.Visibility != Visibility.Visible);
            if (say is not null)
                header.ToolTip = new ToolTip { Content = Ui.Words(say, 12, Ui.Ink), MaxWidth = 380 };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, _) => _clock.Text = Ui.Took(DateTime.Now - _started);
            _timer.Start();
        }

        public string Text => _text.ToString();

        public void Add(string line, bool error)
        {
            if (++_lines > MaxLines)
            {
                // The oldest line and the break after it.
                _output.Inlines.Remove(_output.Inlines.FirstInline);
                _output.Inlines.Remove(_output.Inlines.FirstInline);
            }
            Echo(_output, line, error ? Ui.TerminalError : Ui.TerminalText);
            _scroll.ScrollToEnd();
        }

        public void Finish(int exitCode, TimeSpan took)
        {
            _timer.Stop();
            var passed = exitCode == 0;
            _status.Background = passed ? Ui.PassSoft : Ui.FailSoft;
            _status.Child = passed
                ? Ui.IconText(Icons.Tick, Ui.Took(took), Ui.Pass, 10.5)
                : Ui.IconText(Icons.Cross, $"{exitCode} · {Ui.Took(took)}", Ui.Fail, 10.5);
            _status.ToolTip = $"Exit code {exitCode}";
            Echo(_output, $"exit {exitCode} · {Ui.Took(took)}", passed ? Ui.Brush("#89D185") : Ui.TerminalError);
            // Nothing printed, nothing to look at, unless it failed.
            if (passed && _lines == 0)
                Open(false);
        }

        private static TextBlock Terminal() => new() { FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.TerminalText, TextWrapping = TextWrapping.Wrap };

        private void Echo(TextBlock block, string line, Brush color)
        {
            _text.AppendLine(line);
            if (block.Inlines.Count > 0)
                block.Inlines.Add(new LineBreak());
            block.Inlines.Add(new Run(line) { Foreground = color });
        }

        private void Open(bool open)
        {
            _terminal.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _chevron.Text = open ? Icons.Expand : Icons.Collapse;
        }
    }

    /// <summary>
    /// A checkpoint: a table of what it checked, with what each expected beside what it found, and the steps before it
    /// that failed; then what to look at yourself, and buttons that open things. Red if anything failed.
    /// </summary>
    private sealed class CheckpointCard : Border
    {
        private readonly Grid _table = new();

        public CheckpointCard(string title, IReadOnlyList<(Activity? Step, StepFailure Failure)> stepFailures, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look,
            IReadOnlyList<ToolButton> tools, Action<ToolButton> open)
        {
            var failed = Failed = stepFailures.Count > 0 || checks.Any(check => !check.Passed);
            BorderBrush = failed ? Ui.Fail : checks.Count > 0 ? Ui.Pass : Ui.Line;
            BorderThickness = new Thickness(3, 0, 0, 0);
            Background = failed ? Ui.FailSoft : Ui.Surface;
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(10, 8, 10, 6);
            Margin = new Thickness(0, 4, 0, 6);

            var stack = new StackPanel();
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Ui.Glyph(Icons.CheckList, 15, failed ? Ui.Fail : Ui.Pass);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 1, 0, 0);
            var words = Ui.Words(title, 13, Ui.Ink, FontWeights.SemiBold);
            words.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(words, 1);
            heading.Children.Add(icon);
            heading.Children.Add(words);
            var total = checks.Count + stepFailures.Count;
            if (total > 0)
            {
                var passed = checks.Count(check => check.Passed);
                var count = Ui.Pill(Ui.IconText(failed ? Icons.Cross : Icons.Tick, $"{passed} / {total}", failed ? Ui.Fail : Ui.Pass, 11), Brushes.White, 8, new Thickness(7, 1, 8, 1));
                count.VerticalAlignment = VerticalAlignment.Top;
                count.ToolTip = $"{passed} of {total} passed";
                Grid.SetColumn(count, 2);
                heading.Children.Add(count);
            }
            stack.Children.Add(heading);

            if (total > 0)
            {
                _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
                _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Row(null, Ui.Heading("Check"), Ui.Heading("Expected"), Ui.Heading("Actual"));
                foreach (var (step, failure) in stepFailures)
                    Row(false, StepCell(step, failure), Value(failure.Expected ?? "", Ui.Ink), Value(failure.Actual ?? failure.Message, Ui.Fail, failure.Message));
                foreach (var check in checks)
                    Row(check.Passed, SubjectCell(check), ExpectedCell(check), Value(check.Actual, check.Passed ? Ui.Ink : Ui.Fail, check.Full));
                stack.Children.Add(_table);
            }

            if (look.Count > 0)
            {
                var manual = new StackPanel { Margin = new Thickness(0, total > 0 ? 8 : 0, 0, 0) };
                foreach (var item in look)
                    manual.Children.Add(Line(Icons.Look, item, Ui.Accent));
                stack.Children.Add(manual);
            }
            if (tools.Count > 0)
            {
                var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var tool in tools)
                    buttons.Children.Add(Ui.LabelledButton(tool.Kind switch
                    {
                        "openFolder" => Icons.Folder,
                        "openFile" => Icons.Document,
                        "console" => Icons.Console,
                        _ => Icons.App,
                    }, tool.Label, () => open(tool)));
                stack.Children.Add(buttons);
            }
            Child = stack;
        }

        public bool Failed { get; }

        // A row of the table, with a line under it; passed is null for the headings.
        private void Row(bool? passed, UIElement what, UIElement expected, UIElement actual)
        {
            var row = _table.RowDefinitions.Count;
            _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
            Put(_table, line, row, 0, 4);
            if (passed is { } ok)
            {
                var mark = Ui.Glyph(ok ? Icons.Tick : Icons.Cross, 12, ok ? Ui.Pass : Ui.Fail);
                mark.VerticalAlignment = VerticalAlignment.Top;
                mark.Margin = new Thickness(0, 5, 0, 0);
                Put(_table, mark, row, 0);
            }
            foreach (var (cell, column) in new[] { (what, 1), (expected, 2), (actual, 3) })
            {
                if (cell is FrameworkElement element)
                    element.Margin = new Thickness(0, passed is null ? 0 : 4, 8, passed is null ? 0 : 4);
                Put(_table, cell, row, column);
            }
        }

        // What a check looked at: an icon for what kind of thing, its name, and a detail such as the setting.
        private static StackPanel SubjectCell(CheckResult check)
        {
            var cell = new StackPanel();
            var name = new TextBlock { FontSize = 12, Foreground = Ui.Ink, TextWrapping = TextWrapping.Wrap };
            name.Inlines.Add(Ui.GlyphInline(Icons.Of(check.Subject.Kind), 11.5, Ui.Muted));
            name.Inlines.Add(new Run("  " + (Labelled(check) ? check.Label : check.Subject.Name)) { FontWeight = FontWeights.SemiBold });
            cell.Children.Add(name);
            if (!Labelled(check) && check.Subject.Detail is { } detail)
                cell.Children.Add(new TextBlock { Text = detail, FontSize = 11, FontFamily = Ui.Mono, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap });
            var tip = string.Join("\n", new[] { check.Subject.Tip, Labelled(check) ? Checks.Label(check.Subject, check.How, check.Expected) : null }.OfType<string>());
            if (tip.Length > 0)
                cell.ToolTip = new ToolTip { Content = new TextBlock { Text = tip, FontFamily = Ui.Mono, FontSize = 11, TextWrapping = TextWrapping.Wrap }, MaxWidth = 460 };
            return cell;
        }

        // A label the script gave the check names it in the table, in place of what it looks at.
        private static bool Labelled(CheckResult check) => check.Label != Checks.Label(check.Subject, check.How, check.Expected);

        // What it expected, with how it compares, such as contains, quiet in front.
        private static TextBlock ExpectedCell(CheckResult check)
        {
            var cell = Value(check.Expected, Ui.Ink);
            if (!Checks.IsState(check.How))
            {
                cell.Text = "";
                cell.Inlines.Add(new Run(check.How + " ") { Foreground = Ui.Muted, FontSize = 11 });
                cell.Inlines.Add(new Run(check.Expected));
            }
            return cell;
        }

        // A step that failed before the checkpoint, by its line.
        private static UIElement StepCell(Activity? step, StepFailure failure) =>
            step is null ? Ui.Words(failure.Message, 12, Ui.Fail) : StepLine.Of(step, 12);

        private static Grid Line(string glyph, string text, Brush color)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Ui.Glyph(glyph, 12, color);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            var words = Ui.Words(text, 12.5, Ui.Ink);
            Grid.SetColumn(words, 1);
            row.Children.Add(icon);
            row.Children.Add(words);
            return row;
        }
    }
}
