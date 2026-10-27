using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTestRunner.Building;

namespace AutoTestRunner.Cli.Views;

internal sealed partial class BuilderPanel
{
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
        var number = Ui.Words($"{index + 1}", 11, Ui.Faint, wrap: false);
        number.VerticalAlignment = VerticalAlignment.Top;
        number.Margin = new Thickness(0, 2, 0, 0);
        var open = _open.Contains(step);
        var chevron = Ui.IconButton(open ? Icons.Expand : Icons.Collapse, open ? "Fold the details away" : "Change this step's details", () => Fold(step), 9, new Thickness(3));
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
            buttons.Children.Add(Ui.IconButton(glyph, tip, act, 12, new Thickness(5, 3, 5, 3)));
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
        Refresh();
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
        int to;
        lock (_draft.Steps)
        {
            var index = _draft.Steps.IndexOf(step);
            to = index + by;
            if (index < 0 || to < 0 || to >= _draft.Steps.Count)
                return;
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
        List<DraftStep> steps;
        lock (_draft.Steps)
            steps = [.. _draft.Steps];
        if (steps.Any(step => step.Reset))
        {
            Refresh();
            return;
        }
        foreach (var (step, row) in _rows)
            if (Line(row)?.Children[0] is TextBlock number)
                number.Text = $"{steps.IndexOf(step) + 1}";
    }

    private void UpdateCount()
    {
        var count = _draft.Steps.Count;
        _count.Text = count == 1 ? "1 step" : $"{count} steps";
        _save.IsEnabled = !_saved && (count > 0 || _draft.Editing is not null);
    }

    private static void Mark(Border row, string? problem)
    {
        row.Background = problem is null ? Brushes.Transparent : Ui.FailSoft;
        row.BorderBrush = Ui.Fail;
        row.BorderThickness = new Thickness(problem is null ? 0 : 3, 0, 0, 0);
        row.ToolTip = problem;
    }
}
