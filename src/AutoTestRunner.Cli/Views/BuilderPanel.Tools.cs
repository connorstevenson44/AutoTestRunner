using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTestRunner.Building;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// The builder panel's forms: a step of any kind from the toolbar, a check of any kind, a step's details folded out
/// under it, the kept values to put in a box, and what can be done with steps picked with Shift+click: a macro made of
/// them, or a repeat or an if put round them.
/// </summary>
internal sealed partial class BuilderPanel
{
    // ===== New steps and checks =====

    // A step of any kind, from its form, which starts as given or empty: it is played, and added when it works.
    private void NewStep(string kind, string title, string glyph, JsonObject? start = null)
    {
        start ??= new JsonObject { [kind] = ScriptVocabulary.Steps[kind].Takes == Takes.List ? new JsonArray() : "" };
        if (FormWindow.Ask(this, title, glyph, Forms.ForStep(kind, _suite), kind, start, _suite, ValuesNow, PickFor, add: "Do it") is { } step)
            Play?.Invoke(StepMaker.FromJson(step, DraftStepState.Played), true);
    }

    // A check of any kind, from its form, which can try it against the app first; target is a control picked for it.
    private void NewCheck(CheckKind kind, JsonNode? target = null)
    {
        var start = Forms.NewCheck(kind.Key);
        if (target is not null)
            start[kind.Key] = target.DeepClone();
        if (FormWindow.Ask(this, kind.Title, CheckMenu.Glyph(kind.Group), Forms.ForCheck(kind.Key, _suite), kind.Key, start, _suite, ValuesNow, PickFor, TryCheck) is { } check)
            AddCheck(check, check["label"]?.ToString() ?? Describe(kind, check));
    }

    // What the checkpoint list says for a check: what it is, then what it is of.
    private static string Describe(CheckKind kind, JsonObject check) =>
        check[kind.Key] is JsonValue value && value.ToString() is { Length: > 0 } subject ? $"{kind.Title}: {subject}"
        : check[kind.Key] is JsonObject or JsonArray ? $"{kind.Title}: {JsonLine.Write(check[kind.Key])}"
        : kind.Title;

    // Picks a control in the app for a form: picking starts, and the next control clicked goes to the form.
    private void PickFor(Action<TargetChoice> then)
    {
        _pickInto = then;
        ShowMode();
    }

    private Task<CheckResult> TryCheck(JsonObject check)
    {
        var answer = new TaskCompletionSource<CheckResult>();
        if (Try is null || !_ready)
            answer.SetResult(CheckResult.Unmade("The app isn't ready yet."));
        else
            Try(check, result => answer.TrySetResult(result));
        return answer.Task;
    }

    // ===== Kept values =====

    // The values the steps played so far kept, and the suite's variables, with what each holds now; and the names
    // later steps keep, which hold nothing yet.
    private IReadOnlyList<(string Name, string Text)> ValuesNow()
    {
        var values = Values.ToList();
        List<string> kept;
        lock (_draft.Steps)
            kept = [.. _draft.Steps.Select(step => step.Json["as"]?.ToString()).OfType<string>()];
        foreach (var name in kept.Distinct().Where(name => values.All(value => value.Name != name)))
            values.Add((name, "kept by a step not played yet"));
        // The steps' own values first: they are what a check is usually of.
        return [.. values.OrderBy(value => kept.Contains(value.Name) ? 0 : 1)];
    }

    // The list of values under the steps heading: one clicked goes in the box typed in last, or onto the clipboard.
    private void ShowValues() => Ui.ValuesMenu(_count, ValuesNow(), text =>
    {
        if (_lastBox is { IsVisible: true } box)
            Ui.InsertAtCaret(box, text);
        else
        {
            Clipboard.SetText(text);
            Note($"Copied {text}. Paste it into a box.");
        }
    });

    // ===== A step's details =====

    private void Fold(DraftStep step)
    {
        if (!_open.Remove(step))
            _open.Add(step);
        Refresh(step);
    }

