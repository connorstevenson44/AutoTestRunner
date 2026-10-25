using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Walkthrough.Running;

namespace Walkthrough.Cli;

/// <summary>
/// Everything the run has done, newest at the bottom: each case with its steps, the commands it ran with what they
/// printed as they printed it, and its checkpoints. A case that passed folds away when it ends; a click on a case or
/// command opens and closes it. It keeps the newest in view unless you scroll up, and a button brings you back.
/// </summary>
internal sealed class Feed : Grid
{
    // Long outputs keep their last lines here; all of it is in run.log.
    private const int MaxLines = 1500;

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _items = new() { Margin = new Thickness(0, 0, 4, 8) };
    private readonly Button _latest;
    private readonly string _before;
    private Group? _group;
    private CommandRow? _command;
    private bool _follow = true;

    /// <param name="before">The name of the group for what happens before the first case.</param>
    public Feed(string before = "Getting ready")
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

    public void Step(Doing kind, string text) => Body.Children.Add(Row(Icons.Of(kind), text, Ui.Ink));

    /// <summary>A wait that took long enough to notice, once it is over.</summary>
    public void Waited(string what, TimeSpan took) => Body.Children.Add(Row(Icons.Wait, $"{what} · {Ui.Took(took)}", Ui.Faint, small: true));

    public void Note(string text) => Body.Children.Add(Row(Icons.Note, text, Ui.Hold));

    public void CommandStarted(string line, string? script)
    {
        _command = new CommandRow(line, script);
        Body.Children.Add(_command);
    }

    public void CommandOutput(string line, bool error) => _command?.Add(line, error);

    public void CommandFinished(int exitCode, TimeSpan took)
    {
        _command?.Finish(exitCode, took);
        _command = null;
    }

    public void Checkpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools, Action<ToolButton> open) =>
        Body.Children.Add(new CheckpointCard(title, checks, look, tools, open));

    // Steps before the first case, such as cleaning up first, go in a group of their own.
    private StackPanel Body => (_group ??= AddGroup()).Body;

    private Group AddGroup()
    {
        var group = new Group(null, _before);
        Add(group);
        return group;
    }

    private void Add(UIElement item) => _items.Children.Add(item);

    private static Grid Row(string glyph, string text, Brush color, bool small = false)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Glyph(glyph, small ? 12 : 14, small ? Ui.Faint : Ui.Muted);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(2, 2, 0, 0);
        var words = Ui.Words(text, small ? 11.5 : 12.5, color);
        var time = Ui.Words(DateTime.Now.ToString("HH:mm:ss"), 10.5, Ui.Faint, wrap: false);
        time.VerticalAlignment = VerticalAlignment.Top;
        time.Margin = new Thickness(8, 2, 0, 0);
        Grid.SetColumn(words, 1);
        Grid.SetColumn(time, 2);
        row.Children.Add(icon);
        row.Children.Add(words);
        row.Children.Add(time);
        return row;
    }

    /// <summary>A case: a header with its number, name and how it went, over its steps.</summary>
    private sealed class Group : StackPanel
    {
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

        public void Finish(CaseResult result)
        {
            var color = Ui.Of(result.Status);
            _badge.Background = color;
            _badge.Child = Ui.IconText(Icons.Of(result.Status), $"{result.Number}", Brushes.White, 11, FontWeights.Bold);
            _badge.ToolTip = result.Status.ToString();
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
        private readonly TextBlock _echo, _output, _clock, _chevron;
        private readonly ScrollViewer _scroll;
        private readonly DispatcherTimer _timer;
        private readonly DateTime _started = DateTime.Now;
        private readonly System.Text.StringBuilder _text = new();
        private int _lines;

        public CommandRow(string line, string? script)
        {
            Margin = new Thickness(0, 3, 0, 5);
            var header = new Grid { Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Ui.Glyph(Icons.Console, 14, Ui.Accent);
            icon.Margin = new Thickness(2, 0, 0, 0);
            var words = new TextBlock { Text = line, FontFamily = Ui.Mono, FontSize = 11.5, Foreground = Ui.Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = line };
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
            var copy = Ui.IconButton(Icons.Copy, "Copy the command and what it printed", () =>
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
            copy.Foreground = Ui.TerminalScript;
            copy.HorizontalAlignment = HorizontalAlignment.Right;
            copy.VerticalAlignment = VerticalAlignment.Top;
            copy.Margin = new Thickness(0, 2, 14, 0);
            var inside = new Grid();
            inside.Children.Add(_scroll);
            inside.Children.Add(copy);
            _terminal = new Border { Background = Ui.Terminal, CornerRadius = new CornerRadius(6), Margin = new Thickness(26, 4, 0, 0), Child = inside };
            Children.Add(_terminal);
            header.MouseLeftButtonUp += (_, _) => Open(_terminal.Visibility != Visibility.Visible);

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

    /// <summary>A checkpoint: what it checked, each with a tick or a cross, what to look at, and buttons that open things.</summary>
    private sealed class CheckpointCard : Border
    {
        public CheckpointCard(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools, Action<ToolButton> open)
        {
            var failed = checks.Any(check => !check.Passed);
            BorderBrush = failed ? Ui.Fail : checks.Count > 0 ? Ui.Pass : Ui.Line;
            BorderThickness = new Thickness(3, 0, 0, 0);
            Background = failed ? Ui.FailSoft : Ui.Surface;
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(10, 8, 10, 6);
            Margin = new Thickness(0, 4, 0, 6);

            var stack = new StackPanel();
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            heading.Children.Add(Ui.Glyph(Icons.CheckList, 15, failed ? Ui.Fail : Ui.Pass));
            var words = Ui.Words(title, 13, Ui.Ink, FontWeights.SemiBold);
            words.Margin = new Thickness(8, 0, 0, 0);
            heading.Children.Add(words);
            stack.Children.Add(heading);

            foreach (var check in checks)
            {
                stack.Children.Add(Line(check.Passed ? Icons.Tick : Icons.Cross, check.Label, check.Passed ? Ui.Pass : Ui.Fail));
                if (!check.Passed && check.Detail is { } detail)
                {
                    var why = Ui.Words(detail, 11.5, Ui.Fail);
                    why.Margin = new Thickness(22, 0, 0, 3);
                    stack.Children.Add(why);
                }
            }
            foreach (var item in look)
                stack.Children.Add(Line(Icons.Look, item, Ui.Accent));
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

        private static Grid Line(string glyph, string text, Brush color)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
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
