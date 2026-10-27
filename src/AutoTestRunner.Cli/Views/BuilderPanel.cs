using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// The builder's panel beside the app. From the top: the case's name and Save; a toolbar of steps, with Record first,
/// and under it the amber pick tools, each used on the control clicked next in the app; what the builder is doing or
/// what to do next; the steps so far, each with how sturdily it finds its control and whether the app has been taken
/// through it; and the checkpoint the case ends at. A case opened to change it lists its own steps, to play up to any
/// of them, go back to one, or record again from one.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The outline and the timer go when the window closes.")]
internal sealed partial class BuilderPanel : Window
{
    private const double PanelWidth = 460;

    private readonly Suite _suite;
    private readonly CaseDraft _draft;
    private readonly TextBox? _name;
    private readonly ToggleButton _record;
    private readonly Button _save;
    private readonly TextBlock _nowIcon, _nowText;
    private readonly TextBlock _spinner;
    private readonly StackPanel _steps = new();
    private readonly ScrollViewer _stepsScroll;
    private readonly TextBlock _count;
    private readonly TextBox _checkpointTitle, _look;
    private readonly StackPanel _checks = new();
    private readonly ContentControl _footer = new();
    private readonly OutlineWindow _outline = new();
    private readonly Dictionary<DraftStep, Border> _rows = [];
    private readonly HashSet<JsonObject> _shownChecks = [];
    private readonly Dictionary<JsonObject, string> _labels = [];
    private readonly List<DraftStep> _selected = [];
    private readonly HashSet<DraftStep> _open = [];
    private readonly ContentControl _selection = new();

    private volatile int _mode = (int)BuildMode.Paused;
    private volatile nint _hwnd;
    private bool _ready, _saved, _recordOn;
    private int _changes;

    // A form waiting for a control to be picked in the app.
    private Action<TargetChoice>? _pickInto;

    // The step a Shift+click picks steps from, and the panel's box last typed in, for a kept value to go into.
    private DraftStep? _anchor;
    private TextBox? _lastBox;
    private volatile IReadOnlyList<(string Name, string Text)> _values = [];

