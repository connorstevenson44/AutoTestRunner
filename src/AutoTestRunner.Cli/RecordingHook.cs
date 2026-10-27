using System.Runtime.InteropServices;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using static AutoTestRunner.Cli.HookThread;

namespace AutoTestRunner.Cli;

/// <summary>
/// The person's clicks and keys while a case is built, wherever they go. A click or key can be kept from the app, as
/// a pick tool, Ctrl+click and the record key do, and as a message box's are while the builder reads it. While the
/// builder plays steps itself, everything is let through unnoticed. Input another program sends counts as the
/// person's, so a test can build a case as a person would. The callbacks run on a <see cref="HookThread"/> and only
/// note what happened: the builder reads the app on a thread of its own.
/// </summary>
internal sealed class RecordingHook : IDisposable
{
    private readonly HookThread _hooks;
    private readonly Func<int, int, bool, bool> _onDown;
    private readonly Func<string, bool> _onKeys;
    private readonly Func<bool> _playing;
    private readonly Action<int, int>? _onRight;
    private bool _keepingUp;

    /// <param name="onDown">The left button went down at x, y, with whether Ctrl is held. True keeps the click from the app.</param>
    /// <param name="onKeys">A key a step can press was pressed, such as Enter or Ctrl+S. True keeps it from the app.</param>
    /// <param name="playing">Whether the builder is playing steps itself, when nothing is noticed.</param>
    /// <param name="onRight">The right button went down at x, y. It always reaches the app.</param>
    public RecordingHook(Func<int, int, bool, bool> onDown, Func<string, bool> onKeys, Func<bool> playing, Action<int, int>? onRight = null)
    {
        (_onDown, _onKeys, _playing, _onRight) = (onDown, onKeys, playing, onRight);
        _hooks = new HookThread(OnKeyboard, OnMouse, "Recording hook");
    }

    /// <summary>
    /// Waits for one click in the app, whose processes these are, and keeps it from the app; or for Esc. Calls picked,
    /// on the hook's thread, with where the click was and whether Shift was held to pick exactly what is under the
    /// mouse, or with null for Esc.
    /// </summary>
    public static RecordingHook PickOnce(IReadOnlySet<int> app, Action<(int X, int Y, bool Exact)?> picked) => new(
        (x, y, _) =>
        {
            if (!app.Contains(Native.ProcessOf(Native.WindowAt(x, y))))
                return false;
            picked((x, y, ShiftHeld));
            return true;
        },
        keys =>
        {
            if (keys == "Esc")
                picked(null);
            return false;
        },
        () => false);

    /// <summary>Whether Ctrl is held down now.</summary>
    public static bool ControlHeld => Held(Control);

    /// <summary>Whether Shift is held down now: a control picked is the one under the mouse, even one no step can find.</summary>
    public static bool ShiftHeld => Held(Shift);

    /// <summary>Whether the left mouse button is held down now.</summary>
    public static bool ButtonHeld => Held(LeftButton);

    // Until Dispose. The hook can be called before the constructor has set _hooks.
    private bool Open => _hooks is not { Closed: true };

    private nint OnMouse(int code, nint message, nint data)
    {
        if (!Open)
            return HookThread.Next(code, message, data);
        if (code >= 0 && message == RightButtonDown && _onRight is not null && !_playing())
            _onRight(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
        if (code >= 0 && message is LeftButtonDown or LeftButtonUp)
        {
            var playing = _playing();
            var (x, y) = (Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
            if (!playing && message == LeftButtonDown && _onDown(x, y, ControlHeld))
            {
                _keepingUp = true;
                return 1;
            }
            if (message == LeftButtonUp && _keepingUp)
            {
                _keepingUp = false;
                return 1;
            }
        }
        return HookThread.Next(code, message, data);
    }

    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (Open && code >= 0 && message is KeyDown or SystemKeyDown && !_playing() && Keys(Marshal.ReadInt32(data)) is { } keys && _onKeys(keys))
            return 1;
        return HookThread.Next(code, message, data);
    }

    // A key as a key step writes it, with the keys held with it: "Enter", "F5", "Ctrl+S". Null for keys that are
    // typing, moving about, or editing in a box: those are recorded as what the box holds when it is left.
    private static string? Keys(int key)
    {
        var (ctrl, alt) = (Held(Control), Held(Menu));
        var recorded = key switch
        {
            // Enter, Esc, Delete and F1 to F12.
            0x0D or 0x1B or 0x2E or (>= 0x70 and <= 0x7B) => true,
            // Letters and digits only with Ctrl or Alt; copy, paste, cut, undo, redo and select all belong to typing in a box.
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => (ctrl || alt) && !(ctrl && !alt && (char)key is 'A' or 'C' or 'V' or 'X' or 'Z' or 'Y'),
            _ => false,
        };
        return recorded ? KeyNames.Written(key, ctrl, alt, Held(Shift), win: false) : null;
    }

    public void Dispose() => _hooks.Dispose();
}
