using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AutoTestRunner.Building;
using AutoTestRunner.Cli.Views;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// The builder's thread: gets the app to where the case starts, then makes steps of what the person does in it, in the
/// order they did it. Between inputs it keeps up with the app: the box being typed in, the status text, and the
/// control under the mouse, which is read before it is clicked so that the step is for what the person saw.
/// </summary>
internal sealed class BuildWorker
{
    // How long the status text keeps still before a wait is made for it, in milliseconds.
    private const int StatusSettles = 700;

    // How often the app's process ids are read again, in milliseconds: an app can start helpers and windows.
    private const int ProcessesEvery = 1000;

    private readonly BuildSession _session;
    private readonly BuildRequest _request;
    private readonly Suite _suite;
    private readonly CaseDraft _draft;
    private readonly BuilderPanel _panel;
    private readonly BuilderView _view;
    private readonly FlaUiDriver _driver;
    private readonly Runner _runner;
    private readonly FlaUiInspector _inspector;
    private readonly Recorder _recorder;

    private long _processesRead = long.MinValue;

    // The box being typed in, and what it last held, for when it has gone by the time it is left.
    private InspectedControl? _typing;
    private string? _typed;

    // The status text a wait was last made for, or that was there; and a new one, until it keeps still.
    private string? _status;
    private string? _changing;
    private long _changingSince;

    // Where the mouse was, the app's control there, and whether that is outlined.
    private (int X, int Y) _pointer = (int.MinValue, int.MinValue);
    private InspectedControl? _under;
    private bool _outlined;

    public BuildWorker(BuildSession session, BuildRequest request, CaseDraft draft, BuilderPanel panel, BuilderView view, FlaUiDriver driver, Runner runner)
    {
        (_session, _request, _suite, _draft, _panel, _view, _driver, _runner) = (session, request, request.Suite, draft, panel, view, driver, runner);
        _inspector = new FlaUiInspector(driver);
        _recorder = new Recorder(_suite, draft, Name);
    }

    /// <summary>Gets the app ready, then builds until the panel closes.</summary>
    public void Run()
    {
        if (_runner.Missing() is [_, ..] missing)
        {
            _panel.Tell($"These can't be found, so the builder can't start {_suite.App.Window}:\n\n{string.Join("\n", missing)}");
            while (_session.Next(out _))
            {
                // Nothing can be built: wait for the panel to close.
            }
            return;
        }
        GetReady();
        while (_session.Next(out var input))
        {
            if (input is null)
            {
                KeepUp();
                continue;
            }
            Settle();
            switch (input.Kind)
            {
                case BuildInputKind.Click:
                    Clicked(input);
                    break;
                case BuildInputKind.Pick:
                    Picked(input.X, input.Y);
                    break;
                case BuildInputKind.Key when input.Keys is { } keys:
                    Pressed(keys, input.PassOn);
                    break;
                case BuildInputKind.Play when input.Step is { } step:
                    Play(step, input.Add);
                    break;
                case BuildInputKind.Locate:
                    Locate(input.Step);
                    break;
            }
        }
    }

    // ===== Getting the app ready =====

    // A new case starts the app; one that carries on from a case, or steps added to one, play that case first, and
    // the cases it carries on from.
    private void GetReady()
    {
        _panel.Busy($"Getting {_suite.App.Window} ready");
        string? problem;
        if ((_request.AddTo ?? _request.After) is { } start)
        {
            var cases = Leading(start);
            problem = Played(() =>
            {
                _runner.Prepare(cases);
                foreach (var item in cases)
                    _runner.Play(item.Steps, Path.GetFileName(item.File));
            });
        }
        else
        {
            var step = StepMaker.StartApp(_suite);
            _panel.Add(_recorder.Add(step, changesStatus: false));
            problem = Played(() => _runner.Play(new JsonArray(step.Json.DeepClone()), "The new case"));
        }
        ReadProcesses();
        _status = Status();
        _panel.Ready(problem is null ? null : $"Couldn't get {_suite.App.Window} there: {problem} Carry on from where it is, or close the builder.");
    }

