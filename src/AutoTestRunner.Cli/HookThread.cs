using System.Runtime.InteropServices;

namespace AutoTestRunner.Cli;

/// <summary>
/// Low-level mouse and keyboard hooks on a thread of their own, which does nothing but pump the messages that call
/// them. Windows calls a low-level hook on the thread that set it, and every mouse move and key press on the desktop
/// waits for it: set on a window's UI thread, each moment that thread spends laying out, drawing or reading the app
/// holds up the mouse everywhere, so it stutters and jumps. Microsoft's advice is to keep the callback short and the
/// thread that runs it free, as here.
/// https://learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc
/// </summary>
internal sealed class HookThread : IDisposable
{
    private const int KeyboardHook = 13, MouseHook = 14;
    private const uint Quit = 0x0012, NoRemove = 0;

    // The messages the callbacks get, and the virtual keys of the buttons and keys held with others.
    public const int KeyDown = 0x0100, KeyUp = 0x0101, SystemKeyDown = 0x0104;
    public const int MouseMove = 0x0200, LeftButtonDown = 0x0201, LeftButtonUp = 0x0202, RightButtonDown = 0x0204;
    public const int LeftButton = 0x01, Control = 0x11, Shift = 0x10, Menu = 0x12, LeftWindows = 0x5B, RightWindows = 0x5C;

    private readonly HookProc _keyboard, _mouse;
    private readonly Thread _thread;
    private uint _threadId;
    private volatile bool _closed;

    /// <param name="keyboard">Called for each key going down or up, on the hook's thread.</param>
    /// <param name="mouse">Called for each move and button of the mouse, on the hook's thread.</param>
    /// <param name="name">The thread's name, as the debugger shows it.</param>
    public HookThread(HookProc keyboard, HookProc mouse, string name)
    {
        // Kept in fields, so the garbage collector leaves them while Windows calls them.
        (_keyboard, _mouse) = (keyboard, mouse);
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            // A thread has no message queue until it asks for a message: one is made now, so Dispose can post to it.
            _ = PeekMessage(out _, 0, 0, 0, NoRemove);
            var module = GetModuleHandle(null);
            var keyboardHook = SetWindowsHookEx(KeyboardHook, _keyboard, module, 0);
            var mouseHook = SetWindowsHookEx(MouseHook, _mouse, module, 0);
            ready.Set();
            while (GetMessage(out var message, 0, 0, 0) > 0)
                DispatchMessage(ref message);
            UnhookWindowsHookEx(keyboardHook);
            UnhookWindowsHookEx(mouseHook);
        })
        { IsBackground = true, Name = name };
        _thread.Start();
        ready.Wait();
    }

    /// <summary>Whether Dispose was called, after which the callbacks should let everything through.</summary>
    public bool Closed => _closed;

    /// <summary>Whether a key or mouse button is held down now, by its virtual key.</summary>
    public static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>Passes input on to the next hook and the app, as a callback does with what it doesn't keep.</summary>
    public static nint Next(int code, nint message, nint data) => CallNextHookEx(0, code, message, data);

    /// <summary>Takes the hooks away. It can be called from any thread, and more than once.</summary>
    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _ = PostThreadMessage(_threadId, Quit, 0, 0);
    }

    public delegate nint HookProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam, LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, nint window, uint first, uint last);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, nint window, uint first, uint last, uint remove);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