    public BuilderPanel(BuildRequest request, CaseDraft draft)
    {
        (_suite, _draft) = (request.Suite, draft);
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = request.Edit is { } editing ? $"Edit {editing.Number}. {editing.Name}" : $"New case: {request.Name}";
        Width = PanelWidth;
        Topmost = true;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Background = Brushes.White;
        FontFamily = Ui.Text;
        Foreground = Ui.Ink;

        var root = new Grid { Margin = new Thickness(14, 12, 10, 10) };
        for (var row = 0; row < 7; row++)
            root.RowDefinitions.Add(new RowDefinition { Height = row == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // ===== The case, and Save =====
        var header = new Grid { Margin = new Thickness(0, 0, 4, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var named = new StackPanel();
        named.Children.Add(Ui.Heading(request.Edit is { } changing ? $"Changing case {changing.Number}" : request.After is { } after ? $"New case, after {after.Number}. {after.Name}{(request.Reset ? "'s reset" : "")}" : $"New case in {_suite.Name}"));
        if (request.Edit is { } existing)
            named.Children.Add(Ui.Words(existing.Name, 16, Ui.Ink, FontWeights.Bold));
        else
        {
            _name = new TextBox
            {
                Text = request.Name, FontSize = 16, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Line,
                Padding = new Thickness(0, 2, 0, 3), Background = Brushes.Transparent, ToolTip = "What the case does",
            };
            _name.TextChanged += (_, _) => _draft.Name = _name.Text.Trim();
            named.Children.Add(_name);
        }
        header.Children.Add(named);
        _save = Ui.LabelledButton(Icons.Save, "Save", Save, "MainButton");
        _save.VerticalAlignment = VerticalAlignment.Bottom;
        _save.Margin = new Thickness(12, 0, 0, 0);
        _save.ToolTip = request.Edit is null ? "Save the case, and check the suite for mistakes" : "Save the changes to the case, and check the suite for mistakes";
        Grid.SetColumn(_save, 1);
        header.Children.Add(_save);
        root.Children.Add(header);

        // ===== The steps toolbar, with Record, and the pick tools =====
        _record = RecordButton();
        var toolbars = Toolbars();
        Grid.SetRow(toolbars, 1);
        root.Children.Add(toolbars);

        // ===== What is happening, or what to do =====
        var now = new Grid();
        now.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        now.ColumnDefinitions.Add(new ColumnDefinition());
        _nowIcon = Ui.Glyph(Icons.Info, 16, Ui.Accent);
        _nowIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _spinner = Ui.Spinner(15, Ui.Accent);
        _spinner.HorizontalAlignment = HorizontalAlignment.Center;
        var icons = new Grid();
        icons.Children.Add(_nowIcon);
        icons.Children.Add(_spinner);
        var circle = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Ui.AccentSoft, Child = icons, VerticalAlignment = VerticalAlignment.Top };
        _nowText = Ui.Words("", 13, Ui.Ink);
        _nowText.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_nowText, 1);
        now.Children.Add(circle);
        now.Children.Add(_nowText);
        var nowCard = new Border { Background = Ui.Surface, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Child = now, Margin = new Thickness(0, 0, 4, 0) };
        Grid.SetRow(nowCard, 2);
        root.Children.Add(nowCard);

        // ===== The steps =====
        var stepsHeading = new DockPanel { Margin = new Thickness(0, 12, 4, 0) };
        _count = Ui.Words("", 11, Ui.Faint, wrap: false);
        _count.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_count, Dock.Right);
        stepsHeading.Children.Add(_count);
        var values = Ui.IconButton(Icons.Values, "Kept values and suite variables, with what each holds now. Click one to put it in the box you were typing in.", ShowValues, 12);
        values.Padding = new Thickness(5, 2, 5, 2);
        values.Margin = new Thickness(0, 0, 6, 2);
        DockPanel.SetDock(values, Dock.Right);
        stepsHeading.Children.Add(values);
        stepsHeading.Children.Add(Ui.Heading("Steps"));
        var stepsTop = new StackPanel();
        stepsTop.Children.Add(stepsHeading);
        stepsTop.Children.Add(_selection);
        Grid.SetRow(stepsTop, 3);
        root.Children.Add(stepsTop);
        _stepsScroll = new ScrollViewer { Content = _steps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var stepsBox = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = _stepsScroll, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(2) };
        Grid.SetRow(stepsBox, 4);
        root.Children.Add(stepsBox);

        // ===== The checkpoint =====
        var checkpoint = new StackPanel { Margin = new Thickness(0, 10, 4, 0) };
        var existingCheckpoint = request.Edit?.Steps.LastOrDefault()?["checkpoint"]?.GetValue<string>();
        var checkpointHeading = new DockPanel();
        Button? addCheck = null;
        addCheck = Ui.IconButton(Icons.Add, "Add a check: a file, a setting, a service, a value, or a control in the app", () => CheckMenu.Open(addCheck!, kind => NewCheck(kind)), 12);
        addCheck.Padding = new Thickness(5, 2, 5, 2);
        addCheck.Margin = new Thickness(0, 0, 0, 2);
        DockPanel.SetDock(addCheck, Dock.Right);
        checkpointHeading.Children.Add(addCheck);
        checkpointHeading.Children.Add(Ui.Heading(existingCheckpoint is null ? "Checkpoint" : "Checkpoint: checks added to the existing ones"));
        checkpoint.Children.Add(checkpointHeading);
        (var titleField, _checkpointTitle) = Field(existingCheckpoint ?? "Checkpoint title, such as: The order is in the list", single: true);
        if (existingCheckpoint is not null)
            titleField.Visibility = Visibility.Collapsed;
        _checkpointTitle.TextChanged += (_, _) => _draft.CheckpointTitle = _checkpointTitle.Text;
        checkpoint.Children.Add(titleField);
        _checks.Margin = new Thickness(0, 4, 0, 0);
        checkpoint.Children.Add(_checks);
        (var lookField, _look) = Field("Manual checks for the tester, one per line", single: false);
        lookField.Margin = new Thickness(0, 6, 0, 0);
        _look.TextChanged += (_, _) =>
        {
            _draft.Look.Clear();
            _draft.Look.AddRange(_look.Text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
        };
        checkpoint.Children.Add(lookField);
        Grid.SetRow(checkpoint, 5);
        root.Children.Add(checkpoint);

        _footer.Margin = new Thickness(0, 8, 4, 0);
        Grid.SetRow(_footer, 6);
        root.Children.Add(_footer);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var area = SystemParameters.WorkArea;
            Left = area.Right - PanelWidth;
            Top = area.Top;
            Height = area.Height;
        };
        Closing += (_, e) =>
        {
            // A new case with nothing recorded, or a case opened and left as it was, has nothing to lose.
            if (_saved || _changes == 0 || (_draft.Editing is null && !_draft.Steps.Any(step => step.Verb != "startApp")))
                return;
            var lost = _draft.Editing is null ? $"The {_draft.Steps.Count} steps recorded so far will be lost." : "Your changes to the case will be lost.";
            if (MessageBox.Show(this, $"Close without saving? {lost}", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
        Closed += (_, _) => _outline.Close();
        // The box typed in last, for a kept value to go into from the list of them.
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.NewFocus is TextBox box)
                _lastBox = box;
        }));
        foreach (var step in _draft.Steps)
            _steps.Children.Add(StepRow(step));
        Busy($"Getting {_suite.App.Window} ready");
        UpdateCount();
    }

    /// <summary>What the builder makes of clicks in the app now. Read from any thread.</summary>
    public BuildMode Mode => (BuildMode)_mode;

    /// <summary>The panel's window, once it has one, so a key pressed in it can be told from one pressed elsewhere. Read from any thread.</summary>
    public nint Handle => _hwnd;

    /// <summary>Where the panel is on the screen, in pixels.</summary>
    public ScreenRect? Bounds => _hwnd == 0 ? null : WindowBounds(_hwnd);

    /// <summary>The case file it was saved in, once it was.</summary>
    public string? SavedAs { get; private set; }

    /// <summary>Asks for a step to be played in the app, and added to the steps when add is true.</summary>
    public event Action<DraftStep, bool>? Play;

    /// <summary>Asks for a step's control to be outlined in the app; null takes the outline away.</summary>
    public event Action<DraftStep?>? Locate;

    /// <summary>Asks for a step to go on the end of the case without being played, such as a script still to write.</summary>
    public event Action<DraftStep>? AddStep;

    /// <summary>Asks for a check to be run against the app as it is, for a form's Try now; the answer comes on the builder's thread.</summary>
    public event Action<JsonObject, Action<CheckResult>>? Try;

    /// <summary>A macro was made of steps, for the runner to play them by it from now on.</summary>
    public event Action<Macro>? MadeMacro;

    /// <summary>The kept values and suite variables, with what each holds now. Set by the builder after each step it plays.</summary>
    public IReadOnlyList<(string Name, string Text)> Values
    {
        get => _values;
        set => _values = value;
    }

    /// <summary>Asks for the steps not played yet to be played, up to and including this one.</summary>
    public event Action<DraftStep>? RunTo;

    /// <summary>Asks for the app to start again from where the case starts, and the steps to be played up to this one.</summary>
    public event Action<DraftStep>? RollBack;

    /// <summary>
    /// The steps after this one were taken out: asks for the app to be taken to just after it, starting again when
    /// it had gone past it, and for recording to start.
    /// </summary>
    public event Action<DraftStep, bool>? EditFrom;

    // ===== Called by the builder, from any thread =====

    /// <summary>Steps were recorded: they go on the end of the list, and the list scrolls to them.</summary>
    public void Add(IReadOnlyList<DraftStep> steps)
    {
        if (steps.Count == 0)
            return;
        OnUi(() =>
        {
            foreach (var step in steps)
            {
                if (_rows.ContainsKey(step))
                    continue;
                _steps.Children.Add(StepRow(step));
                if (step.Verb != "startApp")
                    _changes++;
            }
            ShowChecks();
            UpdateCount();
            _stepsScroll.ScrollToEnd();
        });
    }

    /// <summary>A step is being played to take the app through the case: it is marked, and nothing is recorded meanwhile.</summary>
    public void Playing(DraftStep step) => OnUi(() =>
    {
        Busy($"Step {_draft.Steps.IndexOf(step) + 1}: {step.Text}");
        if (_rows.TryGetValue(step, out var row))
        {
            row.Background = Ui.AccentSoft;
            row.BringIntoView();
        }
    });

    /// <summary>Shows again whether a step was played, and whether it worked.</summary>
    public void Refresh(DraftStep step) => OnUi(() =>
    {
        if (_rows.TryGetValue(step, out var row))
            Refresh(step, row);
    });

    /// <summary>Shows again whether each step was played, as when the app starts again.</summary>
    public void Refresh() => OnUi(() =>
    {
        foreach (var (step, row) in _rows)
            Refresh(step, row);
    });

    /// <summary>The builder is busy getting the app ready or playing a step, and nothing is recorded meanwhile.</summary>
    public void Busy(string text) => OnUi(() =>
    {
        _nowIcon.Visibility = Visibility.Hidden;
        _spinner.Visibility = Visibility.Visible;
        _nowText.Text = text;
        EnableTools(false);
        _mode = (int)BuildMode.Paused;
    });

    /// <summary>
    /// The app is ready, or the steps have been played: back to the mode before, or recording when asked. While some
    /// of the case's steps are still to play, nothing can be recorded: new steps go after them.
    /// </summary>
    public void Ready(string? problem = null, bool record = false) => OnUi(() =>
    {
        var behind = Behind;
        EnableTools(!_saved && !behind);
        if (!_ready || record)
        {
            _ready = true;
            _recordOn = !behind;
            PutDown();
        }
        ShowMode(problem);
    });

    /// <summary>Turns recording on or off, as Record does: for its key, pressed in the app or the panel. A pick tool waiting is put down.</summary>
    public void ToggleRecording() => OnUi(() =>
    {
        if (!_record.IsEnabled)
            return;
        _recordOn = !_recordOn;
        PutDown();
        ShowMode();
    });

    /// <summary>Puts down the pick tool waiting for a control, as Esc does in the app or the panel.</summary>
    public void CancelPick() => OnUi(() =>
    {
        if (_armed is null)
            return;
        PutDown();
        ShowMode();
    });

    // Whether some of the steps haven't been played, so the app isn't at the end of the case.
    private bool Behind
    {
        get
        {
            lock (_draft.Steps)
                return _draft.Steps.Any(step => step.State == DraftStepState.NotPlayed);
        }
    }

    /// <summary>Something stops the builder starting, such as the app not being there.</summary>
    public void Tell(string message) => OnUi(() =>
    {
        _ready = true;
        _spinner.Visibility = Visibility.Collapsed;
        _nowIcon.Visibility = Visibility.Visible;
        _nowIcon.Text = Icons.Warning;
        _nowIcon.Foreground = Ui.Fail;
        _nowText.Text = message;
        _save.IsEnabled = false;
    });

    /// <summary>
    /// Outlines the control under the mouse while picking, or while Ctrl is held, with how a step would find it; or,
    /// when the pick tool can't be used on it, why not.
    /// </summary>
    public void ShowHover(ControlFacts facts, TargetChoice choice) => OnUi(() =>
    {
        if (Mode == BuildMode.Paused)
            return;
        if (_pickInto is null && ToolNow is { } tool && tool.Refusal(facts, choice) is { } why)
            _outline.Refuse(facts.Bounds, $"Can't {tool.Doing}: {why}");
        else
            _outline.Show(facts.Bounds, choice.Shown, choice.Quality);
    });

    /// <summary>The mouse left the app, or picking ended: the outline goes.</summary>
    public void EndHover() => OnUi(_outline.Hide);

    /// <summary>
    /// A control was clicked while picking, or Ctrl+clicked while recording: it goes to the form waiting for it, or the
    /// pick tool is used on it, when it can be.
    /// </summary>
    public void ShowPicked(ControlFacts facts, TargetChoice choice) => OnUi(() =>
    {
        if (_pickInto is { } waiting)
        {
            _pickInto = null;
            ShowMode();
            waiting(choice);
            return;
        }
        if (ToolNow is not { } tool)
        {
            Note("Choose what to do from the amber tools first, then click the control. After that, Ctrl+click does the same again.");
            return;
        }
        if (tool.Refusal(facts, choice) is { } why)
        {
            Note($"Can't {tool.Doing} on {facts.Label}: {why} Pick another control, or press Esc.", Icons.Warning, Ui.Fail);
            return;
        }
        _lastTool = tool;
        PutDown();
        ShowMode();
        _outline.Hide();
        Use(tool, facts, choice);
    });

    /// <summary>A step was played: marked red with why when it didn't work.</summary>
    public void Tried(DraftStep step, string? problem) => OnUi(() =>
    {
        if (_rows.TryGetValue(step, out var row))
            Mark(row, problem);
    });

    /// <summary>Outlines a step's control, found in the app, or takes the outline away.</summary>
    public void Outline(ScreenRect? box, TargetChoice? choice) => OnUi(() =>
    {
        if (box is { } found && choice is not null)
            _outline.Show(found, choice.Shown, choice.Quality);
        else
            _outline.Hide();
    });

    // ===== Modes =====

    // Picking while a pick tool or a form waits for a control; else recording, when Record is on. Nothing is made of
    // the app while it is behind the case, once the case is saved, or before the app is ready.
    private void ShowMode(string? problem = null)
    {
        // Behind the case, Record goes off: played up to the end, the app waits for Record to be pressed again.
        if (Behind)
            _recordOn = false;
        var mode = !_ready || _saved || Behind ? BuildMode.Paused
            : _pickInto is not null || _armed is not null ? BuildMode.Picking
            : _recordOn ? BuildMode.Recording
            : BuildMode.Paused;
        _mode = (int)mode;
        _record.IsChecked = _recordOn && !_saved;
        _spinner.Visibility = Visibility.Collapsed;
        _nowIcon.Visibility = Visibility.Visible;
        _nowIcon.Foreground = problem is not null ? Ui.Fail : mode == BuildMode.Picking ? Ui.AmberInk : Ui.Accent;
        var app = _suite.App.Window;
        (_nowIcon.Text, _nowText.Text) = (problem, mode) switch
        {
            ({ } why, _) => (Icons.Warning, why),
            (_, BuildMode.Picking) when _pickInto is not null => (Icons.Pick, $"Click the control in {app}. It goes in the form."),
            (_, BuildMode.Picking) => (Icons.Pick, $"Click a control in {app} to {_armed!.Doing}. Esc puts the tool down."),
            (_, BuildMode.Recording) => (Icons.Record,
                $"Recording. Use {app} as normal: each click, choice and entry becomes a step. {RecordKey} pauses.{(_lastTool is null ? "" : $" Ctrl+click to {_lastTool.Doing} again.")}"),
            _ when _saved => (Icons.Passed, "Saved."),
            _ when Behind => (Icons.Next, BehindText()),
            _ => (Icons.Pause, $"Paused. Press Record or {RecordKey} to carry on."),
        };
        if (mode != BuildMode.Picking)
            _outline.Hide();
    }

    private string BehindText()
    {
        int played;
        lock (_draft.Steps)
            played = _draft.Steps.TakeWhile(step => step.State != DraftStepState.NotPlayed).Count();
        var at = played == 0 ? "the start of the case" : $"step {played}";
        return $"{_suite.App.Window} is at {at}. Hover a step to run to it or edit from it.";
    }

    // ===== The checkpoint =====

    // Says what happened in the card at the top, as a step does.
    private void Note(string text, string glyph = Icons.Info, Brush? color = null)
    {
        _spinner.Visibility = Visibility.Collapsed;
        _nowIcon.Visibility = Visibility.Visible;
        _nowIcon.Text = glyph;
        _nowIcon.Foreground = color ?? Ui.Accent;
        _nowText.Text = text;
    }

    private void AddCheck(JsonObject check, string label)
    {
        _labels[check] = label;
        lock (_draft.Checks)
            _draft.Checks.Add(check);
        _changes++;
        ShowChecks();
    }

    // Shows the checks not shown yet: those picked here, and those the builder added for a watched control.
    private void ShowChecks()
    {
        List<JsonObject> checks;
        lock (_draft.Checks)
            checks = [.. _draft.Checks.Where(check => !_shownChecks.Contains(check))];
        foreach (var check in checks)
            ShowCheck(check, _labels.GetValueOrDefault(check) ?? check["label"]?.ToString() ?? JsonLine.Write(check));
    }

    private void ShowCheck(JsonObject check, string label)
    {
        _shownChecks.Add(check);
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Tag = check };
        var remove = Ui.IconButton(Icons.Cross, "Remove this check", () =>
        {
            lock (_draft.Checks)
                _draft.Checks.Remove(check);
            _checks.Children.Remove(row);
        }, 10);
        remove.Padding = new Thickness(4, 2, 4, 2);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        var glyph = Ui.Glyph(Icons.CheckList, 13, Ui.Pass);
        glyph.Margin = new Thickness(2, 0, 8, 0);
        row.Children.Add(glyph);
        row.Children.Add(Ui.Words(label, 12.5, Ui.Ink, wrap: false));
        _checks.Children.Add(row);
    }

    // ===== Steps =====

    private Border StepRow(DraftStep step)
    {
        var row = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(2, 5, 4, 5), Margin = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Tag = step };
        _rows[step] = row;
        Refresh(step, row);
        row.MouseEnter += (_, _) =>
        {
            if (Buttons(row) is { } buttons)
                buttons.Visibility = Visibility.Visible;
            if (row.Background == Brushes.Transparent)
                row.Background = Ui.Surface;
            if (step.Target is not null && Mode != BuildMode.Picking)
                Locate?.Invoke(step);
        };
        row.MouseLeave += (_, _) =>
        {
            if (Buttons(row) is { } buttons)
                buttons.Visibility = Visibility.Hidden;
            if (row.Background == Ui.Surface)
                row.Background = Brushes.Transparent;
            if (Mode != BuildMode.Picking)
                Locate?.Invoke(null);
        };
        // Shift+click picks the steps from the last one picked to this one, Ctrl+click picks one more or one fewer.
        row.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0)
                return;
            Select(step, range: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
        };
        return row;
    }

    // The row's line: its fold-out chevron, number, icon, words and buttons.
    private static Grid? Line(Border row) => row.Child is StackPanel { Children: [Grid line, ..] } ? line : null;

    private static StackPanel? Buttons(Border row) => Line(row)?.Children.OfType<StackPanel>().LastOrDefault(panel => panel.Orientation == Orientation.Horizontal);

    // Draws a step's row: a chevron that folds out its details, its number and icon, what it says (click to change
    // it), how it finds its control, and its buttons, which show while the mouse is over it. A step not played yet is
    // greyed; one that didn't work is red; one picked is tinted; one switched off is struck through. The case's reset
    // steps have purple icons, under a band at the first of them.
    private void Refresh(DraftStep step, Border row)
    {
        var played = step.State != DraftStepState.NotPlayed;
        int index, resetAt;
        lock (_draft.Steps)
            (index, resetAt) = (_draft.Steps.IndexOf(step), _draft.Steps.FindIndex(each => each.Reset));
        var inReset = resetAt >= 0 && index >= resetAt;
        var off = step.Json["disabled"]?.GetValue<bool>() == true;
        Mark(row, step.State == DraftStepState.Failed ? step.Problem ?? "It didn't work." : null);
        if (_selected.Contains(step))
            (row.Background, row.BorderBrush, row.BorderThickness) = (Ui.AccentSoft, Ui.Accent, new Thickness(3, 0, 0, 0));
        if (!played)
            row.ToolTip = "Not played yet";
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = Ui.Words($"{_draft.Steps.IndexOf(step) + 1}", 11, Ui.Faint, wrap: false);
        number.VerticalAlignment = VerticalAlignment.Top;
        number.Margin = new Thickness(0, 2, 0, 0);
        var open = _open.Contains(step);
        var chevron = Ui.IconButton(open ? Icons.Expand : Icons.Collapse, open ? "Fold the details away" : "Change this step's details", () => Fold(step), 9);
        chevron.Padding = new Thickness(3);
        chevron.VerticalAlignment = VerticalAlignment.Top;
        var icon = Ui.Glyph(off ? Icons.Blocked : step.Waits ? Icons.Wait : step.Verb == "repeat" ? Icons.Repeat : step.Verb == "if" ? Icons.Branch : Icons.Of(step.Kind), 14,
            played && !off ? inReset ? Ui.Step : Ui.Accent : Ui.Faint);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        var words = new StackPanel();
        var say = Ui.Words(step.Text, 13, played && !off ? Ui.Ink : Ui.Muted);
        if (off)
            say.TextDecorations = TextDecorations.Strikethrough;
        say.Cursor = Cursors.IBeam;
        say.ToolTip = off ? "Switched off: the step is skipped when the case runs. Click to edit the description" : "Click to edit the description";
        say.MouseLeftButtonUp += (_, e) =>
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0)
                EditSay(step, words, say);
        };
        words.Children.Add(say);
        if (step.Target is { } target)
        {
            var chip = Chip(target);
            chip.ToolTip = target.Why;
            chip.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(chip);
        }
        else if (Detail(step) is { } detail)
            words.Children.Add(Ui.Words(detail, 11, Ui.Muted, wrap: false));
        // Drawn again under the mouse, as after Apply, the buttons stay: the mouse won't come onto the row again to show them.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Visibility = row.IsMouseOver ? Visibility.Visible : Visibility.Hidden };
        var actions = new List<(string Glyph, string Tip, Action Act)>();
        if (played)
        {
            actions.Add((Icons.Play, "Play this step", () => Play?.Invoke(step, false)));
            actions.Add((Icons.Retry, "Roll back to here\nRestarts the app and plays the steps up to this one. Later steps are kept.", () => RollBackTo(step)));
        }
        else
            actions.Add((Icons.Next, "Run to here\nPlays the steps up to this one.", () => RunToHere(step)));
        actions.Add((Icons.Edit, "Edit from here\nPlays up to this step, removes the steps after it, and starts recording.", () => EditFromHere(step)));
        if (step.Script is { } script)
            actions.Add((Icons.Document, "Open the script in Notepad++ or Notepad", () => OpenScript(script)));
        if (step.Verb is "repeat" or "if")
            actions.Add((Icons.Back, "Take the steps out of it", () => Unwrap(step)));
        if (index == resetAt)
            actions.Add((Icons.Reset, "Not a reset\nThese steps become the case's own again.", () => MarkReset(step, false)));
        else if (!inReset)
            actions.Add((Icons.Reset, "Reset from here\nThis step and those after it take the app back to where this case leaves it. The cases that start after this one play them instead of starting the app again.", () => MarkReset(step, true)));
        actions.Add((Icons.Blocked, off ? "Switch this step back on" : "Switch this step off: it is skipped when the case runs, and kept in the file", () => SwitchOff(step, !off)));
        actions.Add((Icons.Up, "Move up", () => Move(step, -1)));
        actions.Add((Icons.Down, "Move down", () => Move(step, 1)));
        actions.Add((Icons.Delete, "Delete this step", () => Remove(step)));
        foreach (var (glyph, tip, act) in actions)
        {
            var button = Ui.IconButton(glyph, tip, act, 12);
            button.Padding = new Thickness(5, 3, 5, 3);
            buttons.Children.Add(button);
        }
        Grid.SetColumn(number, 1);
        Grid.SetColumn(icon, 2);
        Grid.SetColumn(words, 3);
        Grid.SetColumn(buttons, 4);
        grid.Children.Add(number);
        grid.Children.Add(chevron);
        grid.Children.Add(icon);
        grid.Children.Add(words);
        grid.Children.Add(buttons);
        if (index == resetAt)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (UIElement child in grid.Children)
                Grid.SetRow(child, 1);
            var band = new Border
            {
                BorderBrush = Ui.Step, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(0, 4, 0, 0),
                Child = Ui.IconText(Icons.Reset, "Reset", Ui.Step, 11.5, FontWeights.SemiBold),
                ToolTip = "The case's reset: these steps take the app back to where it leaves it, and play again before each case that starts after it.",
            };
            Grid.SetColumnSpan(band, 5);
            grid.Children.Add(band);
        }
        var stack = new StackPanel();
        stack.Children.Add(grid);
        if (open)
            stack.Children.Add(Details(step));
        row.Child = stack;
    }

    // Makes a step the first of the case's reset, or makes the reset steps the case's own again.
    private void MarkReset(DraftStep step, bool reset)
    {
        lock (_draft.Steps)
            foreach (var each in _draft.Steps)
                each.Reset = reset && each == step;
        _changes++;
        foreach (var (each, row) in _rows)
            Refresh(each, row);
    }

    // What a step without a control shows under it: the message box it answers, the file it picks, the wait.
    private static string? Detail(DraftStep step) => step.Verb switch
    {
        "answerDialog" when step.Json["expect"] is { } expect => $"{step.Json["answerDialog"]}: \"{expect}…\"",
        "answerDialog" => $"{step.Json["answerDialog"]}",
        "pickFile" or "pickFolder" => $"{step.Json["path"]}",
        "call" => $"{step.Json["call"]}, a macro",
        "repeat" or "if" => StepWrapper.Inside(step.Json).Count is var count && count == 1 ? "1 step inside" : $"{count} steps inside",
        "script" => step.Script,
        _ => null,
    };

    // ===== Going through the case =====

    private void RunToHere(DraftStep step)
    {
        if (_ready && !_saved)
            RunTo?.Invoke(step);
    }

    private void RollBackTo(DraftStep step)
    {
        if (_ready && !_saved)
            RollBack?.Invoke(step);
    }

    // Takes out the steps after this one, once the person has seen which, and which cases carry on from this one to
    // mark as needing a look; then the app is taken to just after it, and recording starts.
    private void EditFromHere(DraftStep step)
    {
        if (!_ready || _saved)
            return;
        List<DraftStep> later;
        int index;
        lock (_draft.Steps)
        {
            index = _draft.Steps.IndexOf(step);
            later = [.. _draft.Steps.Skip(index + 1)];
        }
        if (index < 0)
            return;
        if (later.Count > 0)
        {
            var editing = _draft.Editing;
            var carryingOn = editing is null ? [] : _suite.CarryingOn(editing);
            if (EditFromWindow.Ask(this, index + 1, later, carryingOn) is not { } flagged)
                return;
            foreach (var item in flagged)
                _draft.Review[item.File] = $"Case {editing!.Number}, {editing.Name}, was changed from step {index + 1}: check this case still starts where it should.";
            lock (_draft.Steps)
                _draft.Steps.RemoveRange(index + 1, later.Count);
            foreach (var gone in later)
            {
                Deselect(gone);
                _steps.Children.Remove(_rows[gone]);
                _rows.Remove(gone);
            }
            _changes++;
            UpdateCount();
        }
        Locate?.Invoke(null);
        EditFrom?.Invoke(step, later.Any(gone => gone.State != DraftStepState.NotPlayed));
    }

    private void EditSay(DraftStep step, StackPanel words, TextBlock say)
    {
        var box = new TextBox { Text = step.Text, FontSize = 13, Padding = new Thickness(2, 1, 2, 1) };
        Swap(words, say, box);
        void Done(bool keep)
        {
            if (!words.Children.Contains(box))
                return;
            if (keep && box.Text.Trim().Length > 0 && box.Text.Trim() != step.Text)
            {
                step.Text = box.Text.Trim();
                _changes++;
            }
            say.Text = step.Text;
            Swap(words, box, say);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
                Done(e.Key == Key.Enter);
        };
        box.LostKeyboardFocus += (_, _) => Done(true);
        Activate();
        box.Focus();
        box.SelectAll();
    }

    // Puts one in the other's place. A panel's child can't be set over another, which WPF says is still connected.
    private static void Swap(Panel panel, UIElement from, UIElement to)
    {
        var index = panel.Children.IndexOf(from);
        if (index < 0)
            return;
        panel.Children.RemoveAt(index);
        panel.Children.Insert(index, to);
    }

    private void Move(DraftStep step, int by)
    {
        var index = _draft.Steps.IndexOf(step);
        var to = index + by;
        if (index < 0 || to < 0 || to >= _draft.Steps.Count)
            return;
        lock (_draft.Steps)
        {
            _draft.Steps.RemoveAt(index);
            _draft.Steps.Insert(to, step);
        }
        _changes++;
        var row = _rows[step];
        _steps.Children.Remove(row);
        _steps.Children.Insert(to, row);
        Renumber();
    }

    private void Remove(DraftStep step)
    {
        Deselect(step);
        lock (_draft.Steps)
        {
            var index = _draft.Steps.IndexOf(step);
            if (step.Reset && index + 1 < _draft.Steps.Count)
                _draft.Steps[index + 1].Reset = true;
            _draft.Steps.Remove(step);
        }
        _changes++;
        _steps.Children.Remove(_rows[step]);
        _rows.Remove(step);
        Locate?.Invoke(null);
        Renumber();
        UpdateCount();
    }

    // Numbers the steps again, and redraws them all when the case has a reset, whose band moves with its first step.
    private void Renumber()
    {
        if (_draft.Steps.Any(step => step.Reset))
        {
            foreach (var (step, row) in _rows)
                Refresh(step, row);
            return;
        }
        foreach (var (step, row) in _rows)
            if (Line(row)?.Children[0] is TextBlock number)
                number.Text = $"{_draft.Steps.IndexOf(step) + 1}";
    }

    private void UpdateCount()
    {
        _count.Text = _draft.Steps.Count == 1 ? "1 step" : $"{_draft.Steps.Count} steps";
        _save.IsEnabled = !_saved && (_draft.Steps.Count > 0 || _draft.Editing is not null);
    }

    private static void Mark(Border row, string? problem)
    {
        row.Background = problem is null ? Brushes.Transparent : Ui.FailSoft;
        row.BorderBrush = Ui.Fail;
        row.BorderThickness = new Thickness(problem is null ? 0 : 3, 0, 0, 0);
        row.ToolTip = problem;
    }

    // ===== Saving =====

    private void Save()
    {
        if (_draft.Editing is null && _draft.Name.Length == 0)
            _draft.Name = "New case";
        string path;
        try
        {
            path = CaseWriter.Save(_suite, _draft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _footer.Content = Card(Ui.Fail, Ui.FailSoft, Icons.Failed, "Couldn't save", ex.Message, []);
            return;
        }
        _saved = true;
        SavedAs = path;
        PutDown();
        ShowMode();
        EnableTools(false);
        UpdateCount();
        _outline.Hide();
        var relative = Path.GetRelativePath(_suite.Folder, path);
        IReadOnlyList<string> problems;
        try
        {
            Suite.Load(_suite.Folder);
            problems = [];
        }
        catch (SuiteException ex)
        {
            problems = ex.Problems;
        }
        var open = (Icons.Document, "Open it", (Action)(() => new WindowsMachine().Open("file", path)));
        var done = (Icons.Back, "Back to the cases", (Action)Close);
        _footer.Content = problems.Count == 0
            ? Card(Ui.Pass, Ui.PassSoft, Icons.Passed, $"Saved in {relative}", "No mistakes found in the suite.", [open, done])
            : Card(Ui.Hold, Ui.HoldSoft, Icons.Warning, $"Saved in {relative}, with mistakes", string.Join("\n", problems), [open, done]);
    }

    private static Border Card(Brush color, Brush background, string glyph, string title, string text, (string Glyph, string Text, Action Click)[] buttons)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.IconText(glyph, title, color, 13));
        var words = Ui.Words(text, 12, Ui.Ink);
        words.Margin = new Thickness(0, 4, 0, 6);
        stack.Children.Add(words);
        var row = new WrapPanel();
        foreach (var (buttonGlyph, buttonText, click) in buttons)
            row.Children.Add(Ui.LabelledButton(buttonGlyph, buttonText, click));
        stack.Children.Add(row);
        return new Border { Child = stack, BorderBrush = color, BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4), Background = background, Padding = new Thickness(10, 8, 6, 4) };
    }

    // ===== Pieces =====

    /// <summary>How a step finds its control, after a dot coloured by how sturdy that is.</summary>
    private static StackPanel Chip(TargetChoice choice)
    {
        var chip = new StackPanel { Orientation = Orientation.Horizontal };
        chip.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = OutlineWindow.Quality(choice.Quality), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        chip.Children.Add(new TextBlock { Text = choice.Shown, FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
        return chip;
    }

    // A text box with grey words in it until something is typed.
    private static (Grid Field, TextBox Box) Field(string placeholder, bool single)
    {
        var box = new TextBox
        {
            FontSize = 12.5, Padding = new Thickness(5, 4, 5, 4), AcceptsReturn = !single, TextWrapping = TextWrapping.Wrap,
            MinHeight = single ? 0 : 44, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderBrush = Ui.Line,
        };
        var hint = new TextBlock { Text = placeholder, FontSize = 12.5, Foreground = Ui.Faint, Margin = new Thickness(8, 5, 8, 0), IsHitTestVisible = false };
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.Children.Add(box);
        grid.Children.Add(hint);
        return (grid, box);
    }

    private void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

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
}
