using System.Globalization;
using System.Text.RegularExpressions;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// The steps a person does themselves in a manual run. The run never moves the mouse or presses a key: it outlines the
/// control, says what to do, and carries on once the app shows it was done, by the control's state where it has one,
/// such as the text in a box or a tick, else by a click or key of the person's. Anything else they do meanwhile is
/// theirs: the run goes on waiting rather than failing. Each method gives false when the run isn't manual, or stops
/// being manual while it waits, so the runner does the step itself.
/// </summary>
public sealed partial class Runner
{
    // How often a manual run looks at the app, and outlines what to use again.
    private const int LookEvery = 200, OutlineEvery = 1000;

    // Text has to stay what the step wants this long, so a box isn't taken half way through typing.
    private const int Settled = 600;

    private const int DoubleClickTime = 500;

    private bool ClickByHand(Activity line, Target target, ClickHow how, bool tab)
    {
        var followed = new Followed(target);
        bool selected = false, focused = false;
        long? lastLeft = null;
        var hint = tab ? "Open the outlined tab in the app."
            : how switch
            {
                ClickHow.Right => "Right-click the outlined control in the app.",
                ClickHow.Double => "Double-click the outlined control in the app.",
                _ => "Click the outlined control in the app.",
            };
        return ByHand(line, hint, () =>
        {
            var control = followed.Look(_driver);
            selected = tab && control is { IsSelected: true };
            focused = control is { HasFocus: true };
            return control;
        }, events =>
        {
            if (selected)
                return true;
            // Space or Enter on a button that has the keyboard clicks it too.
            if (how is ClickHow.Invoke or ClickHow.Mouse && focused && followed.Enabled && events.Any(happened => happened.Keys is "SPACE" or "ENTER"))
                return true;
            foreach (var happened in events.Where(happened => followed.Enabled && followed.Hit(happened)))
            {
                if (how == ClickHow.Right ? happened.Kind == PersonEventKind.RightDown : happened.Kind == PersonEventKind.LeftDown)
                {
                    if (how != ClickHow.Double || happened.At - lastLeft <= DoubleClickTime)
                        return true;
                    lastLeft = happened.At;
                }
            }
            return false;
        });
    }

    // The button goes down on one control and up on the other.
    private bool DragByHand(Activity line, Target from, Target to)
    {
        var (start, end) = (new Followed(from), new Followed(to));
        var held = false;
        return ByHand(line, "Drag the outlined control onto the other one.", () =>
        {
            end.Look(_driver);
            return held && end.Control is not null ? end.Control : start.Look(_driver);
        }, events =>
        {
            foreach (var happened in events)
            {
                if (happened.Kind == PersonEventKind.LeftDown)
                    held = start.Hit(happened);
                else if (happened.Kind == PersonEventKind.LeftUp && held && end.Hit(happened))
                    return true;
                else if (happened.Kind == PersonEventKind.LeftUp)
                    held = false;
            }
            return false;
        });
    }

    // Outlines the deepest item of the path that shows, or the control the menu opens on, and waits for a click on the
    // last item.
    private bool MenuByHand(Activity line, Target? on, IReadOnlyList<string> path)
    {
        var opener = on is null ? null : new Followed(on);
        ScreenRect? last = null;
        return ByHand(line, on is null ? "Open the menu, and pick the outlined items in turn." : "Right-click the outlined control, and pick the outlined items in turn.", () =>
        {
            var shown = _driver.MenuItems();
            for (var index = path.Count - 1; index >= 0; index--)
            {
                if (shown.FirstOrDefault(item => item.Name == path[index]) is not { } item)
                    continue;
                if (index == path.Count - 1 && item.Bounds is { } bounds)
                    last = bounds;
                return item;
            }
            return opener?.Look(_driver);
        }, events => last is { } item && events.Any(happened => happened.Kind == PersonEventKind.LeftDown && item.Contains(happened.X, happened.Y)));
    }

    // Outlines the deepest item of the path that shows, and waits until the last is picked. Gives it, or null when the
    // runner should expand the items itself.
    private IControl? ExpandByHand(Activity line, Target? parent, IReadOnlyList<string> path)
    {
        var targets = new List<Target>();
        foreach (var name in path)
            targets.Add(Target.Of(new Selector(Type: "TreeItem", Name: name, Within: targets.Count == 0 ? parent : targets[^1])));
        IControl? picked = null;
        var done = ByHand(line, "Open the outlined items in turn, and pick the last.", () =>
        {
            for (var index = targets.Count - 1; index >= 0; index--)
            {
                if (_driver.TryFind(targets[index]) is not { } item)
                    continue;
                picked = index == targets.Count - 1 && item.IsSelected ? item : null;
                return item;
            }
            return null;
        }, _ => picked is not null);
        return done ? picked : null;
    }