    // A case and those it carries on from, in the order they run.
    private List<Case> Leading(Case last)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new Stack<Case>([last]);
        while (waiting.TryPop(out var item))
        {
            if (!ids.Add(item.Id))
                continue;
            foreach (var need in item.Needs)
                if (_suite.Find(need) is { } needed)
                    waiting.Push(needed);
        }
        return [.. _suite.Cases.Where(item => ids.Contains(item.Id))];
    }

    // ===== What the person did =====

    private void Clicked(BuildInput click)
    {
        // The control the mouse stopped on, read before the click; a message box is read now, as the click waited.
        var hit = !click.PassOn && _under is not null && _pointer == (click.X, click.Y) ? _under : _inspector.At(click.X, click.Y);
        _under = null;
        if (_typing is { } box && !box.Is(hit))
            LeaveBox();
        if (hit is not null)
            _panel.Add(_recorder.Clicked(hit.Facts));
        if (click.PassOn)
            Quietly(() => FlaUiInspector.Pass(click.X, click.Y));
    }

    private void Picked(int x, int y)
    {
        if (_inspector.At(x, y) is not { } hit)
            return;
        var meant = hit.Facts.Meant();
        _panel.ShowPicked(meant, Name(meant), FlaUiInspector.Describe(hit));
        _outlined = true;
    }

    private void Pressed(string keys, bool passOn)
    {
        // Delete in a box is editing it.
        if (keys == "Delete" && _typing is not null)
            return;
        // A message box's Enter or Esc waited, so the box can be read before it closes.
        var focused = passOn ? _inspector.Focused()?.Facts : null;
        LeaveBox();
        _panel.Add(_recorder.Pressed(keys, focused));
        if (passOn)
            Quietly(() => _inspector.Pass(keys));
    }

    // Plays a step from the panel: a picked control's, which goes in the case if it worked, or one tried again.
    private void Play(DraftStep step, bool add)
    {
        LeaveBox();
        _panel.Busy(step.Text);
        var problem = Played(() => _runner.Play(new JsonArray(Inline(step)), "The builder"));
        if (!add)
        {
            _panel.Tried(step, problem);
            // What a step tried again changes is not a new step's.
            _status = Status();
            _changing = null;
        }
        else if (problem is null)
            _panel.Add(_recorder.Add(step, changesStatus: step.Verb == "click"));
        _panel.Ready(add && problem is not null ? $"That didn't work, so it isn't a step: {problem}" : null);
    }

    private void Locate(DraftStep? step)
    {
        if (step?.Target is not { } target)
        {
            _panel.Outline(null, null);
            return;
        }
        ScreenRect? box;
        try
        {
            box = _inspector.Locate(Targets.Resolve(Inline(target.Json), _runner.Globals, Elements()));
        }
        catch (ScriptException)
        {
            // It uses a variable kept by an earlier step, say: there's nothing to outline.
            box = null;
        }
        _panel.Outline(box, target);
    }

    // ===== Keeping up with the app =====

    private void KeepUp()
    {
        if (Environment.TickCount64 - _processesRead >= ProcessesEvery)
            ReadProcesses();
        var mode = _panel.Mode;
        if (mode == BuildMode.Recording)
        {
            FollowTyping();
            FollowStatus();
        }
        else
            LeaveBox();
        if (mode == BuildMode.Paused)
            EndHover();
        else
            FollowPointer(mode);
    }

    private void ReadProcesses()
    {
        _session.Watch(_inspector.Processes());
        _processesRead = Environment.TickCount64;
    }

    // A box the keyboard went into, and a type step for what it holds once the keyboard leaves it. A file picker's
    // name box is not one: what it holds is the pick's.
    private void FollowTyping()
    {
        var box = _inspector.TypingIn() is { Facts.Dialog: null } typing ? typing : null;
        if (_typing is { } current)
        {
            if (box is not null && box.Is(current))
            {
                _typed = FlaUiInspector.ValueOf(box) ?? _typed;
                return;
            }
            LeaveBox();
        }
        if (box is null)
            return;
        _recorder.Entered(box.Facts);
        (_typing, _typed) = (box, box.Facts.Value);
    }

    // A password can't be read: a box for one gets a step for ${password} whenever it is left.
    private void LeaveBox()
    {
        if (_typing is not { } box)
            return;
        _typing = null;
        _panel.Add(_recorder.Left(box.Facts.IsPassword ? "" : FlaUiInspector.ValueOf(box) ?? _typed));
    }

    private void FollowStatus()
    {
        if (Status() is not { } now || now == _status)
        {
            _changing = null;
            return;
        }
        if (now != _changing)
            (_changing, _changingSince) = (now, Environment.TickCount64);
        else if (Environment.TickCount64 - _changingSince >= StatusSettles)
            Settle();
    }

    // The status text changed and kept still, or something else is about to happen: a wait for it goes after the step
    // that changed it.
    private void Settle()
    {
        if (_changing is not { } now)
            return;
        (_status, _changing) = (now, null);
        _panel.Add(_recorder.StatusSaid(now));
    }

    // Reads the control under the mouse as it stops on one, so a click is of what was there before it; and outlines
    // it, with how a step would find it, while picking or while Ctrl is held.
    private void FollowPointer(BuildMode mode)
    {
        var at = Desktop.Cursor();
        var moved = at != _pointer;
        if (moved)
        {
            _pointer = at;
            _under = _session.IsApp(Desktop.WindowAt(at.X, at.Y)) ? _inspector.At(at.X, at.Y) : null;
        }
        if ((mode == BuildMode.Picking || RecordingHook.ControlHeld) && _under is { } under)
        {
            if (!moved && _outlined)
                return;
            var meant = under.Facts.Meant();
            _panel.ShowHover(meant, Name(meant));
            _outlined = true;
        }
        else
            EndHover();
    }

    private void EndHover()
    {
        if (!_outlined)
            return;
        _panel.EndHover();
        _outlined = false;
    }

    // ===== Helpers =====

    // Plays steps with the person's input let through unnoticed, and says why they didn't work, if they didn't.
    private string? Played(Action play)
    {
        _view.Problem = null;
        try
        {
            Quietly(play);
            return _view.Problem;
        }
        catch (Exception ex) when (ex is SkipCaseException or StopRunException)
        {
            return _view.Problem ?? ex.Message;
        }
        catch (Exception ex) when (ex is ScriptException or InvalidOperationException or ArgumentException or IOException or FormatException or KeyNotFoundException)
        {
            return ex.Message;
        }
        finally
        {
            _under = null;
        }
    }

    // The hook sees input a moment after it was sent: the builder waits for it before noticing input again.
    private void Quietly(Action act)
    {
        _session.Playing = true;
        try
        {
            act();
        }
        finally
        {
            Thread.Sleep(150);
            _session.Playing = false;
        }
    }

    // How a step would find a control, asking the app how many controls each way finds.
    private TargetChoice Name(ControlFacts control)
    {
        var elements = Elements();
        return TargetSuggester.Suggest(control, elements, _runner.Globals, node =>
        {
            try
            {
                return _driver.Count(Targets.Resolve(node, _runner.Globals, elements));
            }
            catch (Exception ex) when (ex is ScriptException or COMException or InvalidOperationException)
            {
                return 0;
            }
        });
    }

    private Dictionary<string, JsonNode> Elements() => _draft.ElementsWith(_suite.Elements);

    // A step for the runner, which knows only the suite's elements: a control named while building is given by how
    // it is found instead.
    private JsonObject Inline(DraftStep step)
    {
        var copy = step.Json.DeepClone().AsObject();
        if (copy[step.Verb] is { } target)
            copy[step.Verb] = Inline(target);
        return copy;
    }

    private JsonNode Inline(JsonNode target) =>
        target is JsonValue value && value.TryGetValue<string>(out var name) && !_suite.Elements.ContainsKey(name) && Elements().TryGetValue(name, out var named)
            ? named.DeepClone()
            : target.DeepClone();

    // What the app's status text says now, or null when it has none or it can't be read.
    private string? Status()
    {
        if (_suite.App.Status is not { } status)
            return null;
        try
        {
            return _driver.TryFind(Targets.Resolve(status, _runner.Globals, _suite.Elements)) is { } control ? control.Value ?? control.Name : null;
        }
        catch (Exception ex) when (ex is StuckException or ScriptException or COMException or InvalidOperationException)
        {
            return null;
        }
    }
}
