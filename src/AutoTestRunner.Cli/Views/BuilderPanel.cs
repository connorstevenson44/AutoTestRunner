using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
/// The builder's panel beside the app. From the top: the case's name; Record and Pick, and Save; what the builder is
/// doing or what to do next; the control last picked, with what can be done with it; the steps so far, each with how
/// sturdily it finds its control; and the checkpoint the case ends at.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The outline and the timer go when the window closes.")]
internal sealed partial class BuilderPanel : Window
{
    private const double PanelWidth = 460;

    private readonly Suite _suite;
    private readonly CaseDraft _draft;
    private readonly TextBox? _name;
    private readonly ToggleButton _record, _pick;
    private readonly Button _save;
    private readonly TextBlock _nowIcon, _nowText;
    private readonly TextBlock _spinner;
    private readonly ContentControl _picked = new();
    private readonly StackPanel _steps = new();
    private readonly ScrollViewer _stepsScroll;
    private readonly TextBlock _count;
    private readonly TextBox _checkpointTitle, _look;
    private readonly StackPanel _checks = new();
    private readonly ContentControl _footer = new();
    private readonly OutlineWindow _outline = new();
    private readonly Dictionary<DraftStep, Border> _rows = [];

    private volatile int _mode = (int)BuildMode.Paused;
    private bool _ready, _saved;
    private (ControlFacts Facts, TargetChoice Choice)? _current;
    private nint _hwnd;

