using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Walkthrough.Driving;
using Walkthrough.FlaUI;
using Walkthrough.Running;
using Walkthrough.Scripting;

namespace Walkthrough.Cli;

/// <summary>
/// A run you watch: the panel beside the app says what is happening and what to look at, Space pauses and carries on,
/// Enter goes a step at a time and Esc stops. The run goes on a worker thread; the panel keeps the UI thread.
/// </summary>
internal static class Watched
{
    public static int Run(Suite suite, IReadOnlyList<Case> cases, Options options, string resultsFolder)
    {
        var control = new RunControl(unattended: false, options.Speed)
        {
            StopAtCheckpoints = options.Mode == "checks",
            StepByStep = options.Mode == "steps",
        };
        var results = new Results(resultsFolder, Console.Out);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var panel = new Panel(suite.Name, control);
        var code = ExitCode.Passed;

        panel.Loaded += (_, _) =>
        {
            var worker = new Thread(() =>
            {
                using var driver = new FlaUiDriver(control, () => panel.Bounds, panel.KeepClear, note => results.Log("NOTE", note));
                var runner = new Runner(suite, driver, new WindowsMachine(), panel, control, results, options.Variables);
                panel.Attach(runner, driver);
                results.Log("START", $"{suite.Name}: {runner.Exe}");
                try
                {
                    if (runner.Missing() is [_, ..] missing)
                    {
                        results.Log("ERROR", "Missing: " + string.Join(", ", missing));
                        panel.Tell($"These can't be found, so the tests can't start:\n\n{string.Join("\n", missing)}");
                        code = ExitCode.Missing;
                        return;
                    }
                    runner.Prepare(cases);
                    code = runner.Run(cases) > 0 ? ExitCode.Failed : ExitCode.Passed;
                }
                catch (Exception ex)
                {
                    results.Log("ERROR", ex.ToString());
                    runner.Capture("unhandled");
                    panel.Tell("Something went wrong in the runner itself. It's in run.log:\n\n" + ex.Message);
                    code = ExitCode.ScriptError;
                }
                finally
                {
                    app.Dispatcher.BeginInvoke(() => app.Shutdown());
                }
            });
            worker.SetApartmentState(ApartmentState.MTA);
            worker.IsBackground = true;
            worker.Start();
        };
        panel.Show();
        app.Run();
        return (int)code;
    }
}

/// <summary>The panel beside the app, and how a watched run shows itself in it.</summary>
internal sealed class Panel : Window, IRunHost
{
    private static readonly Brush Ink = Brush("#1A2633"), Muted = Brush("#5B6B7C");
    private const double PanelWidth = 420;

    private readonly RunControl _control;
    private readonly TextBlock _caseLabel, _caseName, _nowLabel, _stepText, _stateText, _checkTitle;
    private readonly Border _state;
    private readonly TextBox _checkText;
    private readonly WrapPanel _tools;
    private readonly ListBox _history;
    private readonly Button _mainButton;
    private Hooks? _hooks;
    private Runner? _runner;
    private FlaUiDriver? _driver;
    private volatile bool _asking, _youClicked, _typing;
    private Timer? _typingPoll;
    private nint _hwnd;