    // The step's form, folded out under it, with Apply and Cancel.
    private Border Details(DraftStep step)
    {
        var form = new FormView(Forms.ForStep(step.Verb, _suite), step.Json, step.Verb, _suite, ValuesNow, PickFor);
        var problem = Ui.Words("", 11.5, Ui.Fail);
        problem.Visibility = Visibility.Collapsed;
        var stack = new StackPanel();
        stack.Children.Add(form);
        stack.Children.Add(problem);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var apply = Ui.LabelledButton(Icons.Tick, "Apply", () =>
        {
            var changed = step.Json.DeepClone().AsObject();
            try
            {
                form.WriteTo(changed);
            }
            catch (FormatException ex)
            {
                (problem.Text, problem.Visibility) = (ex.Message, Visibility.Visible);
                return;
            }
            Change(step, changed);
            _open.Remove(step);
            Refresh(step);
        });
        apply.ToolTip = "Keep the changes. A step already played isn't played again: Roll back to here plays it as it is now.";
        buttons.Children.Add(apply);
        buttons.Children.Add(Ui.LabelledButton(Icons.Cross, "Cancel", () => Fold(step)));
        stack.Children.Add(buttons);
        return new Border
        {
            Child = stack, Margin = new Thickness(20, 6, 6, 4), Padding = new Thickness(10, 2, 10, 8), Background = Brushes.White,
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
        };
    }

    // Gives a step new JSON, keeping it the same step in the list, and how it finds its control.
    private void Change(DraftStep step, JsonObject changed)
    {
        lock (_draft.Steps)
        {
            step.Json.Clear();
            foreach (var (key, value) in changed)
                step.Json[key] = value?.DeepClone();
            step.Target = StepMaker.FromJson(step.Json, step.State).Target;
        }
        _changes++;
    }

    private void SwitchOff(DraftStep step, bool off)
    {
        var changed = step.Json.DeepClone().AsObject();
        if (off)
            changed["disabled"] = true;
        else
            changed.Remove("disabled");
        Change(step, changed);
        Refresh(step);
    }

    /// <summary>Folds out a step's details and picks steps, as clicking them does: for the preview's pictures.</summary>
    internal void Show(DraftStep? folded, IReadOnlyList<DraftStep> picked)
    {
        if (folded is not null && !_open.Contains(folded))
            Fold(folded);
        _selected.Clear();
        _selected.AddRange(picked);
        Refresh();
        ShowSelection();
    }

    /// <summary>For preview-ui: the steps from this one marked as the case's reset, with nothing folded out or picked.</summary>
    internal void ShowReset(DraftStep from)
    {
        _open.Clear();
        _selected.Clear();
        MarkReset(from, true);
        ShowSelection();
    }

    // ===== Steps picked with Shift+click =====

