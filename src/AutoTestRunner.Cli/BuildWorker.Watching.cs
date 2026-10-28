using AutoTestRunner.Building;
using AutoTestRunner.FlaUI;

namespace AutoTestRunner.Cli;

internal sealed partial class BuildWorker
{
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
            if (!_watching.TryGetValue(hook.Element, out var watching))
            {
                _watching[hook.Element] = Watch(hook.Element);
                continue;
            }
            var now = TextOf(hook.Element, watching);
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
    // is read as the mouse goes, and outlined with how a step would find it: the control around it, such as a text
    // box, when no way finds it by itself, or with Shift held exactly the control under the mouse.
    private void FollowPointer(BuildMode mode)
    {
        var at = Native.Cursor();
        var moved = at != _pointer;
        if (moved)
            (_pointer, _under, _read) = (at, null, false);
        var outlining = mode == BuildMode.Picking || RecordingHook.ControlHeld;
        if (!_read && (!moved || outlining))
        {
            var window = Native.WindowAt(at.X, at.Y);
            var isApp = _session.IsApp(window);
            if (isApp)
                FollowWindow(window);
            _under = isApp ? _inspector.At(at.X, at.Y) : null;
            _read = true;
        }
        if (outlining && _under is { } under)
        {
            var exact = RecordingHook.ShiftHeld;
            // Still on the control outlined, it is named again only when Shift went down or up.
            if (!moved && _outlined && (_named is not { } held || held.Exact == exact))
                return;
            if (_outlined && _named is { } named && named.Exact == exact && Same(named.Pointed, under.Facts))
                return;
            var (picked, choice) = Pick(under.Facts, exact);
            _named = (under.Facts, exact, picked, choice);
            _panel.ShowHover(picked, choice);
            _outlined = true;
        }
        else
            EndHover();
    }

    // The window driven may have closed, as a sign-in window does once it is used: the app's window the mouse is on is
    // driven from then on, and the case does the same, so its steps find their controls there when it runs.
    private void FollowWindow(nint window)
    {
        if (Native.IsDialog(window) || Native.Title(window) is not { Length: > 0 } title || !_driver.Follow(window))
            return;
        LeaveBox();
        _panel.Add(_recorder.Add(StepMaker.UseWindow(title), changesStatus: false));
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
}