    public Panel(string title, RunControl control)
    {
        _control = control;
        Title = title;
        Width = PanelWidth;
        SizeToContent = SizeToContent.Height;
        Topmost = true;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Margin = new Thickness(15, 12, 15, 12) };
        _caseLabel = Label("GETTING READY");
        _caseName = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        _nowLabel = Label("NOW", top: 14);
        _stepText = new TextBlock { FontSize = 14.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Margin = new Thickness(0, 3, 0, 0) };
        _stateText = new TextBlock { Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _state = new Border { Height = 30, Margin = new Thickness(0, 6, 0, 0), Child = _stateText };
        _checkTitle = Label("CHECK", top: 14);
        _checkText = new TextBox { IsReadOnly = true, Height = 220, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Foreground = Ink, Margin = new Thickness(0, 4, 0, 0), FontSize = 12 };
        _tools = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        _history = new ListBox { Height = 130, Margin = new Thickness(0, 4, 0, 0), FontSize = 12, Foreground = Ink };

        var speed = new ComboBox { Width = 110, ItemsSource = Enum.GetValues<Speed>(), SelectedItem = control.Speed, Margin = new Thickness(6, 0, 0, 0) };
        speed.SelectionChanged += (_, _) => _control.SetSpeed((Speed)speed.SelectedItem);
        var speedRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        speedRow.Children.Add(new TextBlock { Text = "Speed", Foreground = Muted, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        speedRow.Children.Add(speed);

        _mainButton = Button("Pause  (Space)", () => _control.PauseOrCarryOn(), bold: true);
        var next = Button("Next step  (Enter)", () => _control.NextStep(), bold: true);
        var note = Button("Note a problem…", NoteProblem);
        var stop = Button("Stop the test  (Esc)", RequestStop);

        stack.Children.Add(_caseLabel);
        stack.Children.Add(_caseName);
        stack.Children.Add(_nowLabel);
        stack.Children.Add(_stepText);
        stack.Children.Add(_state);
        stack.Children.Add(_checkTitle);
        stack.Children.Add(_checkText);
        stack.Children.Add(_tools);
        stack.Children.Add(Label("DONE SO FAR", top: 14));
        stack.Children.Add(_history);
        stack.Children.Add(speedRow);
        stack.Children.Add(Pair(_mainButton, next, top: 10));
        stack.Children.Add(Pair(note, stop, top: 6));
        Content = stack;

        Closing += (_, e) =>
        {
            if (_runner is null || _control.StopRequested)
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
            _hooks = new Hooks(OnKey, OnClick);
        };
        Closed += (_, _) =>
        {
            _hooks?.Dispose();
            _typingPoll?.Dispose();
        };
        _control.Changed += () => Dispatcher.BeginInvoke(ShowState);
        ShowState();
    }

    public void Attach(Runner runner, FlaUiDriver driver)
    {
        (_runner, _driver) = (runner, driver);
        // Whether the person is typing in a text box, looked at a few times a second rather than from the key hook,
        // which Windows drops if it takes too long.
        _typingPoll = new Timer(_ => _typing = _youClicked && _driver.IsTyping(), null, 0, 300);
    }

    /// <summary>Where the panel is on the screen, in pixels.</summary>
    public ScreenRect? Bounds => _hwnd == 0 ? null : WindowBounds(_hwnd);

    // ===== IRunHost =====

    public void CaseStarted(int index, int count, Case item) => Ui(() =>
    {
        _caseLabel.Text = $"CASE {index} OF {count}";
        _caseName.Text = item.Name;
        _checkTitle.Text = "CHECK";
        _checkText.Text = "";
        _tools.Children.Clear();
        AddHistory("▶ " + item.Name);
    });

    public void ShowStep(string text) => Ui(() =>
    {
        _stepText.Text = text;
        AddHistory(text);
    });

    public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) => Ui(() =>
    {
        _checkTitle.Text = "CHECK: " + title.ToUpperInvariant();
        var lines = new List<string>();
        if (checks.Count > 0)
        {
            lines.Add("Checked for you:");
            foreach (var check in checks)
                lines.Add((check.Passed ? "  ✓ " : "  ✗ ") + check.Label + (check.Passed ? "" : "\n      " + check.Detail));
        }
        if (look.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add("Have a look yourself:");
            lines.AddRange(look.Select(item => "  •  " + item));
        }
        _checkText.Text = string.Join("\n", lines);
        _tools.Children.Clear();
        foreach (var tool in tools)
            _tools.Children.Add(Button(tool.Label, () =>
            {
                try
                {
                    tool.Open();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
                {
                    MessageBox.Show(this, "That didn't open: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }, width: 126, top: 0));
        if (checks.Any(check => !check.Passed))
            System.Media.SystemSounds.Exclamation.Play();
        else if (_control.StopAtCheckpoints || _control.StepByStep)
            System.Media.SystemSounds.Asterisk.Play();
    });

    public void ShowCarryingOn() => Ui(() => SetState("Carrying on in a few seconds. Space stays here", "#2E7D32"));

    public StuckAnswer AskStuck(string message, string screenshot) =>
        Ui(() => Ask("Stuck", $"{message}\n\nYou can put the app right by hand first, then try again. A screenshot of the screen now is saved with the results:\n{screenshot}",
            ["Try again", "Skip the rest of this case", "Stop the test"]) switch
        {
            0 => StuckAnswer.TryAgain,
            1 => StuckAnswer.SkipCase,
            _ => StuckAnswer.Stop,
        });

    public bool AskScriptError(string message) => Ui(() => Ask("Script error", message, ["Go on to the next case", "Stop the test"]) == 0);

    public void KeepClear(ScreenRect rect) => Ui(() =>
    {
        if (Bounds is not { } panel || !panel.Overlaps(rect))
            return;
        var area = SystemParameters.WorkArea;
        Left = Left > area.Left + 10 ? area.Left : area.Right - ActualWidth;
    });

    public void Finished(string summary, string resultsFolder, bool allPassed) => Ui(() =>
    {
        _control.CarryOn();
        SetState(allPassed ? "Finished: everything passed" : "Finished, with problems", allPassed ? "#2E7D32" : "#C62828");
        _asking = true;
        if (MessageBox.Show(this, summary + "\nOpen the results folder?", Title + ": results", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{resultsFolder}\""));
        _asking = false;
    });

    /// <summary>Shows a message, for a problem that stops the run before it starts.</summary>
    public void Tell(string message) => Ui(() =>
    {
        _asking = true;
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        _asking = false;
    });

    // ===== Keys, buttons and state =====

    // Space, Enter and Esc belong to the run unless the person is typing: in a text box they clicked into themselves,
    // or in one of the runner's own windows, such as a question. While the run is stuck they do nothing.
    private bool OnKey(int key)
    {
        var foreground = GetForegroundWindow();
        if (foreground != _hwnd)
        {
            if (_control.Waiting == Waiting.Stuck || _asking)
                return false;
            GetWindowThreadProcessId(foreground, out var processId);
            if (processId == Environment.ProcessId || _typing)
                return false;
        }
        if (Hooks.IsSpace(key))
            Dispatcher.BeginInvoke(_control.PauseOrCarryOn);
        else if (Hooks.IsEnter(key))
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

    private void RequestStop()
    {
        if (_control.StopRequested || _asking)
            return;
        if (Ask("Stop the test?", "Stop the test here? The app is left as it is now, and the results so far are saved.", ["Stop the test", "Keep going"]) != 0)
            return;
        _control.RequestStop();
        SetState("Stopping…", "#C62828");
    }

    private void NoteProblem()
    {
        if (_runner is not { } runner)
            return;
        _asking = true;
        var text = Prompt("Note a problem", "What looks wrong? It's saved with this case's results, with a screenshot of the screen now.");
        _asking = false;
        if (string.IsNullOrWhiteSpace(text))
            return;
        AddHistory("Noted: " + text);
        ThreadPool.QueueUserWorkItem(_ => runner.Note(text));
    }

    private void ShowState()
    {
        if (_control.StopRequested)
            return;
        switch (_control.Waiting)
        {
            case Waiting.Pause: SetState("Paused", "#9A5B00"); break;
            case Waiting.Step: SetState("Waiting for you: Enter does this step", "#6A3D9A"); break;
            case Waiting.Checkpoint: SetState("Your turn: look it over, then carry on", "#2E7D32"); break;
            case Waiting.Stuck: SetState("Stuck: see the message", "#C62828"); break;
            default:
                SetState(_control.StepByStep ? "Doing this step" : "Running by itself", "#004B87");
                _youClicked = false;
                break;
        }
        _nowLabel.Text = _control.Waiting == Waiting.Step ? "UP NEXT" : "NOW";
        _mainButton.Content = _control.Waiting == Waiting.None ? "Pause  (Space)" : _control.StepByStep ? "Run by itself  (Space)" : "Carry on  (Space)";
    }

    private void SetState(string text, string color)
    {
        _stateText.Text = text;
        _state.Background = Brush(color);
    }

    private void AddHistory(string text)
    {
        _history.Items.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        _history.ScrollIntoView(_history.Items[^1]);
    }

    // ===== Questions =====

    // Asks something with a button for each answer, and returns the index of the one clicked. Closing it gives the last.
    private int Ask(string title, string message, string[] answers)
    {
        _asking = true;
        var picked = answers.Length - 1;
        var window = new Window
        {
            Title = title, Owner = this, Topmost = true, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.White,
        };
        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 16), MaxWidth = 520 };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ink });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        for (var index = 0; index < answers.Length; index++)
        {
            var answer = index;
            var button = new Button { Content = "   " + answers[index] + "   ", Height = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = index == 0 };
            button.Click += (_, _) =>
            {
                picked = answer;
                window.Close();
            };
            row.Children.Add(button);
        }
        stack.Children.Add(row);
        window.Content = stack;
        window.ShowDialog();
        _asking = false;
        return picked;
    }

    private string? Prompt(string title, string message)
    {
        string? text = null;
        var window = new Window
        {
            Title = title, Owner = this, Topmost = true, SizeToContent = SizeToContent.Height, Width = 480, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.White,
        };
        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        var box = new TextBox { Margin = new Thickness(0, 10, 0, 0), Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
        var ok = new Button { Content = "   Save   ", IsDefault = true, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        ok.Click += (_, _) =>
        {
            text = box.Text;
            window.Close();
        };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Ink });
        stack.Children.Add(box);
        stack.Children.Add(ok);
        window.Content = stack;
        window.Loaded += (_, _) => box.Focus();
        window.ShowDialog();
        return text;
    }

    // ===== Helpers =====

    private void Ui(Action action) => Dispatcher.Invoke(action);

    private T Ui<T>(Func<T> function) => Dispatcher.Invoke(function);

    private static TextBlock Label(string text, double top = 0) =>
        new() { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Muted, Margin = new Thickness(0, top, 0, 0) };

    private static Button Button(string text, Action click, bool bold = false, double width = 192, double top = 0)
    {
        var button = new Button { Content = text, Width = width, Height = bold ? 36 : 30, Margin = new Thickness(0, top, 6, 6), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Focusable = false };
        button.Click += (_, _) => click();
        return button;
    }

    private static StackPanel Pair(UIElement left, UIElement right, double top)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, top, 0, 0) };
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

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