    private void Select(DraftStep step, bool range)
    {
        List<DraftStep> steps;
        lock (_draft.Steps)
            steps = [.. _draft.Steps];
        if (range && _anchor is { } anchor && steps.IndexOf(anchor) is >= 0 and var from)
        {
            var to = steps.IndexOf(step);
            _selected.Clear();
            _selected.AddRange(steps.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1));
        }
        else
        {
            if (!_selected.Remove(step))
                _selected.Add(step);
            _anchor = step;
        }
        Refresh();
        ShowSelection();
    }

    private void Deselect(DraftStep step)
    {
        if (_selected.Remove(step))
            ShowSelection();
        if (_anchor == step)
            _anchor = null;
        _open.Remove(step);
    }

    private void ClearSelection()
    {
        _selected.Clear();
        _anchor = null;
        Refresh();
        ShowSelection();
    }

    // The bar over the steps while some are picked: how many, and what can be done with them.
    private void ShowSelection()
    {
        if (_selected.Count == 0)
        {
            _selection.Content = null;
            return;
        }
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 4, 4) };
        var count = Ui.Pill(Ui.Words(_selected.Count == 1 ? "1 step picked" : $"{_selected.Count} steps picked", 11.5, Ui.Accent, FontWeights.SemiBold, wrap: false), Ui.AccentSoft, 8, new Thickness(8, 3, 8, 3));
        count.Margin = new Thickness(0, 0, 6, 6);
        bar.Children.Add(count);
        Button Action(string glyph, string text, string tip, Action click)
        {
            var button = Ui.LabelledButton(glyph, text, click);
            button.ToolTip = tip;
            button.Padding = new Thickness(8, 3, 9, 3);
            bar.Children.Add(button);
            return button;
        }
        Action(Icons.Macro, "Make a macro", "Make a macro of these steps, in macros/, and use it here instead. Values such as what is typed become its parameters.", MakeMacro);
        Action(Icons.Repeat, "Repeat", "Run these steps for each of a list of values, or each row of a data file", WrapInRepeat);
        Button? onlyIf = null;
        onlyIf = Action(Icons.Branch, "Only if…", "Run these steps only when a check passes", () => CheckMenu.Open(onlyIf!, WrapInIf));
        Action(Icons.Cross, "Clear", "Unpick the steps", ClearSelection);
        _selection.Content = bar;
    }

    // The steps picked, in order, when they are next to each other; else null, having said why.
    private List<DraftStep>? Picked()
    {
        List<DraftStep> steps;
        List<int> places;
        lock (_draft.Steps)
        {
            steps = [.. _draft.Steps.Where(_selected.Contains)];
            places = [.. steps.Select(step => _draft.Steps.IndexOf(step))];
        }
        if (places.Count > 0 && places[^1] - places[0] == places.Count - 1)
            return steps;
        MessageBox.Show(this, "Pick steps that are next to each other: Shift+click the first and the last.", "Steps", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void MakeMacro()
    {
        if (Picked() is not { } steps)
            return;
        var taken = _suite.Macros.Keys.Concat(_draft.NewMacros.Keys).ToHashSet(StringComparer.Ordinal);
        var json = steps.Select(step => step.Json).ToList();
        if (MacroWindow.Ask(this, MacroMaker.Values(json), taken) is not { } made)
            return;
        var (macro, call) = MacroMaker.Make(made.Name, made.Description, json, made.Parameters);
        _draft.NewMacros[made.Name] = macro;
        MadeMacro?.Invoke(Macro.From(made.Name, $"macros/{made.Name}.json", macro));
        Replace(steps, call);
        Note($"Made {made.Name}. It is saved in macros/ with the case, and can be used in other cases from the macro tool.");
    }

    private void WrapInRepeat()
    {
        if (Picked() is not { } steps)
            return;
        FormField[] fields =
        [
            new("items", "For each of these values, one to a line", FieldType.Lines),
            new("rows", "Or each row of a data file, CSV or JSON", FieldType.File),
            new("item", "Call each", FieldType.Text),
        ];
        if (FormWindow.Ask(this, "Repeat the steps", Icons.Repeat, fields, "item", new JsonObject { ["item"] = "item" }, _suite, ValuesNow, null, add: "Repeat") is not { } answer)
            return;
        var items = (answer["items"] as JsonArray ?? []).Select(item => item!.ToString()).ToList();
        var rows = answer["rows"]?.ToString();
        if (items.Count == 0 && rows is null)
            return;
        // A data file in the suite is written from its folder, as repeat reads it.
        if (rows?.StartsWith("${suiteDir}\\", StringComparison.Ordinal) == true)
            rows = rows["${suiteDir}\\".Length..].Replace('\\', '/');
        Replace(steps, StepWrapper.Repeat([.. steps.Select(step => step.Json)], items, rows, answer["item"]?.ToString()));
    }

    private void WrapInIf(CheckKind kind)
    {
        if (Picked() is not { } steps)
            return;
        if (FormWindow.Ask(this, $"Only if: {kind.Title}", CheckMenu.Glyph(kind.Group), Forms.ForCheck(kind.Key, _suite), kind.Key, Forms.NewCheck(kind.Key), _suite,
            ValuesNow, PickFor, TryCheck, "Only if") is { } check)
            Replace(steps, StepWrapper.OnlyIf([.. steps.Select(step => step.Json)], check));
    }

    // Puts one step in the place of the steps picked: played when they all were.
    private void Replace(List<DraftStep> steps, JsonObject json)
    {
        var played = steps.All(step => step.State == DraftStepState.Played);
        var made = StepMaker.FromJson(json, played ? DraftStepState.Played : DraftStepState.NotPlayed);
        int at;
        lock (_draft.Steps)
        {
            at = _draft.Steps.IndexOf(steps[0]);
            _draft.Steps.RemoveRange(at, steps.Count);
            _draft.Steps.Insert(at, made);
        }
        foreach (var step in steps)
        {
            _steps.Children.Remove(_rows[step]);
            _rows.Remove(step);
            _open.Remove(step);
        }
        _steps.Children.Insert(at, StepRow(made));
        _changes++;
        ClearSelection();
        Renumber();
        UpdateCount();
    }

    // Takes the steps out of a repeat or an if, in its place.
    private void Unwrap(DraftStep step)
    {
        var inside = StepWrapper.Inside(step.Json).Select(json => StepMaker.FromJson(json.DeepClone().AsObject(), step.State)).ToList();
        int at;
        lock (_draft.Steps)
        {
            at = _draft.Steps.IndexOf(step);
            _draft.Steps.RemoveAt(at);
            _draft.Steps.InsertRange(at, inside);
        }
        Deselect(step);
        _steps.Children.Remove(_rows[step]);
        _rows.Remove(step);
        for (var index = 0; index < inside.Count; index++)
            _steps.Children.Insert(at + index, StepRow(inside[index]));
        _changes++;
        Renumber();
        UpdateCount();
    }
}
