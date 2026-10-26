using System.Runtime.InteropServices;

namespace AutoTestRunner.Cli;

/// <summary>
/// Space, Enter and Esc for the run while it goes, whichever window has the focus, as the AutoHotkey runner's hotkeys
/// were. Keys the runner types itself are marked as injected by Windows and pass straight through, so typing into the
/// app never pauses the run. The callbacks run on the hook's own thread, not the panel's UI thread, so a busy panel
/// never holds up the mouse or the keyboard.
/// </summary>
internal sealed class GlobalKeyHook : IDisposable
{
    private const int KeyDown = 0x0100, KeyUp = 0x0101, LeftButtonDown = 0x0201;
    private const uint Injected = 0x10;
    private const int Space = 0x20, Enter = 0x0D, Escape = 0x1B;

    private readonly HookThread _hooks;
    private readonly Func<int, bool> _onKey;
    private readonly Action<int, int> _onClick;
    private readonly HashSet<int> _swallowed = [];

    /// <param name="onKey">Called with Space, Enter or Esc pressed by the person. True takes the key for the run.</param>
    /// <param name="onClick">Called with where the person clicked, on the screen.</param>
    public GlobalKeyHook(Func<int, bool> onKey, Action<int, int> onClick)
    {
        (_onKey, _onClick) = (onKey, onClick);
        _hooks = new HookThread(OnKeyboard, OnMouse, "Run keys hook");
    }

    // Until Dispose. The hook can be called before the constructor has set _hooks.
    private bool Open => _hooks is not { Closed: true };

    public static bool IsSpace(int key) => key == Space;
    public static bool IsEnter(int key) => key == Enter;
    public static bool IsEscape(int key) => key == Escape;

    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0 && Open)
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
        return HookThread.Next(code, message, data);
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && message == LeftButtonDown && Open)
        {
            var flags = (uint)Marshal.ReadInt32(data, 12);
            if ((flags & 1) == 0) // not injected: the person clicked, not the runner
                _onClick(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
        }
        return HookThread.Next(code, message, data);
    }

    public void Dispose() => _hooks.Dispose();
}
