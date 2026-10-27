using System.Runtime.InteropServices;
using AutoTestRunner.Driving;

namespace AutoTestRunner.Cli;

/// <summary>
/// Space, Enter and Esc for the run while it goes, whichever window has the focus, as the AutoHotkey runner's hotkeys
/// were. Keys the runner types itself are marked as injected by Windows and pass straight through, so typing into the
/// app never pauses the run. The callbacks run on the hook's own thread, not the panel's UI thread, so a busy panel
/// never holds up the mouse or the keyboard. The person's clicks and keys are noted for a manual run too, which waits
/// on them.
/// </summary>
internal sealed class GlobalKeyHook : IDisposable
{
    private const int KeyDown = 0x0100, KeyUp = 0x0101, SystemKeyDown = 0x0104;
    private const int MouseMove = 0x0200, LeftButtonDown = 0x0201, LeftButtonUp = 0x0202, RightButtonDown = 0x0204;
    private const int Control = 0x11, Shift = 0x10, Menu = 0x12, LeftWindows = 0x5B, RightWindows = 0x5C;
    private const uint Injected = 0x10;
    private const int Space = 0x20, Enter = 0x0D, Escape = 0x1B;

    private readonly HookThread _hooks;
    private readonly Func<int, bool> _onKey;
    private readonly Action<int, int> _onClick;
    private readonly PersonInput _person;
    private readonly HashSet<int> _swallowed = [];

    /// <param name="onKey">Called with Space, Enter or Esc pressed by the person. True takes the key for the run.</param>
    /// <param name="onClick">Called with where the person clicked, on the screen.</param>
    /// <param name="person">Where the person's clicks and keys are noted, for a manual run.</param>
    public GlobalKeyHook(Func<int, bool> onKey, Action<int, int> onClick, PersonInput person)
    {
        (_onKey, _onClick, _person) = (onKey, onClick, person);
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
            if ((flags & Injected) == 0 && message is KeyDown or SystemKeyDown && _person.Noting
                && KeyNames.Of(key, Held(Control), Held(Menu), Held(Shift), Held(LeftWindows) || Held(RightWindows)) is { } keys)
                _person.Note(PersonEvent.Pressed(keys));
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
        // Not injected: the person, not the runner.
        if (code >= 0 && Open && ((uint)Marshal.ReadInt32(data, 12) & 1) == 0)
        {
            var (x, y) = (Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
            switch (message)
            {
                case MouseMove:
                    _person.Moved(x, y);
                    break;
                case LeftButtonDown:
                    _onClick(x, y);
                    _person.Note(PersonEvent.Button(PersonEventKind.LeftDown, x, y));
                    break;
                case LeftButtonUp:
                    _person.Note(PersonEvent.Button(PersonEventKind.LeftUp, x, y));
                    break;
                case RightButtonDown:
                    _person.Note(PersonEvent.Button(PersonEventKind.RightDown, x, y));
                    break;
            }
        }
        return HookThread.Next(code, message, data);
    }

    private static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose() => _hooks.Dispose();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