    private bool TypeByHand(Activity line, Target target, string text, bool secret)
    {
        // A password box won't say what it holds: it is done once the person leaves it, or presses Enter in it.
        if (secret)
            return SecretByHand(line with { Parts = [.. line.Parts.Select(part => part.Kind == PartKind.Value ? Part.Value(text) : part)] }, target);
        return Holds(line, target, "Type this in the outlined box. It carries on once the box holds just this, however it got there.",
            control => control.Value?.ReplaceLineEndings() == text.ReplaceLineEndings());
    }

    private bool SecretByHand(Activity line, Target target)
    {
        var followed = new Followed(target);
        bool hadFocus = false, focused = false;
        return ByHand(line, "Type this in the outlined box, then press Tab or Enter.", () =>
        {
            var control = followed.Look(_driver);
            focused = control is { HasFocus: true };
            hadFocus |= focused;
            return control;
        }, events => hadFocus && (!focused || events.Any(happened => happened.Keys is "TAB" or "ENTER")));
    }

    private bool SetValueByHand(Activity line, Target target, string value) =>
        Holds(line, target, "Set the outlined control to this.", control => SameValue(_driver.ValueOf(control), value));

    // A number is close enough when it rounds to the one wanted, as a slider is hard to put on the exact value.
    private static bool SameValue(string? found, string wanted)
    {
        if (found is null)
            return false;
        if (!double.TryParse(wanted, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.TryParse(found, NumberStyles.Float, CultureInfo.InvariantCulture, out var near))
            return found == wanted;
        var places = wanted.Contains('.', StringComparison.Ordinal) ? wanted.Length - wanted.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
        return Math.Round(near, places) == Math.Round(number, places);
    }

    private bool ChooseByHand(Activity line, Target target, string option)
    {
        // While the drop-down is open, its option is what to outline.
        var item = Target.Of(new Selector(Type: "ListItem", Name: option, Within: target));
        var followed = new Followed(target);
        var picked = false;
        return ByHand(line, "Pick this in the outlined drop-down.", () =>
        {
            var control = followed.Look(_driver);
            picked = control?.Selection == option;
            return picked ? control : _driver.TryFind(item) ?? control;
        }, _ => picked);
    }

    private bool TickByHand(Activity line, Target target, bool isChecked) =>
        Holds(line, target, isChecked ? "Tick the outlined box." : "Clear the outlined box.", control => control.IsChecked == isChecked, settle: 0);

    private bool HoverByHand(Activity line, Target target)
    {
        var followed = new Followed(target);
        return ByHand(line, "Point at the outlined control with the mouse.", () => followed.Look(_driver),
            _ => followed.Bounds is { } bounds && bounds.Contains(Control.Person.Mouse.X, Control.Person.Mouse.Y));
    }

    // Whether the text had a match to replace, once the person has made the change; null when the runner should.
    private bool? ReplaceByHand(Activity line, Target target, IControl control, string pattern, string replacement)
    {
        if (!Control.Manual)
            return null;
        var regex = new Regex(pattern, RegexOptions.Multiline);
        var text = control.Value ?? "";
        if (!regex.IsMatch(text))
            return false;
        var wanted = regex.Replace(text, replacement, 1);
        return Holds(line, target, $"Change the text in the outlined box to:\n{wanted}", found => found.Value == wanted) ? true : null;
    }

    private bool KeyByHand(Activity line, string keys) =>
        ByHand(line, "Press this in the app.", () => null, events => events.Any(happened => happened.Keys is { } pressed && KeyNames.Same(pressed, keys)));

    // A message box is done once it closes. Closed with another of its buttons, the step fails and the case carries on.
    private bool AnswerByHand(string title, DialogButton button)
    {
        var window = Target.Of(new Selector(Type: "Window", Name: title));
        var followed = new Followed(new Target([new Selector(Id: ((int)button).ToString(CultureInfo.InvariantCulture), Type: "Button", Within: window)], $"the {button} button"));
        bool open = true, focused = false, pressed = false;
        var line = Activity.Of(ActivityKind.Dialog, Part.Verb("Press"), Part.Key(button.ToString()), Part.Word("in"), Part.Window(title));
        var done = ByHand(line, "Press the outlined button.", () =>
        {
            var control = followed.Look(_driver);
            open = control is not null || _driver.TryFind(window) is not null;
            focused = control is { HasFocus: true };
            return control;
        }, events =>
        {
            pressed |= events.Any(happened => happened.Kind == PersonEventKind.LeftDown && followed.Hit(happened)
                || focused && happened.Keys is "ENTER" or "SPACE");
            return !open;
        });
        if (done && !pressed)
            FailStep(new StepFailure($"The \"{title}\" message was closed another way than with {button}", button.ToString(), "another button"));
        return done;
    }

    // A file picker is done once it closes. With another file in its name box, the step fails and the case carries on.
    private bool PickByHand(Activity line, string title, string path, bool folder)
    {
        if (!Control.Manual)
            return false;
        _driver.WaitDialog(title, Control.Scale(TimeSpan.FromSeconds(30)));
        var window = Target.Of(new Selector(Type: "Window", Name: title));
        var box = new Followed(new Target([new Selector(Id: folder ? "1152" : "1148", Type: "Edit", Within: window)], "the name box"));
        var open = new Target([new Selector(Id: "1", Type: "Button", Within: window)], "Open");
        string? first = null, named = null;
        var showing = true;
        var done = ByHand(line, $"Pick {path} in the picker, by browsing to it or typing it in the outlined box, then open it.", () =>
        {
            var control = box.Look(_driver);
            showing = control is not null || _driver.TryFind(window) is not null;
            if (control is null)
                return null;
            named = control.Value;
            first ??= named;
            return SamePick(named, path) ? _driver.TryFind(open) ?? control : control;
        }, _ => !showing);
        // A file opened with a double-click can close the picker between two looks, so a name box that never changed
        // says nothing about what was picked.
        if (done && named != first && !SamePick(named, path))
            FailStep(new StepFailure($"{(string.IsNullOrEmpty(named) ? "Nothing" : named)} was picked in {title}, not {path}", path, named ?? ""));
        return done;
    }

    // What a picker's name box says names the path: by its whole path, or by the name of its file or folder.
    private static bool SamePick(string? named, string path)
    {
        if (string.IsNullOrWhiteSpace(named))
            return false;
        static string Last(string text) => Path.GetFileName(text.Trim().Trim('"').TrimEnd('\\', '/'));
        return string.Equals(named.Trim().Trim('"'), path, StringComparison.OrdinalIgnoreCase) || string.Equals(Last(named), Last(path), StringComparison.OrdinalIgnoreCase);
    }

    // Done once the control has been as the step wants for a moment.
    private bool Holds(Activity line, Target target, string hint, Func<IControl, bool> wanted, int settle = Settled)
    {
        var followed = new Followed(target);
        long since = 0;
        var holds = false;
        return ByHand(line, hint, () =>
        {
            var control = followed.Look(_driver);
            var now = control is not null && wanted(control);
            if (now && !holds)
                since = Environment.TickCount64;
            holds = now;
            return control;
        }, _ => holds && Environment.TickCount64 - since >= settle);
    }

    /// <summary>
    /// The person's turn: says what to do, then looks at the app every so often and outlines what look gives, until
    /// done says it is done, given what the person did since the last look. Nothing else they do ends the wait, and it
    /// has no time limit. A pause or stop works as ever. False when the run isn't manual, or stops being manual.
    /// </summary>
    private bool ByHand(Activity line, string hint, Func<IControl?> look, Func<IReadOnlyList<PersonEvent>, bool> done)
    {
        if (!Control.Manual)
            return false;
        _view.YourTurn(line with { Detail = hint });
        var started = Environment.TickCount64;
        try
        {
            _ = Control.Person.Take();
            var shown = Look(look);
            if (shown is not null)
                Read(() => _driver.Point(shown, line.Text));
            var outlined = Environment.TickCount64;
            while (true)
            {
                Control.Wait(LookEvery);
                if (!Control.Manual)
                    return false;
                // What the person did is weighed against the app as it was before they did it, as a click can take
                // its control away.
                if (done(Control.Person.Take()))
                {
                    _results.Log("MANUAL", $"{line.Text}: done ({(Environment.TickCount64 - started) / 1000.0:0.0} s)");
                    return true;
                }
                shown = Look(look);
                if (shown is not null && Environment.TickCount64 - outlined >= OutlineEvery)
                {
                    Read(() => _driver.Outline(shown, line.Text));
                    outlined = Environment.TickCount64;
                }
            }
        }
        finally
        {
            _view.YourTurn(null);
        }
    }

    // The app can change under a look, as the person uses it: a control that goes as it is read wasn't seen.
    private static IControl? Look(Func<IControl?> look) => Read(look);

    private static T? Read<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not (StopRunException or StuckException or ScriptException))
        {
            return default;
        }
    }

    private static void Read(Action read) => Read<object>(() =>
    {
        read();
        return null;
    });

    /// <summary>
    /// A control the person is to use, found again at each look, as it may be built again or scrolled. Where it was
    /// last seen, and whether it could be used then, stay when it goes, so a click that takes it away still counts.
    /// </summary>
    private sealed class Followed(Target target)
    {
        public IControl? Control { get; private set; }

        public ScreenRect? Bounds { get; private set; }

        public bool Enabled { get; private set; }

        public IControl? Look(IDriver driver)
        {
            Control = driver.TryFind(target);
            if (Control is { } control && control.Bounds is { } bounds)
                (Bounds, Enabled) = (bounds, control.IsEnabled);
            return Control;
        }

        public bool Hit(PersonEvent happened) => Bounds is { } bounds && bounds.Contains(happened.X, happened.Y);
    }
}
