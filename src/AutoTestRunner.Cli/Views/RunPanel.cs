using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// The panel beside the app in a run you watch. From the top: the case and a bar of every case, coloured as each one
/// ends; the controls, as icons, with their keys under them; what is happening now, with the command it is about to
/// run and what it is waiting for; a question, when the run needs you; then everything it has done, with what each
/// command printed as it printed it.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The key hook and the timer are disposed when the window closes.")]
internal sealed partial class RunPanel : Window, IRunView
{
    private const double PanelWidth = 460;

    private readonly RunControl _control;
    private readonly string _resultsFolder;
    private readonly bool _canRunAgain;
    private readonly List<CaseResult> _ended = [];

    private readonly Border[] _segments;
    private readonly TextBlock _caseCount, _caseName;
    private readonly Border _state;
    private readonly Button _main, _next;
    private readonly ToggleButton _checks;
    private readonly ToggleButton[] _speeds;
    private readonly TextBlock _nowIcon, _nowLabel, _nowText, _waitText, _waitTook;
    private readonly Border _nowCommand, _countdown;
    private readonly TextBlock _nowCommandText;
    private readonly DockPanel _waitRow;
    private readonly ContentControl _question;
    private readonly ActivityFeed _feed;
    private readonly DispatcherTimer _waitTimer;
    // What a manual run outlines for the person: one window that moves, which the mouse goes through.
    private readonly OutlineWindow _outline = new();

    private GlobalKeyHook? _hooks;
    private readonly Suite _suite;
    private Runner? _runner;
    private FlaUiDriver? _driver;
    private volatile bool _asking, _youClicked, _typing, _over;
    // The step on show, and what a manual run is waiting for the person to do, while it waits.
    private Activity? _step, _yourTurn;
    private Timer? _typingPoll;
    private nint _hwnd;
    private int _caseIndex;
    // Whether a step or check of the case running has failed, which a checkpoint's "Your turn" shows in red.
    private bool _caseFailed;

    // What the run is waiting for, a command running too, and since when.
    private Activity? _wait;
    private DateTime _waitStarted;

    // The question on show, and the answer that closing it gives. The keys' hook reads whether there is one, from its
    // own thread.
    private volatile TaskCompletionSource<int>? _answer;
    private int _closeAnswer;
    private volatile bool _askingToStop;

