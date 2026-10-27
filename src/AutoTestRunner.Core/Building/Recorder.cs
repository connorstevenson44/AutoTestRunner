using System.Text.Json.Nodes;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Turns what the person does in the app into steps, as a person would write them: a click on a drop-down's item is
/// one choose, the text in a box is one type when they leave it, a click in a message box answers it. After a click
/// that changes the text of a control the suite watches, such as the app's status text, it adds a wait for the new
/// words, so the case waits for the app to finish; and after a click on a control with an afterClick hook, the hook's
/// steps instead.
/// </summary>
/// <param name="suite">The suite, for the controls it watches, its hooks and its macros.</param>
/// <param name="draft">The case the steps go in.</param>
/// <param name="name">Picks how a step finds a control: TargetSuggester, asked of the app.</param>
public sealed class Recorder(Suite suite, CaseDraft draft, Func<ControlFacts, TargetChoice> name)
{
    // Controls that do something when clicked; a click on the text or picture inside one is a click on it.
    private static readonly HashSet<string> Clickable = new(
        ["Button", "CheckBox", "RadioButton", "ComboBox", "ListItem", "TabItem", "MenuItem", "Hyperlink", "TreeItem", "DataItem", "SplitButton", "Edit", "Document", "ScrollBar", "Thumb", "TitleBar"],
        StringComparer.OrdinalIgnoreCase);

