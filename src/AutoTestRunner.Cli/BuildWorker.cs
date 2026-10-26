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
/// order they did it. A case opened to change it is played as far as the person asks, and started again to go back.
/// Between inputs it keeps up with the app: the box being typed in, the text of the controls the suite watches, such
/// as the status text, and the control under the mouse, which is read before it is clicked so that the step is for
/// what the person saw. Each look is a call into the app, answered on the app's UI thread, so each is made only as
/// often as it needs to be, and the app stays smooth while the person uses it.
/// </summary>
internal sealed class BuildWorker
{
    // How long a watched control's text keeps still before a wait is made for it, in milliseconds.
    private const int StatusSettles = 700;

    // How long a right-click's menu waits for another click before its step is made, in milliseconds.
    private const int MenuSettles = 4000;

    // How often the app's process ids are read again, in milliseconds: an app can start helpers and windows.
    private const int ProcessesEvery = 1000;

    // How often the box with the keyboard and the watched controls' text are read, in milliseconds.
    private const int TypingEvery = 150, WatchedEvery = 250;

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

    // When each was last read, by Environment.TickCount64: 0 is long enough ago for each to be read straight away.
    private long _processesRead, _typingRead, _watchedRead;

    // When the last click was and where, for a double-click; and when a right-click's menu was last clicked in.
    private (long At, int X, int Y) _lastClick = (long.MinValue, 0, 0);
    private long _menuSince;

    // The box being typed in, and what it last held, for when it has gone by the time it is left.
    private InspectedControl? _typing;
    private string? _typed;

    // What each watched control said when a wait was last made for it, or that was there; and a new text, until it
    // keeps still.
    private readonly Dictionary<string, Watching> _watching = new(StringComparer.Ordinal);