    public RunPanel(Suite suite, IReadOnlyList<Case> cases, RunControl control, string resultsFolder, bool canRunAgain)
    {
        (_suite, _control, _resultsFolder, _canRunAgain) = (suite, control, resultsFolder, canRunAgain);
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = suite.Name;
        Width = PanelWidth;
        Topmost = true;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Background = Brushes.White;
        FontFamily = Ui.Text;
        Foreground = Ui.Ink;

        var root = new Grid { Margin = new Thickness(14, 12, 10, 0) };
        for (var row = 0; row < 6; row++)
            root.RowDefinitions.Add(new RowDefinition { Height = row == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // ===== The case, and a bar of all of them =====
        var header = new Grid { Margin = new Thickness(0, 0, 4, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var names = new StackPanel();
        _caseCount = Ui.Words("", 11, Ui.Muted, FontWeights.SemiBold, wrap: false);
        _caseName = Ui.Words("Getting ready", 16, Ui.Ink, FontWeights.Bold);
        names.Children.Add(_caseCount);
        names.Children.Add(_caseName);
        _state = Ui.Pill(new TextBlock(), Ui.AccentSoft, 11, new Thickness(9, 3, 10, 3));
        _state.VerticalAlignment = VerticalAlignment.Top;
        _state.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_state, 1);
        header.Children.Add(names);
        header.Children.Add(_state);

        var bar = new Grid { Height = 6, Margin = new Thickness(0, 10, 4, 0) };
        _segments = new Border[cases.Count];
        for (var index = 0; index < cases.Count; index++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            _segments[index] = new Border
            {
                Background = Ui.Pending, CornerRadius = new CornerRadius(3), Margin = new Thickness(index == 0 ? 0 : 1.5, 0, index == cases.Count - 1 ? 0 : 1.5, 0),
                ToolTip = $"{cases[index].Number}. {cases[index].Name}",
            };
            Grid.SetColumn(_segments[index], index);
            bar.Children.Add(_segments[index]);
        }
        var top = new StackPanel();
        top.Children.Add(header);
        top.Children.Add(bar);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // ===== The controls =====
        var controls = new Grid { Margin = new Thickness(-6, 10, 0, 4) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var run = new StackPanel { Orientation = Orientation.Horizontal };
        _main = Ui.IconButton(Icons.Pause, "Pause (Space)", () => _control.PauseOrCarryOn(), 20);
        _next = Ui.IconButton(Icons.Next, "Do the next step, then wait (Enter)", () => _control.NextStep(), 20);
        var stop = Ui.IconButton(Icons.Stop, "Stop the test (Esc)", RequestStop, 20);
        stop.Foreground = Ui.Fail;
        foreach (var button in new[] { _main, _next, stop })
            button.Padding = new Thickness(7, 5, 7, 3);
        run.Children.Add(Captioned(_main, "Space"));
        run.Children.Add(Captioned(_next, "Enter"));
        run.Children.Add(Captioned(stop, "Esc"));
        controls.Children.Add(run);

        var settings = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        _speeds =
        [
            Ui.Toggle(Icons.SpeedSlow, "Slow", 16),
            Ui.Toggle(Icons.SpeedModest, "Modest speed", 16),
            Ui.Toggle(Icons.SpeedFast, "Fast", 16),
            Ui.Toggle(Icons.SpeedManual, "Manual: you click and type, and each step waits for you", 16),
        ];
        for (var index = 0; index < _speeds.Length; index++)
        {
            var speed = (Speed)index;
            _speeds[index].IsChecked = control.Speed == speed;
            _speeds[index].Click += (_, _) => SetSpeed(speed);
            settings.Children.Add(_speeds[index]);
        }
        settings.Children.Add(new Border { Width = 1, Background = Ui.Line, Margin = new Thickness(4, 6, 4, 6) });
        _checks = Ui.Toggle(Icons.CheckList, "Wait for me at each checkpoint", 16);
        _checks.IsChecked = control.StopAtCheckpoints;
        _checks.Click += (_, _) => _control.StopAtCheckpoints = _checks.IsChecked == true;
        settings.Children.Add(_checks);
        settings.Children.Add(Ui.IconButton(Icons.Note, "Note a problem, with a screenshot", NoteProblem, 16));
        settings.Children.Add(Ui.IconButton(Icons.Folder, "Open the results folder", () => Open("folder", _resultsFolder), 16));
        foreach (var setting in settings.Children.OfType<ButtonBase>())
            setting.Padding = new Thickness(6, 5, 6, 5);
        Grid.SetColumn(settings, 2);
        controls.Children.Add(settings);
        Grid.SetRow(controls, 1);
        root.Children.Add(controls);

        // ===== Now =====
        var now = new Grid();
        now.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        now.ColumnDefinitions.Add(new ColumnDefinition());
        _nowIcon = Ui.Glyph(Icons.Info, 18, Ui.Accent);
        _nowIcon.HorizontalAlignment = HorizontalAlignment.Center;
        var circle = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Ui.AccentSoft, Child = _nowIcon, VerticalAlignment = VerticalAlignment.Top };
        var said = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        _nowLabel = Ui.Heading("Now");
        _nowText = Ui.Words("", 14.5, Ui.Ink);
        _nowText.MinHeight = 40;
        _nowCommandText = new TextBlock { FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.TerminalText, TextWrapping = TextWrapping.Wrap, MaxHeight = 92, TextTrimming = TextTrimming.CharacterEllipsis };
        _nowCommand = new Border { Background = Ui.Terminal, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 6, 0, 0), Child = _nowCommandText, Visibility = Visibility.Collapsed };
        _waitText = Ui.Words("", 11.5, Ui.Muted);
        _waitText.Margin = new Thickness(6, 0, 0, 0);
        _waitTook = Ui.Words("", 11, Ui.Muted, wrap: false);
        _waitTook.VerticalAlignment = VerticalAlignment.Top;
        _waitTook.Margin = new Thickness(8, 2, 0, 0);
        _waitRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var turning = Ui.Spinner(11, Ui.Muted);
        turning.VerticalAlignment = VerticalAlignment.Top;
        turning.Margin = new Thickness(0, 3, 0, 0);
        DockPanel.SetDock(_waitTook, Dock.Right);
        _waitRow.Children.Add(turning);
        _waitRow.Children.Add(_waitTook);
        _waitRow.Children.Add(_waitText);
        said.Children.Add(_nowLabel);
        said.Children.Add(_nowText);
        said.Children.Add(_nowCommand);
        said.Children.Add(_waitRow);
        Grid.SetColumn(said, 1);
        now.Children.Add(circle);
        now.Children.Add(said);
        _countdown = new Border { Height = 3, Background = Ui.Pass, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 10, 0, 0), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(), Visibility = Visibility.Hidden };
        var nowCard = new StackPanel();
        nowCard.Children.Add(new Border { Background = Ui.Surface, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Child = now, Margin = new Thickness(0, 0, 4, 0) });
        nowCard.Children.Add(_countdown);
        Grid.SetRow(nowCard, 2);
        root.Children.Add(nowCard);

        // ===== A question, when there is one =====
        _question = new ContentControl { Margin = new Thickness(0, 4, 4, 0) };
        Grid.SetRow(_question, 3);
        root.Children.Add(_question);

        // ===== Everything so far =====
        var line = new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 8, 4, 0) };
        Grid.SetRow(line, 4);
        root.Children.Add(line);
        _feed = new ActivityFeed();
        Grid.SetRow(_feed, 5);
        root.Children.Add(_feed);
        Content = root;