    // Clicked on, these do nothing a step needs: typing is recorded as the box is left, scrolling is the runner's.
    private static readonly HashSet<string> NotSteps = new(["ComboBox", "Edit", "Document", "ScrollBar", "Thumb", "TitleBar"], StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _waited = new(StringComparer.Ordinal);
    private ControlFacts? _box;
    private string _before = "";
    private bool _mayChangeStatus;

    // A control right-clicked, and the items of its menu clicked since, which make one menu step.
    private (ControlFacts Control, List<string> Items)? _menu;

    /// <summary>The box being typed in, if any.</summary>
    public ControlFacts? Box => _box;

    /// <summary>
    /// A click in the app, on the control under the mouse, read as the button went down; a tick box's tick is read
    /// once the click took effect.
    /// </summary>
    public IReadOnlyList<DraftStep> Clicked(ControlFacts hit)
    {
        // An item of the menu a right-click opened is a step of the menu, not a click of its own.
        if (_menu is { } menu && Actionable(hit) is { Type: "MenuItem" } item)
        {
            menu.Items.Add(item.Name);
            return [];
        }
        if (hit.Dialog is { } dialog)
            return AddIf(Answer(hit, dialog));
        if (Actionable(hit) is not { } control)
            return [];
        if (string.Equals(control.Type, "ListItem", StringComparison.OrdinalIgnoreCase) && control.Nearest("ComboBox") is { } box)
            return Add(StepMaker.Choose(name(box), box, control.Name), changesStatus: true);
        if (NotSteps.Contains(control.Type))
            return [];
        return control.Type switch
        {
            "CheckBox" => Add(StepMaker.Tick(name(control), control, control.Checked ?? true), changesStatus: true),
            "TabItem" when control.Name.Length > 0 => Add(StepMaker.Tab(control.Name), changesStatus: false),
            _ => Add(StepMaker.Click(name(control), control), changesStatus: true),
        };
    }

    /// <summary>
    /// A right-click on a control: the items of its menu clicked next make a menu step on it, once something else
    /// happens or MenuDone is called; with no item clicked, it is a right-click.
    /// </summary>
    public IReadOnlyList<DraftStep> RightClicked(ControlFacts hit)
    {
        var pending = MenuDone();
        if (hit.Dialog is null && Actionable(hit) is { } control)
            _menu = (control, []);
        return pending;
    }

    /// <summary>Whether a right-click's menu step is still to be made.</summary>
    public bool MenuOpen => _menu is not null;

    /// <summary>Makes the step of a right-click and the menu items clicked since, if there was one.</summary>
    public IReadOnlyList<DraftStep> MenuDone()
    {
        if (_menu is not { } menu)
            return [];
        _menu = null;
        return Add(StepMaker.ContextMenu(name(menu.Control), menu.Control, menu.Items), changesStatus: true);
    }

    /// <summary>
    /// A second click on the control just clicked, quickly enough to be a double-click: the click step becomes a
    /// double-click. Returns the step changed, or null when the last step isn't a click on it.
    /// </summary>
    public DraftStep? Doubled(ControlFacts hit)
    {
        if (Actionable(hit) is not { } control)
            return null;
        lock (draft.Steps)
        {
            if (draft.Steps is not [.., { Verb: "click", Control: { } clicked } last] || clicked.Type != control.Type || clicked.Name != control.Name || clicked.Bounds != control.Bounds
                || last.Json.ContainsKey("how"))
                return null;
            last.Json["how"] = "double";
            if (last.Json["say"]?.GetValue<string>() is { } say && say.StartsWith("Click ", StringComparison.Ordinal))
                last.Json["say"] = "Double-click " + say["Click ".Length..];
            return last;
        }
    }

    /// <summary>The keyboard went into a box that can be typed in, holding what it holds now.</summary>
    public void Entered(ControlFacts box)
    {
        _box = box;
        _before = box.Value ?? "";
    }

    /// <summary>The keyboard left the box, or something else happened: a type step when its text changed.</summary>
    public IReadOnlyList<DraftStep> Left(string? value)
    {
        if (_box is not { } box)
            return [];
        _box = null;
        if (box.IsPassword ? value is null : value is null || value == _before)
            return [];
        return Add(StepMaker.Type(name(box), box, value ?? ""), changesStatus: false);
    }

    /// <summary>
    /// A key or shortcut pressed outside typing, such as Enter, Esc or Ctrl+S, with the control that had the keyboard.
    /// In a message box or file picker, Enter and Esc answer it, as its buttons do.
    /// </summary>
    public IReadOnlyList<DraftStep> Pressed(string keys, ControlFacts? focused = null)
    {
        if (focused?.Dialog is not { } dialog)
            return Add(StepMaker.Key(keys), changesStatus: true);
        return keys switch
        {
            "Esc" => Add(StepMaker.AnswerDialog(dialog, "Cancel"), changesStatus: true),
            "Enter" when dialog.IsPicker => AddIf(string.IsNullOrEmpty(dialog.Path) ? null : StepMaker.PickFile(dialog)),
            "Enter" => AddIf(Answer(focused, dialog)),
            _ => [],
        };
    }

    /// <summary>The controls watched: the suite's, and those the panel added, which take their place.</summary>
    public IReadOnlyList<WatchHook> Watched
    {
        get
        {
            lock (draft.NewWatch)
                return [.. suite.Watched().Where(hook => !draft.NewWatch.Any(added => added.Element == hook.Element)), .. draft.NewWatch];
        }
    }

    /// <summary>
    /// A watched control's text changed after a step that may have changed it, and kept still: a wait for the new
    /// words after that step, once for each control; and with the hook's check, a check of the words at the checkpoint.
    /// </summary>
    public IReadOnlyList<DraftStep> Changed(WatchHook hook, string text)
    {
        if (!_mayChangeStatus || _waited.Contains(hook.Element))
            return [];
        var keep = hook.Check ? draft.FreeName(hook.Element) : null;
        var steps = StepMaker.WatchWait(suite, hook, text, keep);
        if (steps.Count == 0)
            return [];
        _waited.Add(hook.Element);
        lock (draft.Steps)
            draft.Steps.AddRange(steps);
        if (keep is not null)
        {
            lock (draft.Checks)
                draft.Checks.Add(new JsonObject { ["equals"] = $"${{{keep}}}", ["to"] = text, ["label"] = $"{hook.Element} said \"{text}\"" });
        }
        return steps;
    }

    /// <summary>The app's status text changed: a wait for it after the step that changed it.</summary>
    public IReadOnlyList<DraftStep> StatusSaid(string text) =>
        suite.App.Status is { } status && Watched.FirstOrDefault(hook => hook.Element == status) is { } hook ? Changed(hook, text) : [];

    /// <summary>
    /// Adds a step made some other way, such as from a picked control. A click on a control with an afterClick hook
    /// is followed by the hook's steps, which stand for a wait for what the click changes.
    /// </summary>
    public IReadOnlyList<DraftStep> Add(DraftStep step, bool changesStatus)
    {
        // A right-click's menu step goes before whatever happens next.
        List<DraftStep> steps = _menu is not null && step.Verb != "menu" && !(step.Verb == "click" && step.Json["how"]?.GetValue<string>() == "right") ? [.. MenuDone(), step] : [step];
        if (step.Verb is "click" or "tick" or "choose" && ElementOf(step.Target?.Json) is { } element && AfterClick(element) is { } hook)
        {
            if (hook.Delay > 0)
                steps.Add(StepMaker.Wait(hook.Delay));
            var call = new JsonObject { ["call"] = hook.Call };
            if (hook.With is { Count: > 0 } with)
                call["with"] = with.DeepClone();
            steps.Add(new DraftStep(call, ActivityKind.Say));
            changesStatus = false;
        }
        lock (draft.Steps)
            draft.Steps.AddRange(steps);
        _mayChangeStatus = changesStatus;
        _waited.Clear();
        return steps;
    }

    private ClickHook? AfterClick(string element)
    {
        lock (draft.NewAfterClick)
            return draft.NewAfterClick.LastOrDefault(hook => hook.Element == element) ?? suite.AfterClick.FirstOrDefault(hook => hook.Element == element);
    }

    // The element a target names, written as its name or with its parameters.
    private static string? ElementOf(JsonNode? target) => target switch
    {
        JsonValue value when value.TryGetValue(out string? name) => name,
        JsonObject selector when selector.Count <= 2 && selector["element"] is JsonValue element => element.GetValue<string>(),
        _ => null,
    };

    // The control a click means: the one hit, or the button, item or tab around the text or picture that was hit.
    private static ControlFacts? Actionable(ControlFacts hit)
    {
        if (Clickable.Contains(hit.Type))
            return hit;
        for (var index = 0; index < Math.Min(4, hit.Ancestors.Count); index++)
            if (Clickable.Contains(hit.Ancestors[index].Type))
                return hit.Up(index);
        return null;
    }

    // A message box's button answers it; a file picker's Open picks what its name box says. Other clicks in them,
    // such as on a folder, are on the way to that.
    private static DraftStep? Answer(ControlFacts hit, DialogFacts dialog)
    {
        var button = string.Equals(hit.Type, "Button", StringComparison.OrdinalIgnoreCase) ? hit
            : hit.Nearest("Button", 2);
        if (button is null)
            return null;
        if (dialog.IsPicker)
            return button.Id == "1" && !string.IsNullOrEmpty(dialog.Path) ? StepMaker.PickFile(dialog) : null;
        var answer = button.Id switch
        {
            "1" => "OK",
            "2" => "Cancel",
            "6" => "Yes",
            "7" => "No",
            _ => null,
        };
        return answer is null ? null : StepMaker.AnswerDialog(dialog, answer);
    }

    private IReadOnlyList<DraftStep> AddIf(DraftStep? step) => step is null ? [] : Add(step, changesStatus: true);
}