    // Where the mouse was, the app's control there once it was read, and whether that is outlined, with how a step
    // would find the control outlined: naming one asks the app about each way to find it, so it is done once a control.
    private (int X, int Y) _pointer = (int.MinValue, int.MinValue);
    private InspectedControl? _under;
    private bool _read, _outlined;
    private (ControlFacts Facts, TargetChoice Choice)? _named;

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
                case BuildInputKind.RightClick:
                    RightClicked(input);
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
                case BuildInputKind.Add when input.Step is { } step:
                    LeaveBox();
                    _panel.Add(_recorder.Add(step, changesStatus: false));
                    _panel.Ready();
                    break;
                case BuildInputKind.RunTo when input.Step is { } step:
                    GoTo(step, rewind: false);
                    break;
                case BuildInputKind.RollBack when input.Step is { } step:
                    GoTo(step, rewind: true);
                    break;
                case BuildInputKind.EditFrom when input.Step is { } step:
                    GoTo(step, input.Rewind, record: true);
                    break;
                case BuildInputKind.Try when input.Check is { } check:
                    Try(check, input.Answer);
                    break;
                case BuildInputKind.Use when input.Macro is { } macro:
                    _runner.Use(macro);
                    break;
            }
        }
    }

    // ===== Getting the app ready =====

    // A new case starts the app, or plays the case it carries on from and those that one carries on from. A case
    // opened to change it plays the cases it carries on from, and its own steps wait to be played.
    private void GetReady()
    {
        _panel.Busy($"Getting {_suite.App.Window} ready");
        if (_request.Edit is null && _request.After is null)
        {
            var start = StepMaker.StartApp(_suite);
            start.State = DraftStepState.NotPlayed;
            _panel.Add(_recorder.Add(start, changesStatus: false));
        }
        var problem = Played(() =>
        {
            _runner.Prepare(_request.Edit is { } editing ? _suite.Leading(editing) : Before());
            PlayBefore();
        });
        if (problem is null && _request.Edit is null && _draft.Steps is [.., var last])
            problem = PlayTo(last);
        Caught();
        _panel.Ready(problem is null ? null : $"Couldn't get {_suite.App.Window} there: {problem} Carry on from where it is, or close the builder.");
    }

    // The cases played before the case's own steps: those the case carries on from, or the case a new one starts
    // where it leaves off, and those they carry on from.
    private IReadOnlyList<Case> Before() =>
        _request.Edit is { } editing ? [.. _suite.Leading(editing).Where(item => item != editing)]
        : _request.After is { } after ? _suite.Leading(after)
        : [];

    private void PlayBefore()
    {
        foreach (var item in Before())
            _runner.Play(item.Steps, Path.GetFileName(item.File));
    }

    // Takes the app to just after a step: plays the steps not played yet up to it, or starts again from where the
    // case starts and plays them all up to it. Then it records, when asked.
    private void GoTo(DraftStep step, bool rewind, bool record = false)
    {
        LeaveBox();
        string? problem = null;
        if (rewind)
        {
            _panel.Busy($"Starting {_suite.App.Window} again, from where the case starts");
            lock (_draft.Steps)
                foreach (var each in _draft.Steps)
                    (each.State, each.Problem) = (DraftStepState.NotPlayed, null);
            _panel.Refresh();
            problem = Played(() =>
            {
                _driver.CloseAll(_runner.Exe);
                _runner.ResetMachine();
                _runner.Forget();
                PlayBefore();
            });
        }
        problem ??= PlayTo(step);
        Caught();
        _panel.Ready(problem, record && problem is null);
    }

    // Plays the steps not played yet, in order, up to and including this one, and stops at one that doesn't work.
    private string? PlayTo(DraftStep last)
    {
        List<DraftStep> steps;
        lock (_draft.Steps)
            steps = [.. _draft.Steps.Take(_draft.Steps.IndexOf(last) + 1)];
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step.State == DraftStepState.Played)
                continue;
            _panel.Playing(step);
            var problem = Played(() => _runner.Play(new JsonArray(Inline(step)), $"Step {index + 1}"));
            (step.State, step.Problem) = problem is null ? (DraftStepState.Played, null) : (DraftStepState.Failed, problem);
            _panel.Refresh(step);
            if (problem is not null)
                return $"Step {index + 1}, {step.Text}, didn't work: {problem}";
        }
        return null;
    }

    // After playing: the app may have started again, and what it shows now isn't a new step's.
    private void Caught()
    {
        ReadProcesses();
        _watching.Clear();
        foreach (var hook in _recorder.Watched)
            _watching[hook.Element] = new Watching(TextOf(hook.Element));
    }

    // ===== What the person did =====

    private void Clicked(BuildInput click)
    {
        // The control the mouse stopped on, read before the click; a message box is read now, as the click waited.
        var hit = !click.PassOn && _under is not null && _pointer == (click.X, click.Y) ? _under : _inspector.At(click.X, click.Y);
        _under = null;
        if (_typing is { } box && !box.Is(hit))
            LeaveBox();
        // A second click where the last one was, quickly enough, makes that click a double-click.
        var now = Environment.TickCount64;
        var doubled = hit is not null && now - _lastClick.At <= Desktop.DoubleClickTime && Math.Abs(click.X - _lastClick.X) <= 4 && Math.Abs(click.Y - _lastClick.Y) <= 4
            && _recorder.Doubled(hit.Facts) is { } step ? step : null;
        _lastClick = doubled is null ? (now, click.X, click.Y) : (long.MinValue, 0, 0);
        if (doubled is not null)
            _panel.Refresh(doubled);
        else if (hit is not null)
        {
            _panel.Add(_recorder.Clicked(Ticked(hit)));
            _menuSince = Environment.TickCount64;
        }
        if (click.PassOn)
            Quietly(() => FlaUiInspector.Pass(click.X, click.Y));
    }

    // A tick box's click is read once it took effect: when the button is up and the app has had a moment, the tick is
    // read again, of the box clicked or the one around the words clicked.
    private ControlFacts Ticked(InspectedControl hit)
    {
        var before = hit.Facts;
        if (before.Type != "CheckBox" && !before.Ancestors.Take(4).Any(around => around.Type == "CheckBox"))
            return before;
        for (var end = Environment.TickCount64 + 2000; RecordingHook.ButtonHeld && Environment.TickCount64 < end;)
            Thread.Sleep(20);
        Thread.Sleep(200);
        var after = _inspector.Again(hit).Facts;
        return before with
        {
            Checked = after.Checked,
            Ancestors = [.. before.Ancestors.Select((around, index) =>
                index < after.Ancestors.Count && after.Ancestors[index].Type == around.Type ? around with { Checked = after.Ancestors[index].Checked } : around)],
        };
    }

    private void RightClicked(BuildInput click)
    {
        var hit = _under is not null && _pointer == (click.X, click.Y) ? _under : _inspector.At(click.X, click.Y);
        _under = null;
        LeaveBox();
        if (hit is not null)
            _panel.Add(_recorder.RightClicked(hit.Facts));
        _menuSince = Environment.TickCount64;
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
            Caught();
        }
        else if (problem is null)
            _panel.Add(_recorder.Add(step, changesStatus: step.Verb == "click"));
        _panel.Ready(add && problem is not null ? $"That didn't work, so it isn't a step: {problem}" : null);
    }

    // Runs a check from a form against the app as it is, and says how it went.
    private void Try(JsonObject check, Action<CheckResult>? answer)
    {
        CheckResult? result = null;
        var problem = Played(() => result = _runner.TryCheck(InlineCheck(check)));
        answer?.Invoke(result ?? new CheckResult("The check", false, problem));
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
        // A right-click's menu that has had no click for a while is done with: its step is made.
        if (_recorder.MenuOpen && Environment.TickCount64 - _menuSince > MenuSettles)
            _panel.Add(_recorder.MenuDone());
        var now = Environment.TickCount64;
        if (mode == BuildMode.Recording)
        {
            if (now - _typingRead >= TypingEvery)
            {
                _typingRead = now;
                FollowTyping();
            }
            if (now - _watchedRead >= WatchedEvery)
            {
                _watchedRead = now;
                FollowWatched();
            }
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
        var box = _inspector.TypingIn(_typing) is { Facts.Dialog: null } typing ? typing : null;
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

    // A watched control's text that changed and kept still gets a wait after the step that changed it.
    private void FollowWatched()
    {
        foreach (var hook in _recorder.Watched)
        {
            var now = TextOf(hook.Element);
            if (!_watching.TryGetValue(hook.Element, out var watching))
            {
                _watching[hook.Element] = new Watching(now);
                continue;
            }
            if (now is null || now == watching.Last)
            {
                watching.Changing = null;
                continue;
            }
            if (now != watching.Changing)
                (watching.Changing, watching.Since) = (now, Environment.TickCount64);
            else if (Environment.TickCount64 - watching.Since >= StatusSettles)
                Settle();
        }
    }

    // A watched control's text changed and kept still, or something else is about to happen: a wait for it goes
    // after the step that changed it.
    private void Settle()
    {
        foreach (var hook in _recorder.Watched)
        {
            if (!_watching.TryGetValue(hook.Element, out var watching) || watching.Changing is not { } now)
                continue;
            (watching.Last, watching.Changing) = (now, null);
            _panel.Add(_recorder.Changed(hook, now));
        }
    }

    // Reads the control under the mouse once it stops on one, so a click is of what was there before it; while it is
    // moving, nothing is read, as a click there reads what it is on. While picking or while Ctrl is held, the control
    // is read as the mouse goes, and outlined with how a step would find it.
    private void FollowPointer(BuildMode mode)
    {
        var at = Desktop.Cursor();
        var moved = at != _pointer;
        if (moved)
            (_pointer, _under, _read) = (at, null, false);
        var outlining = mode == BuildMode.Picking || RecordingHook.ControlHeld;
        if (!_read && (!moved || outlining))
        {
            _under = _session.IsApp(Desktop.WindowAt(at.X, at.Y)) ? _inspector.At(at.X, at.Y) : null;
            _read = true;
        }
        if (outlining && _under is { } under)
        {
            if (!moved && _outlined)
                return;
            var meant = under.Facts.Meant();
            if (_outlined && _named is { } named && Same(named.Facts, meant))
                return;
            var choice = Name(meant);
            _named = (meant, choice);
            _panel.ShowHover(meant, choice);
            _outlined = true;
        }
        else
            EndHover();
    }

    // Whether two reads are of the same control, as far as how a step finds it goes.
    private static bool Same(ControlFacts one, ControlFacts other) =>
        one.Type == other.Type && one.Name == other.Name && one.Id == other.Id && one.ClassName == other.ClassName && one.Bounds == other.Bounds;

    private void EndHover()
    {
        _named = null;
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
            (_under, _read) = (null, false);
            _panel.Values = _runner.Values();
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
    private JsonObject Inline(DraftStep step) => Inline(step.Json);

    // The same for a step in a repeat or an if, and an if's check.
    private JsonObject Inline(JsonObject step)
    {
        var copy = step.DeepClone().AsObject();
        var verb = ScriptVocabulary.KindOf(copy);
        if (ScriptVocabulary.Steps.TryGetValue(verb, out var kind) && kind.Takes == Takes.Target && copy[verb] is { } target)
            copy[verb] = Inline(target);
        if (verb == "if" && copy["if"] is JsonObject condition)
            copy["if"] = InlineCheck(condition);
        foreach (var key in new[] { "steps", "else" })
            if (copy[key] is JsonArray inner)
                copy[key] = new JsonArray([.. inner.OfType<JsonObject>().Select(Inline)]);
        return copy;
    }

    // A check of a control named while building, with the control given by how it is found.
    private JsonObject InlineCheck(JsonObject check)
    {
        var copy = check.DeepClone().AsObject();
        if (Kinds.Of(copy, ScriptVocabulary.Checks) is { } kind && ScriptVocabulary.ControlChecks.Contains(kind) && copy[kind] is { } target)
            copy[kind] = Inline(target);
        return copy;
    }

    private JsonNode Inline(JsonNode target) =>
        target is JsonValue value && value.TryGetValue<string>(out var name) && !_suite.Elements.ContainsKey(name) && Elements().TryGetValue(name, out var named)
            ? named.DeepClone()
            : target.DeepClone();

    // What a watched control says now, or null when it isn't there or can't be read.
    private string? TextOf(string element)
    {
        try
        {
            var elements = Elements();
            return _driver.TryFind(Targets.Resolve(element, _runner.Globals, elements)) is { } control ? control.Value ?? control.Name : null;
        }
        catch (Exception ex) when (ex is StuckException or ScriptException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    // A watched control's text: what it said when it was last waited for, and a new text and since when.
    private sealed class Watching(string? last)
    {
        public string? Last { get; set; } = last;

        public string? Changing { get; set; }

        public long Since { get; set; }
    }
}
