using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoTestRunner.Cli;

/// <summary>
/// Space, Enter and Esc for the run while it goes, whichever window has the focus, as the AutoHotkey runner's hotkeys
/// were. Keys the runner types itself are marked as injected by Windows and pass straight through, so typing into the
/// app never pauses the run. A low-level hook runs on the thread that installed it, which must pump messages: the
/// panel's UI thread does.
/// </summary>
internal sealed class GlobalKeyHook : IDisposable
{
    private const int KeyboardHook = 13, MouseHook = 14;
    private const int KeyDown = 0x0100, KeyUp = 0x0101, LeftButtonDown = 0x0201;
    private const uint Injected = 0x10;
    private const int Space = 0x20, Enter = 0x0D, Escape = 0x1B;

    private readonly HookProc _keyboard, _mouse;
    private readonly nint _keyboardHook, _mouseHook;
    private readonly Func<int, bool> _onKey;
    private readonly Action<int, int> _onClick;
    private readonly HashSet<int> _swallowed = [];

    /// <param name="onKey">Called with Space, Enter or Esc pressed by the person. True takes the key for the run.</param>
    /// <param name="onClick">Called with where the person clicked, on the screen.</param>
    public GlobalKeyHook(Func<int, bool> onKey, Action<int, int> onClick)
    {
        (_onKey, _onClick) = (onKey, onClick);
        _keyboard = OnKeyboard;
        _mouse = OnMouse;
        var module = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        _keyboardHook = SetWindowsHookEx(KeyboardHook, _keyboard, module, 0);
        _mouseHook = SetWindowsHookEx(MouseHook, _mouse, module, 0);
    }

    public static bool IsSpace(int key) => key == Space;
    public static bool IsEnter(int key) => key == Enter;
    public static bool IsEscape(int key) => key == Escape;

    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var key = Marshal.ReadInt32(data);
            var flags = (uint)Marshal.ReadInt32(data, 8);
            if ((flags & Injected) == 0 && key is Space or Enter or Escape)
            {
                if (message == KeyDown && _onKey(key))
                {
                    _swallowed.Add(key);
                    return 1;
                }
                if (message == KeyUp && _swallowed.Remove(key))
                    return 1;
            }
        }
        return CallNextHookEx(0, code, message, data);
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && message == LeftButtonDown)
        {
            var flags = (uint)Marshal.ReadInt32(data, 12);
            if ((flags & 1) == 0) // not injected: the person clicked, not the runner
                _onClick(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
        }
        return CallNextHookEx(0, code, message, data);
    }

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
