using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Turns what the person does in the app into steps, as a person would write them: a click on a drop-down's item is
/// one choose, the text in a box is one type when they leave it, a click in a message box answers it. After a click
/// that changes the app's status text, it adds a wait for the new words, so the case waits for the app to finish.
/// </summary>
/// <param name="suite">The suite, for its status text and its waitForStatusBar macro.</param>
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

    private ControlFacts? _box;
    private string _before = "";
    private bool _mayChangeStatus;

    /// <summary>The box being typed in, if any.</summary>
    public ControlFacts? Box => _box;

    /// <summary>
    /// A click in the app, on the control under the mouse, read as the button went down: before the click took
    /// effect, so a tick box's click changes what it was then.
    /// </summary>
    public IReadOnlyList<DraftStep> Clicked(ControlFacts hit)
    {
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
            "CheckBox" => Add(StepMaker.Tick(name(control), control, control.Checked != true), changesStatus: true),
            "TabItem" when control.Name.Length > 0 => Add(StepMaker.Tab(control.Name), changesStatus: false),
            _ => Add(StepMaker.Click(name(control), control), changesStatus: true),
        };
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

    /// <summary>The app's status text changed: a wait for it after the step that changed it.</summary>
    public IReadOnlyList<DraftStep> StatusSaid(string text)
    {
        if (!_mayChangeStatus || StepMaker.StatusWait(suite, text) is not { } wait)
            return [];
        _mayChangeStatus = false;
        lock (draft.Steps)
            draft.Steps.Add(wait);
        return [wait];
    }

    /// <summary>Adds a step made some other way, such as from a picked control.</summary>
    public IReadOnlyList<DraftStep> Add(DraftStep step, bool changesStatus)
    {
        lock (draft.Steps)
            draft.Steps.Add(step);
        _mayChangeStatus = changesStatus;
        return [step];
    }

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
