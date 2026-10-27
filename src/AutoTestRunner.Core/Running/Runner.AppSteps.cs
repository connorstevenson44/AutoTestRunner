using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

public sealed partial class Runner
{
    // Steps that use the app's controls, menus, keys and dialogs. False when the step is another kind.
    private bool AppStep(StepAt at)
    {
        var (step, scope, kind, say) = (at.Step, at.Scope, at.Kind, at.Say);
        switch (kind)
        {
            case "say":
                Say(at.Text("say") ?? "");
                break;

            case "startApp":
                ShowStep(at.Line(ActivityKind.App, Part.Verb("Start"), new Part(PartKind.Window, _suite.App.Window, Exe)));
                _driver.Start(_suite.App, Exe);
                break;

            case "closeApp":
                ShowStep(at.Line(ActivityKind.App, Part.Verb("Close"), Part.Window(_suite.App.Window)));
                _driver.Close();
                break;

            case "click" when step["whileEnabled"]?.GetValue<bool>() == true:
                ClickWhileEnabled(at.TargetOf("click"), say, How(at.Text("how")));
                break;

            case "click":
            case "tab":
            {
                var target = kind == "tab" ? Target.Of(new Selector(Type: "TabItem", Name: at.Text("tab"))) : at.TargetOf("click");
                var control = at.Find(target);
                var how = How(at.Text("how"));
                var verb = kind == "tab" ? "Open" : how switch { ClickHow.Right => "Right-click", ClickHow.Double => "Double-click", _ => "Click" };
                var line = at.Line(ActivityKind.Click, Part.Verb(verb), Part.Control(target, control));
                ShowStep(line);
                Mark();
                if (!ClickByHand(line, target, how, tab: kind == "tab"))
                {
                    _driver.Point(control, line.Text);
                    if (!control.IsEnabled)
                        throw new StuckException(Lower(line.Text), "It's greyed out, so it can't be used yet.");
                    _driver.Click(control, how);
                }
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "drag":
            {
                var (from, to) = (at.TargetOf("drag"), at.TargetOf("to"));
                var control = at.Find(from);
                var onto = at.Find(to);
                var line = at.Line(ActivityKind.Point, Part.Verb("Drag"), Part.Control(from, control), Part.Word("onto"), Part.Control(to, onto));
                ShowStep(line);
                Mark();
                if (!DragByHand(line, from, to))
                {
                    _driver.Point(control, line.Text);
                    _driver.Drag(control, onto);
                }
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "menu":
            {
                var path = at.ListOf("menu");
                var items = path.SelectMany((item, index) => index == 0 ? [Part.Value(item)] : new[] { Part.Word("›"), Part.Value(item) });
                var on = step["on"] is null ? null : at.TargetOf("on");
                var opener = on is null ? null : at.Find(on);
                var line = on is null
                    ? at.Line(ActivityKind.Click, [Part.Verb("Menu"), .. items])
                    : at.Line(ActivityKind.Click, [Part.Verb("Right-click"), Part.Control(on, opener), Part.Word("›"), .. items]);
                ShowStep(line);
                Mark();
                if (!MenuByHand(line, on, path))
                {
                    if (opener is not null)
                    {
                        _driver.Point(opener, line.Text);
                        _driver.Click(opener, ClickHow.Right);
                        Control.Wait(300);
                    }
                    _driver.Menu(path);
                }
                Control.Wait(Control.Pace / 2);
                break;
            }

            case "expand":
            {
                var path = at.ListOf("expand");
                var line = at.Line(ActivityKind.Choose, Part.Verb("Expand"), Part.Value(string.Join(" › ", path)));
                ShowStep(line);
                var parent = step["in"] is null ? null : at.TargetOf("in");
                if (ExpandByHand(line, parent, path) is { } expanded)
                {
                    Control.Wait(Control.Pace / 2);
                    Keep(step, scope, expanded);
                    break;
                }
                IControl? item = null;
                for (var index = 0; index < path.Count; index++)
                {
                    var target = Target.Of(new Selector(Type: "TreeItem", Name: path[index], Within: parent));
                    item = at.Find(target);
                    _driver.Point(item, path[index]);
                    if (index < path.Count - 1)
                        _driver.Expand(item);
                    else
                        _driver.Click(item, ClickHow.Invoke);
                    Control.Wait(Control.Pace / 2);
                    parent = target;
                }
                if (item is not null)
                    Keep(step, scope, item);
                break;
            }

            case "setValue":
            {
                var target = at.TargetOf("setValue");
                var control = at.Find(target);
                var value = at.Required("to");
                var line = at.Line(ActivityKind.Type, Part.Verb("Set"), Part.Control(target, control), Part.Word("to"), Part.Value(value));
                UseControl(at, control, line, () => SetValueByHand(line, target, value), () => _driver.SetValue(control, value));
                break;
            }

            case "type":
            {
                var target = at.TargetOf("type");
                var control = at.Find(target);
                var value = at.Text("text") ?? "";
                var secret = step["secret"]?.GetValue<bool>() ?? false;
                var line = at.Line(ActivityKind.Type, Part.Verb("Type"), Part.Value(secret ? "••••••" : value), Part.Word("in"), Part.Control(target, control));
                UseControl(at, control, line, () => TypeByHand(line, target, value, secret), () => _driver.Type(control, value, secret));
                break;
            }

            case "choose":
            {
                var target = at.TargetOf("choose");
                var control = at.Find(target);
                var option = at.Text("option") ?? throw new ScriptException("choose needs the option to pick.");
                var line = at.Line(ActivityKind.Choose, Part.Verb("Pick"), Part.Value(option), Part.Word("in"), Part.Control(target, control));
                UseControl(at, control, line, () => ChooseByHand(line, target, option), () => _driver.Choose(control, option));
                break;
            }

            case "tick":
            {
                var target = at.TargetOf("tick");
                var control = at.Find(target);
                var isChecked = step["checked"]?.GetValue<bool>() ?? true;
                var line = at.Line(ActivityKind.Choose, Part.Verb(isChecked ? "Tick" : "Clear"), Part.Control(target, control));
                UseControl(at, control, line, () => TickByHand(line, target, isChecked), () => _driver.SetChecked(control, isChecked));
                break;
            }

            case "hover":
            {
                var target = at.TargetOf("hover");
                var control = at.Find(target);
                var line = at.Line(ActivityKind.Point, Part.Verb("Point at"), Part.Control(target, control));
                ShowStep(line);
                if (!HoverByHand(line, target))
                    _driver.Point(control, line.Text);
                Control.Wait(300);
                Keep(step, scope, control);
                break;
            }

            case "replaceText":
            {
                var target = at.TargetOf("replaceText");
                var control = at.Find(target);
                var pattern = at.Text("regex") ?? throw new ScriptException("replaceText needs a regex.");
                var replacement = at.Text("replacement") ?? "";
                var line = at.Line(ActivityKind.Type, Part.Verb("Replace"), Part.Value(pattern), Part.Word("with"), Part.Value(replacement), Part.Word("in"), Part.Control(target, control));
                ShowStep(line);
                var replaced = ReplaceByHand(line, target, control, pattern, replacement);
                if (replaced is null)
                    _driver.Point(control, line.Text);
                if (!(replaced ?? _driver.ReplaceText(control, pattern, replacement)))
                    FailStep(new StepFailure($"The text has nothing matching {pattern}, so I couldn't {Lower(line.Text)}", $"matches {pattern}", "no match"));
                Control.Wait(Control.Pace);
                break;
            }

            case "key":
            {
                var line = at.Line(ActivityKind.Key, Part.Verb("Press"), Part.Key(at.Text("key")!));
                ShowStep(line);
                if (!KeyByHand(line, at.Text("key")!))
                    _driver.Key(at.Text("key")!);
                Control.Wait(Control.Pace / 2);
                break;
            }

            case "answerDialog":
            {
                var title = at.Text("answerDialog")!;
                var button = Enum.Parse<DialogButton>(at.Text("button") ?? "OK", ignoreCase: true);
                _view.ShowWait(Activity.Of(ActivityKind.Wait, Part.Verb("Wait for"), Part.Window(title)));
                var message = _driver.WaitDialog(title, Control.Scale(at.Within(DialogTimeout)));
                ShowStep(at.Line(ActivityKind.Dialog, Part.Verb("Answer"), Part.Window(title), Part.Word("with"), Part.Key(button.ToString())) with { Detail = message });
                if (at.Text("expect") is { } expect && !message.Contains(expect, StringComparison.Ordinal))
                    FailStep(new StepFailure($"The \"{title}\" message was \"{message}\", expected \"{expect}\"", $"contains \"{expect}\"", message));
                Mark();
                if (!AnswerByHand(title, button))
                    _driver.PressDialogButton(title, button);
                Control.Wait(Control.Pace / 2);
                if (step["as"] is { } name)
                    scope.Set(name.GetValue<string>(), message);
                break;
            }

            case "pickFile":
            case "pickFolder":
            {
                var path = at.Text("path") ?? throw new ScriptException($"{kind} needs the path to pick.");
                var line = at.Line(ActivityKind.File, Part.Verb("Pick"), Part.File(path), Part.Word("in"), Part.Window(at.Text(kind)!));
                ShowStep(line);
                Mark();
                if (!PickByHand(line, at.Text(kind)!, path, kind == "pickFolder"))
                    _driver.PickFile(at.Text(kind)!, path, kind == "pickFolder");
                Control.Wait(Control.Pace / 2);
                break;
            }

            default:
                return false;
        }
        return true;
    }

    // Shows a step on a control and does it: in a manual run by hand, else by pointing at the control first.
    private void UseControl(StepAt at, IControl control, Activity line, Func<bool> byHand, Action use)
    {
        ShowStep(line);
        if (!byHand())
        {
            _driver.Point(control, line.Text);
            use();
        }
        Control.Wait(Control.Pace / 2);
        Keep(at.Step, at.Scope, control);
    }

    // Clicks a control again and again while it is there and can be used, such as Up until the top folder. Not being
    // there at all is fine: there is nothing to do.
    private void ClickWhileEnabled(Target target, string? say, ClickHow how)
    {
        for (var clicks = 0; _driver.TryFind(target) is { IsEnabled: true } control; clicks++)
        {
            if (clicks == 50)
                throw new StuckException($"get past {target.What}", "It was still there after 50 clicks.");
            var line = new Activity(ActivityKind.Click, [Part.Verb("Click"), Part.Control(target, control)]) { Say = say };
            ShowStep(line);
            Mark();
            if (!ClickByHand(line, target, how, tab: false))
            {
                _driver.Point(control, line.Text);
                _driver.Click(control, how);
            }
            Control.Wait(Math.Max(Control.Pace / 2, 300));
        }
    }

    private static ClickHow How(string? how) => how switch
    {
        null => ClickHow.Invoke,
        "mouse" => ClickHow.Mouse,
        "right" => ClickHow.Right,
        "double" => ClickHow.Double,
        _ => throw new ScriptException($"how is mouse, right or double, not {how}."),
    };
}