        _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _waitTimer.Tick += (_, _) => ShowWaitTime();

        Closing += (_, e) =>
        {
            if (_over || _runner is null || _control.StopRequested)
                return;
            e.Cancel = true;
            RequestStop();
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var area = SystemParameters.WorkArea;
            Left = area.Right - PanelWidth;
            Top = area.Top;
            Height = area.Height;
            _hooks = new GlobalKeyHook(OnKey, OnClick, _control.Person);
        };
        Closed += (_, _) =>
        {
            _outline.Close();
            _hooks?.Dispose();
            _typingPoll?.Dispose();
            _answer?.TrySetResult(_closeAnswer);
        };
        _control.Changed += () => Dispatcher.BeginInvoke(ShowState);
        ShowState();
    }

    /// <summary>True when the person asked to go back to the cases to run more, rather than close.</summary>
    public bool RunAgain { get; private set; }

    public void Attach(Runner runner, FlaUiDriver driver)
    {
        (_runner, _driver) = (runner, driver);
        // Whether the person is typing in a text box, looked at a few times a second rather than from the key hook,
        // which Windows drops if it takes too long. A manual run's keys are the app's anyway, so it doesn't ask the app.
        _typingPoll = new Timer(_ => _typing = !_control.Manual && _youClicked && _driver.IsTyping(), null, 0, 300);
    }

    /// <summary>The run has ended, so closing the panel no longer asks to stop it.</summary>
    public void RunEnded() => OnUi(() =>
    {
        _over = true;
        _main.IsEnabled = _next.IsEnabled = false;
        EndWait();
        _hooks?.Dispose();
        _hooks = null;
    });

    /// <summary>Where the panel is on the screen, in pixels.</summary>
    public ScreenRect? Bounds => _hwnd == 0 ? null : WindowBounds(_hwnd);

    // ===== Keys, buttons and state =====

    // Space, Enter and Esc belong to the run unless the person is typing: in a text box they clicked into themselves,
    // or in one of the runner's own windows, such as a note. In a manual run they belong to the app, as the person uses
    // it, unless the panel has the focus. While the run is stuck, or a question other than whether
    // to stop is showing, they do nothing: the question's buttons answer it. Called on the hook's thread: it only reads,
    // and what a key does happens on the UI thread.
    private bool OnKey(int key)
    {
        if (_answer is not null && !_askingToStop)
            return false;
        var foreground = GetForegroundWindow();
        if (_control.Manual && foreground != _hwnd)
            return false;
        if (foreground != _hwnd)
        {
            if (_control.Waiting == HoldReason.Stuck || _asking)
                return false;
            _ = GetWindowThreadProcessId(foreground, out var processId);
            if (processId == Environment.ProcessId || _typing)
                return false;
        }
        if (GlobalKeyHook.IsSpace(key))
            Dispatcher.BeginInvoke(_control.PauseOrCarryOn);
        else if (GlobalKeyHook.IsEnter(key))
            Dispatcher.BeginInvoke(_control.NextStep);
        else
            Dispatcher.BeginInvoke(RequestStop);
        return true;
    }

    private void OnClick(int x, int y)
    {
        if (Bounds is not { } panel || x < panel.Left || x > panel.Right || y < panel.Top || y > panel.Bottom)
            _youClicked = true;
    }

    // Stuck, stopping is one of the answers; otherwise it asks first, and Esc again says yes.
    private void RequestStop()
    {
        if (_control.StopRequested || _over)
            return;
        if (_control.Waiting == HoldReason.Stuck)
        {
            Answer(2);
            return;
        }
        if (_askingToStop)
        {
            Answer(0);
            return;
        }
        if (_answer is not null)
            return;
        _askingToStop = true;
        Ask(Ui.Fail, Icons.Stop, "Stop the test?", "The app stays as it is, and the results so far are saved. Esc again stops it.",
            [(Icons.Stop, "Stop"), (Icons.Play, "Keep going")], closeAnswer: 1).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            _askingToStop = false;
            if (task.Result != 0 || _control.StopRequested)
                return;
            _control.RequestStop();
            SetState(Icons.Stop, "Stopping", Ui.Fail, Ui.FailSoft);
        }));
    }

    private void NoteProblem()
    {
        if (_runner is not { } runner || _over)
            return;
        _asking = true;
        var text = Ui.Prompt(this, "Note a problem", "What looks wrong? It's saved with this case's results, with a screenshot of the screen now.");
        _asking = false;
        if (string.IsNullOrWhiteSpace(text))
            return;
        _feed.Note(text);
        ThreadPool.QueueUserWorkItem(_ => runner.Note(text));
    }

    private void SetSpeed(Speed speed)
    {
        _control.SetSpeed(speed);
        for (var index = 0; index < _speeds.Length; index++)
            _speeds[index].IsChecked = index == (int)speed;
    }

    private void ShowState()
    {
        if (_control.StopRequested || _over)
            return;
        _checks.IsChecked = _control.StopAtCheckpoints;
        if (_control.Waiting != HoldReason.None)
            HideCountdown();
        switch (_control.Waiting)
        {
            case HoldReason.Pause: SetState(Icons.Pause, "Paused", Ui.Hold, Ui.HoldSoft); break;
            case HoldReason.Step: SetState(Icons.Next, "Your step", Ui.Step, Ui.StepSoft, "Enter does it"); break;
            case HoldReason.Checkpoint:
                SetState(Icons.Look, "Your turn", _caseFailed ? Ui.Fail : Ui.Pass, _caseFailed ? Ui.FailSoft : Ui.PassSoft, "Check the screen, then press Space to continue");
                break;
            case HoldReason.Stuck: SetState(Icons.Warning, "Stuck", Ui.Fail, Ui.FailSoft); break;
            case HoldReason.None when _yourTurn is not null:
                SetState(Icons.SpeedManual, "Your turn", Ui.Step, Ui.StepSoft, "Do it in the app: the run carries on once it's done");
                break;
            default:
                SetState(Icons.Running, _control.StepByStep ? "Doing it" : "Running", Ui.Accent, Ui.AccentSoft);
                _youClicked = false;
                break;
        }
        _nowLabel.Text = _control.Waiting == HoldReason.Step ? "UP NEXT" : _yourTurn is not null ? "YOUR TURN" : "NOW";
        var going = _control.Waiting == HoldReason.None;
        ((TextBlock)_main.Content).Text = going ? Icons.Pause : Icons.Play;
        _main.ToolTip = going ? "Pause (Space)" : _control.StepByStep ? "Run by itself (Space)" : "Carry on (Space)";
        _main.IsEnabled = _next.IsEnabled = _control.Waiting != HoldReason.Stuck;
    }

    private void SetState(string glyph, string text, Brush color, Brush background, string? tip = null)
    {
        _state.Background = background;
        _state.Child = Ui.IconText(glyph, text, color, 12);
        _state.ToolTip = tip;
    }

    // Once the countdown is over or held, the state says what the run is doing again.
    private void HideCountdown()
    {
        if (_countdown.Visibility != Visibility.Visible)
            return;
        ((ScaleTransform)_countdown.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _countdown.Visibility = Visibility.Hidden;
        ShowState();
    }

    // ===== Waits =====

    private void StartWait(Activity what)
    {
        EndWait();
        (_wait, _waitStarted) = (what, DateTime.Now);
        StepLine.Fill(_waitText, what, 11.5, quiet: true);
        _waitTimer.Start();
    }

    // A wait shows once it has gone on long enough to notice, with how long so far.
    private void ShowWaitTime()
    {
        var took = DateTime.Now - _waitStarted;
        if (_wait is null || took < TimeSpan.FromMilliseconds(600))
            return;
        _waitTook.Text = Ui.Took(took);
        _waitRow.Visibility = Visibility.Visible;
    }

    private void EndWait()
    {
        _waitTimer.Stop();
        _waitRow.Visibility = Visibility.Collapsed;
        _wait = null;
    }

    // ===== Helpers =====

    private void OpenTool(ToolButton tool)
    {
        try
        {
            tool.Open();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(this, "That didn't open: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Open(string what, string path) => OpenTool(new ToolButton(path, () => new WindowsMachine().Open(what, path)));

    // A control with its key under it, so the keys are learnt by looking.
    private static StackPanel Captioned(UIElement control, string key)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 2, 0) };
        stack.Children.Add(control);
        var caption = Ui.Words(key, 9.5, Ui.Faint, wrap: false);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(caption);
        return stack;
    }

    // Everything the run asks of the panel waits its turn behind what is already queued, so it shows in order.
    private void OnUi(Action action) => Dispatcher.Invoke(action, DispatcherPriority.Normal);

    private T OnUi<T>(Func<T> function) => Dispatcher.Invoke(function, DispatcherPriority.Normal);

    private static ScreenRect WindowBounds(nint hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}