    public BuilderPanel(BuildRequest request, CaseDraft draft)
    {
        (_suite, _draft) = (request.Suite, draft);
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = request.AddTo is { } adding ? $"Add to {adding.Number}. {adding.Name}" : $"New case: {request.Name}";
        Width = PanelWidth;
        Topmost = true;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Background = Brushes.White;
        FontFamily = Ui.Text;
        Foreground = Ui.Ink;

        var root = new Grid { Margin = new Thickness(14, 12, 10, 10) };
        for (var row = 0; row < 8; row++)
            root.RowDefinitions.Add(new RowDefinition { Height = row == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // ===== The case =====
        var header = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        header.Children.Add(Ui.Heading(request.AddTo is { } addTo ? $"Adding steps to case {addTo.Number}" : request.After is { } after ? $"New case, after {after.Number}. {after.Name}" : $"New case in {_suite.Name}"));
        if (request.AddTo is { } existing)
            header.Children.Add(Ui.Words(existing.Name, 16, Ui.Ink, FontWeights.Bold));
        else
        {
            _name = new TextBox
            {
                Text = request.Name, FontSize = 16, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Line,
                Padding = new Thickness(0, 2, 0, 3), Background = Brushes.Transparent, ToolTip = "What the case does",
            };
            _name.TextChanged += (_, _) => _draft.Name = _name.Text.Trim();
            header.Children.Add(_name);
        }
        root.Children.Add(header);

        // ===== Record, Pick and Save =====
        var tools = new Grid { Margin = new Thickness(-4, 10, 4, 6) };
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tools.ColumnDefinitions.Add(new ColumnDefinition());
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        _record = Tile(Icons.Record, "Record", "Each click, pick and tick in the app becomes a step, and what you type becomes one when you leave the box", Ui.Fail);
        _pick = Tile(Icons.Pick, "Pick", "Point at a control and click it, to wait for it, check it or name it. The click doesn't reach the app. Ctrl+click picks while recording.", null);
        _record.Click += (_, _) => SetMode(_record.IsChecked == true ? BuildMode.Recording : BuildMode.Paused);
        _pick.Click += (_, _) => SetMode(_pick.IsChecked == true ? BuildMode.Picking : BuildMode.Paused);
        modes.Children.Add(_record);
        modes.Children.Add(_pick);
        _save = Ui.LabelledButton(Icons.Save, "Save", Save, "MainButton");
        _save.VerticalAlignment = VerticalAlignment.Center;
        _save.Margin = new Thickness(0);
        _save.ToolTip = request.AddTo is null ? "Saves the case in cases/, and checks the suite" : "Puts the new steps in before the case's checkpoint, and checks the suite";
        Grid.SetColumn(_save, 2);
        tools.Children.Add(modes);
        tools.Children.Add(_save);
        Grid.SetRow(tools, 1);
        root.Children.Add(tools);

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

        // ===== The control picked =====
        _picked.Margin = new Thickness(0, 8, 4, 0);
        Grid.SetRow(_picked, 3);
        root.Children.Add(_picked);

        // ===== The steps =====
        var stepsHeading = new DockPanel { Margin = new Thickness(0, 12, 4, 0) };
        _count = Ui.Words("", 11, Ui.Faint, wrap: false);
        DockPanel.SetDock(_count, Dock.Right);
        stepsHeading.Children.Add(_count);
        stepsHeading.Children.Add(Ui.Heading("Steps"));
        Grid.SetRow(stepsHeading, 4);
        root.Children.Add(stepsHeading);
        _stepsScroll = new ScrollViewer { Content = _steps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var stepsBox = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = _stepsScroll, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(2) };
        Grid.SetRow(stepsBox, 5);
        root.Children.Add(stepsBox);

        // ===== The checkpoint =====
        var checkpoint = new StackPanel { Margin = new Thickness(0, 10, 4, 0) };
        var existingCheckpoint = request.AddTo?.Steps.LastOrDefault()?["checkpoint"]?.GetValue<string>();
        checkpoint.Children.Add(Ui.Heading(existingCheckpoint is null ? "Checkpoint" : "Checkpoint: adds to its own"));
        (var titleField, _checkpointTitle) = Field(existingCheckpoint ?? "What should be true now", single: true);
        if (existingCheckpoint is not null)
            titleField.Visibility = Visibility.Collapsed;
        _checkpointTitle.TextChanged += (_, _) => _draft.CheckpointTitle = _checkpointTitle.Text;
        checkpoint.Children.Add(titleField);
        _checks.Margin = new Thickness(0, 4, 0, 0);
        checkpoint.Children.Add(_checks);
        (var lookField, _look) = Field("What to look at yourself, a line each", single: false);
        lookField.Margin = new Thickness(0, 6, 0, 0);
        _look.TextChanged += (_, _) =>
        {
            _draft.Look.Clear();
            _draft.Look.AddRange(_look.Text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
        };
        checkpoint.Children.Add(lookField);
        Grid.SetRow(checkpoint, 6);
        root.Children.Add(checkpoint);

        _footer.Margin = new Thickness(0, 8, 4, 0);
        Grid.SetRow(_footer, 7);
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
            if (_saved || !_draft.Steps.Any(step => step.Verb != "startApp"))
                return;
            if (MessageBox.Show(this, $"Close without saving? The {_draft.Steps.Count} steps so far are lost.", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
        Closed += (_, _) => _outline.Close();
        Busy($"Getting {_suite.App.Window} ready");
        UpdateCount();
    }

    /// <summary>What the builder makes of clicks in the app now. Read from any thread.</summary>
    public BuildMode Mode => (BuildMode)_mode;

    /// <summary>Where the panel is on the screen, in pixels.</summary>
    public ScreenRect? Bounds => _hwnd == 0 ? null : WindowBounds(_hwnd);

    /// <summary>The case file it was saved in, once it was.</summary>
    public string? SavedAs { get; private set; }

    /// <summary>Asks for a step to be played in the app, and added to the steps when add is true.</summary>
    public event Action<DraftStep, bool>? Play;

    /// <summary>Asks for a step's control to be outlined in the app; null takes the outline away.</summary>
    public event Action<DraftStep?>? Locate;

    // ===== Called by the builder, from any thread =====

    /// <summary>Steps were recorded: they go on the end of the list, and the list scrolls to them.</summary>
    public void Add(IReadOnlyList<DraftStep> steps)
    {
        if (steps.Count == 0)
            return;
        OnUi(() =>
        {
            foreach (var step in steps)
                if (!_rows.ContainsKey(step))
                    _steps.Children.Add(StepRow(step));
            UpdateCount();
            _stepsScroll.ScrollToEnd();
        });
    }

    /// <summary>The builder is busy getting the app ready or playing a step, and nothing is recorded meanwhile.</summary>
    public void Busy(string text) => OnUi(() =>
    {
        _nowIcon.Visibility = Visibility.Hidden;
        _spinner.Visibility = Visibility.Visible;
        _nowText.Text = text;
        _record.IsEnabled = _pick.IsEnabled = false;
        _mode = (int)BuildMode.Paused;
    });

    /// <summary>The app is ready, or the step has been played: back to the mode before.</summary>
    public void Ready(string? problem = null) => OnUi(() =>
    {
        _record.IsEnabled = _pick.IsEnabled = !_saved;
        if (!_ready)
        {
            _ready = true;
            _record.IsChecked = true;
        }
        SetMode(_saved ? BuildMode.Paused : _pick.IsChecked == true ? BuildMode.Picking : _record.IsChecked == true ? BuildMode.Recording : BuildMode.Paused, problem);
    });

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

    /// <summary>Outlines the control under the mouse while picking, with how a step would find it.</summary>
    public void ShowHover(ControlFacts facts, TargetChoice choice) => OnUi(() =>
    {
        if (Mode == BuildMode.Paused)
            return;
        _outline.Show(facts.Bounds, choice.Shown, choice.Quality);
    });

    /// <summary>The mouse left the app, or picking ended: the outline goes back to the control picked, if any.</summary>
    public void EndHover() => OnUi(() =>
    {
        if (_current is { } current)
            _outline.Show(current.Facts.Bounds, current.Choice.Shown, current.Choice.Quality);
        else
            _outline.Hide();
    });

    /// <summary>A control was picked: it stays outlined, and the panel offers what to do with it.</summary>
    public void ShowPicked(ControlFacts facts, TargetChoice choice, string details) => OnUi(() =>
    {
        _current = (facts, choice);
        _outline.Show(facts.Bounds, choice.Shown, choice.Quality);
        _picked.Content = PickedCard(facts, choice, details);
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
            EndHover();
    });

    // ===== Modes =====

    private void SetMode(BuildMode mode, string? problem = null)
    {
        _mode = (int)mode;
        _record.IsChecked = mode == BuildMode.Recording;
        _pick.IsChecked = mode == BuildMode.Picking;
        _spinner.Visibility = Visibility.Collapsed;
        _nowIcon.Visibility = Visibility.Visible;
        _nowIcon.Foreground = problem is null ? Ui.Accent : Ui.Fail;
        var app = _suite.App.Window;
        (_nowIcon.Text, _nowText.Text) = (problem, mode) switch
        {
            ({ } why, _) => (Icons.Warning, why),
            (_, BuildMode.Recording) => (Icons.Record, $"Use {app}: each click, pick and tick becomes a step, and what you type becomes one when you leave the box. Ctrl+click picks a control without using it."),
            (_, BuildMode.Picking) => (Icons.Pick, $"Point at a control in {app}, then click it to wait for it, check it or name it. The click doesn't reach {app}."),
            _ when _saved => (Icons.Passed, "Saved."),
            _ => (Icons.Pause, "Not recording. Record or Pick carries on."),
        };
        if (mode != BuildMode.Picking && _current is null)
            _outline.Hide();
    }

    // ===== The control picked =====

    private Border PickedCard(ControlFacts facts, TargetChoice choice, string details)
    {
        var stack = new StackPanel();
        var top = new DockPanel();
        var close = Ui.IconButton(Icons.Cross, "Done with this control", ClosePicked, 11);
        close.Padding = new Thickness(5, 3, 5, 3);
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(Ui.Words($"{facts.Type} \"{facts.Label}\"", 13, Ui.Ink, FontWeights.SemiBold, wrap: false));
        stack.Children.Add(top);
        var chip = Chip(choice);
        chip.ToolTip = choice.Why + "\n\n" + details;
        chip.Margin = new Thickness(0, 4, 0, 6);
        stack.Children.Add(chip);

        var actions = new WrapPanel();
        void Action(string glyph, string text, PickAction action, string tip, bool enabled = true)
        {
            var button = Ui.LabelledButton(glyph, text, () => Picked(action));
            button.ToolTip = tip;
            button.IsEnabled = enabled;
            button.Padding = new Thickness(8, 4, 9, 4);
            actions.Children.Add(button);
        }
        var text = facts.Value is { Length: > 0 } value ? value : facts.Name;
        Action(Icons.Pointer, "Click", PickAction.Click, "Clicks it now, and adds the step");
        Action(Icons.Look, "Point at", PickAction.Hover, "Moves the mouse onto it, as for buttons that only show while it is over their row");
        Action(Icons.Wait, "Wait for it", PickAction.WaitFor, "Waits until it is there");
        Action(Icons.Wait, "Wait for it to go", PickAction.WaitGone, "Waits until it has gone");
        Action(Icons.Wait, "Wait for its text", PickAction.WaitText, $"Waits until it says \"{StepMaker.StartOf(text)}\"", text.Length > 0);
        Action(Icons.Note, "Keep its text", PickAction.Keep, "Keeps what it says in a variable, for a check or a later step", text.Length > 0);
        Action(Icons.CheckList, "Check it shows", PickAction.CheckShows, "The checkpoint checks that it is there");
        Action(Icons.CheckList, "Check its text", PickAction.CheckText, $"The checkpoint checks that it says \"{text}\"", text.Length > 0);
        Action(Icons.Name, "Name it", PickAction.Name, "Adds it to elements.json under a name of your own, and has the steps use the name",
            choice.Json is JsonObject selector && !selector.ContainsKey("element"));
        stack.Children.Add(actions);
        return new Border
        {
            Child = stack, BorderBrush = Brushes.DarkOrange, BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4),
            Background = Ui.Brush("#FFF6EC"), Padding = new Thickness(10, 8, 6, 4),
        };
    }

    private void ClosePicked()
    {
        _current = null;
        _picked.Content = null;
        _outline.Hide();
    }

    private void Picked(PickAction action)
    {
        if (_current is not { } current)
            return;
        var (facts, choice) = current;
        var text = facts.Value is { Length: > 0 } value ? value : facts.Name;
        switch (action)
        {
            case PickAction.Click:
                Play?.Invoke(StepMaker.Click(choice, facts), true);
                break;
            case PickAction.Hover:
                Play?.Invoke(StepMaker.Hover(choice, facts), true);
                break;
            case PickAction.WaitFor:
                Play?.Invoke(StepMaker.Find(choice, facts), true);
                break;
            case PickAction.WaitGone:
                Play?.Invoke(StepMaker.WaitGone(choice, facts), true);
                break;
            case PickAction.WaitText:
                Play?.Invoke(StepMaker.WaitText(choice, facts, text), true);
                break;
            case PickAction.Keep:
                if (Ui.Prompt(this, "Keep its text", $"What to call what it says (\"{text}\"), as ${{name}} in later steps and checks:", Variable(choice, facts), "Keep", Icons.Note, tall: false) is { } kept
                    && Identifier().IsMatch(kept.Trim()))
                    Play?.Invoke(StepMaker.Keep(choice, facts, kept.Trim()), true);
                break;
            case PickAction.CheckShows:
                AddCheck(new JsonObject { ["shows"] = choice.Json.DeepClone() }, $"{choice.Shown} shows");
                break;
            case PickAction.CheckText:
            {
                var name = Variable(choice, facts);
                Play?.Invoke(StepMaker.Keep(choice, facts, name), true);
                AddCheck(new JsonObject { ["equals"] = $"${{{name}}}", ["to"] = text }, $"{name} is \"{text}\"");
                break;
            }
            case PickAction.Name:
                NameIt(choice);
                break;
        }
        if (action is not PickAction.Name)
            ClosePicked();
    }

    private void NameIt(TargetChoice choice)
    {
        var suggestion = Camel(_current?.Facts.Label ?? "control");
        if (Ui.Prompt(this, "Name it", "A name for it in elements.json, which steps use instead of how it is found now:", suggestion, "Name it", Icons.Name, tall: false) is not { } name)
            return;
        name = name.Trim();
        try
        {
            if (!Identifier().IsMatch(name))
                throw new ArgumentException("A name is letters and digits, such as addOrder.");
            var named = _draft.NameControl(choice, name, _suite.Elements);
            foreach (var (step, row) in _rows)
                if (step.Target == named)
                    Refresh(step, row);
            if (_current is { } current)
                ShowPicked(current.Facts, named, "");
            foreach (var check in _checks.Children.OfType<FrameworkElement>())
                if (check.Tag is JsonObject json && json["shows"] is JsonValue)
                    ((TextBlock)((DockPanel)check).Children[^1]).Text = $"{name} shows";
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Name it", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // A name for a kept value: the element's, else a word or two of what it says, not taken by another step.
    private string Variable(TargetChoice choice, ControlFacts facts)
    {
        var name = choice.Json is JsonValue element ? element.GetValue<string>() : Camel(Regex.Replace(facts.Label, @"[^A-Za-z ]", " ").Trim() is { Length: > 0 } words ? words : "kept");
        var taken = _draft.Steps.Select(step => step.Json["as"]?.GetValue<string>()).OfType<string>().ToHashSet();
        var unique = name;
        for (var copy = 2; taken.Contains(unique); copy++)
            unique = $"{name}{copy}";
        return unique;
    }

    private static string Camel(string text)
    {
        var words = Regex.Matches(text, "[A-Za-z0-9]+").Select(match => match.Value).Take(3).ToList();
        if (words.Count == 0)
            return "control";
        var name = string.Concat(words.Select((word, index) => index == 0 ? word.ToLowerInvariant() : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
        return char.IsDigit(name[0]) ? "control" + name : name;
    }

    private void AddCheck(JsonObject check, string label)
    {
        _draft.Checks.Add(check);
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Tag = check };
        var remove = Ui.IconButton(Icons.Cross, "Take this check out", () =>
        {
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
        var row = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 5, 4, 5), Margin = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Tag = step };
        _rows[step] = row;
        Refresh(step, row);
        row.MouseEnter += (_, _) =>
        {
            if (row.Child is Grid grid)
                grid.Children[^1].Visibility = Visibility.Visible;
            if (row.Background == Brushes.Transparent)
                row.Background = Ui.Surface;
            if (step.Target is not null && Mode != BuildMode.Picking)
                Locate?.Invoke(step);
        };
        row.MouseLeave += (_, _) =>
        {
            if (row.Child is Grid grid)
                grid.Children[^1].Visibility = Visibility.Hidden;
            if (row.Background == Ui.Surface)
                row.Background = Brushes.Transparent;
            if (Mode != BuildMode.Picking)
                Locate?.Invoke(null);
        };
        return row;
    }

    // Draws a step's row: its number and icon, what it says (click to change it), how it finds its control, and
    // its buttons, which show while the mouse is over it.
    private void Refresh(DraftStep step, Border row)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = Ui.Words($"{_draft.Steps.IndexOf(step) + 1}", 11, Ui.Faint, wrap: false);
        number.VerticalAlignment = VerticalAlignment.Top;
        number.Margin = new Thickness(0, 2, 0, 0);
        var icon = Ui.Glyph(step.Waits ? Icons.Wait : Icons.Of(step.Kind), 14, Ui.Accent);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        var words = new StackPanel();
        var say = Ui.Words(step.Text, 13, Ui.Ink);
        say.Cursor = Cursors.IBeam;
        say.ToolTip = "Click to change what the panel says for this step";
        say.MouseLeftButtonUp += (_, _) => EditSay(step, words, say);
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
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Hidden };
        foreach (var (glyph, tip, act) in new (string, string, Action)[]
        {
            (Icons.Play, "Try this step in the app", () => Play?.Invoke(step, false)),
            (Icons.Up, "Move it up", () => Move(step, -1)),
            (Icons.Down, "Move it down", () => Move(step, 1)),
            (Icons.Delete, "Take this step out", () => Remove(step)),
        })
        {
            var button = Ui.IconButton(glyph, tip, act, 12);
            button.Padding = new Thickness(5, 3, 5, 3);
            buttons.Children.Add(button);
        }
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(words, 2);
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(number);
        grid.Children.Add(icon);
        grid.Children.Add(words);
        grid.Children.Add(buttons);
        row.Child = grid;
    }

    // What a step without a control shows under it: the message box it answers, the file it picks, the wait.
    private static string? Detail(DraftStep step) => step.Verb switch
    {
        "answerDialog" when step.Json["expect"] is { } expect => $"{step.Json["answerDialog"]}: \"{expect}…\"",
        "answerDialog" => $"{step.Json["answerDialog"]}",
        "pickFile" or "pickFolder" => $"{step.Json["path"]}",
        "call" => $"{step.Json["call"]}, a macro",
        _ => null,
    };

    private void EditSay(DraftStep step, StackPanel words, TextBlock say)
    {
        var box = new TextBox { Text = step.Text, FontSize = 13, Padding = new Thickness(2, 1, 2, 1) };
        var index = words.Children.IndexOf(say);
        words.Children[index] = box;
        void Done(bool keep)
        {
            if (!words.Children.Contains(box))
                return;
            if (keep && box.Text.Trim().Length > 0)
                step.Text = box.Text.Trim();
            say.Text = step.Text;
            words.Children[words.Children.IndexOf(box)] = say;
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
        var row = _rows[step];
        _steps.Children.Remove(row);
        _steps.Children.Insert(to, row);
        Renumber();
    }

    private void Remove(DraftStep step)
    {
        lock (_draft.Steps)
            _draft.Steps.Remove(step);
        _steps.Children.Remove(_rows[step]);
        _rows.Remove(step);
        Locate?.Invoke(null);
        Renumber();
        UpdateCount();
    }

    private void Renumber()
    {
        foreach (var (step, row) in _rows)
            if (row.Child is Grid grid && grid.Children[0] is TextBlock number)
                number.Text = $"{_draft.Steps.IndexOf(step) + 1}";
    }

    private void UpdateCount()
    {
        _count.Text = _draft.Steps.Count == 1 ? "1 step" : $"{_draft.Steps.Count} steps";
        _save.IsEnabled = !_saved && _draft.Steps.Count > 0;
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
        if (_draft.AddingTo is null && _draft.Name.Length == 0)
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
        SetMode(BuildMode.Paused);
        _record.IsEnabled = _pick.IsEnabled = false;
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
            ? Card(Ui.Pass, Ui.PassSoft, Icons.Passed, $"Saved in {relative}", "The suite has no mistakes. Run it from the launcher to see it play.", [open, done])
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

    // A big toggle: an icon over a word.
    private static ToggleButton Tile(string glyph, string text, string tip, Brush? color)
    {
        var stack = new StackPanel { MinWidth = 56 };
        var icon = Ui.Glyph(glyph, 18, color);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock { Text = text, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        return new ToggleButton { Content = stack, ToolTip = tip, Style = (Style)Ui.Theme["ToggleIcon"], Padding = new Thickness(10, 6, 10, 5), Margin = new Thickness(0, 0, 4, 0), IsEnabled = false };
    }

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

    [GeneratedRegex(@"^[A-Za-z_]\w*$")]
    private static partial Regex Identifier();

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);
}
