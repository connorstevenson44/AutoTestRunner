using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoTestRunner.Cli;

/// <summary>
/// The person's clicks and keys while a case is built, wherever they go. A click or key can be kept from the app, as
/// Pick and Ctrl+click do, and as a message box's are while the builder reads it. While the builder plays steps itself,
/// everything is let through unnoticed. Input
/// another program sends counts as the person's, so a test can build a case as a person would. The
/// callbacks must be quick, as Windows drops a hook that keeps it waiting, so they only note what happened: the
/// builder reads the app on a thread of its own. A low-level hook runs on the thread that installed it, which must
/// pump messages: the builder panel's UI thread does.
/// </summary>
internal sealed class RecordingHook : IDisposable
{
    private const int KeyboardHook = 13, MouseHook = 14;
    private const int KeyDown = 0x0100, SystemKeyDown = 0x0104, LeftButtonDown = 0x0201, LeftButtonUp = 0x0202, RightButtonDown = 0x0204;
    private const int LeftButton = 0x01, Control = 0x11, Shift = 0x10, Menu = 0x12;

    private readonly HookProc _keyboard, _mouse;
    private readonly nint _keyboardHook, _mouseHook;
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
        _keyboard = OnKeyboard;
        _mouse = OnMouse;
        var module = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        _keyboardHook = SetWindowsHookEx(KeyboardHook, _keyboard, module, 0);
        _mouseHook = SetWindowsHookEx(MouseHook, _mouse, module, 0);
    }

    /// <summary>Whether Ctrl is held down now.</summary>
    public static bool ControlHeld => Held(Control);

    /// <summary>Whether the left mouse button is held down now.</summary>
    public static bool ButtonHeld => Held(LeftButton);

    private nint OnMouse(int code, nint message, nint data)
    {
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
        return CallNextHookEx(0, code, message, data);
    }

    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0 && message is KeyDown or SystemKeyDown && !_playing() && Keys(Marshal.ReadInt32(data)) is { } keys && _onKeys(keys))
            return 1;
        return CallNextHookEx(0, code, message, data);
    }

    // A key as a key step writes it, with the keys held with it: "Enter", "F5", "Ctrl+S". Null for keys that are
    // typing, moving about, or editing in a box: those are recorded as what the box holds when it is left.
    private static string? Keys(int key)
    {
        var ctrl = Held(Control);
        var alt = Held(Menu);
        var shift = Held(Shift);
        var name = key switch
        {
            0x0D => "Enter",
            0x1B => "Esc",
            0x2E => "Delete",
            >= 0x70 and <= 0x7B => $"F{key - 0x6F}",
            >= 0x41 and <= 0x5A when ctrl || alt => ((char)key).ToString(),
            >= 0x30 and <= 0x39 when ctrl || alt => ((char)key).ToString(),
            _ => null,
        };
        // Copy, paste, cut, undo, redo and select all belong to typing in a box.
        if (name is null || ctrl && !alt && name is "A" or "C" or "V" or "X" or "Z" or "Y")
            return null;
        // Shift is kept with every key that gets here: letters and digits only do with Ctrl or Alt.
        return (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + name;
    }

    private static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose()
    {
        UnhookWindowsHookEx(_keyboardHook);
        UnhookWindowsHookEx(_mouseHook);
    }

    private delegate nint HookProc(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
