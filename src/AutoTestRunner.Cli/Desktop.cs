using System.Runtime.InteropServices;

namespace AutoTestRunner.Cli;

/// <summary>
/// The quick Win32 questions the builder asks while the person uses the app: which window is under a point or has the
/// keyboard, whose it is, whether it is a Windows message box or file picker, and where the mouse is. They answer in
/// microseconds, so the recording hook can ask them while Windows waits for it.
/// </summary>
internal static class Desktop
{
    private const string DialogClass = "#32770", ButtonClass = "Button";
    private const uint RootWindow = 2;

    /// <summary>The top-level window under a point on the screen, in pixels.</summary>
    public static nint WindowAt(int x, int y) => GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), RootWindow);

    /// <summary>The window that has the keyboard.</summary>
    public static nint Foreground() => GetForegroundWindow();

    /// <summary>How quickly a second click makes a double-click, in milliseconds, as set in Windows.</summary>
    public static long DoubleClickTime => GetDoubleClickTime();

    /// <summary>The id of the process a window belongs to, or 0 when there is no window.</summary>
    public static int ProcessOf(nint hwnd)
    {
        if (hwnd == 0)
            return 0;
        return GetWindowThreadProcessId(hwnd, out var process) == 0 ? 0 : (int)process;
    }

    /// <summary>Whether a window is a Windows message box or file picker.</summary>
    public static bool IsDialog(nint hwnd)
    {
        var text = new char[16];
        var length = GetClassName(hwnd, text, text.Length);
        return new string(text, 0, length) == DialogClass;
    }

    /// <summary>
    /// Whether the window under a point is a button of a Windows message box or file picker, such as OK, Open or
    /// Cancel: those are Win32 buttons of their own, unlike the folders and files in a picker.
    /// </summary>
    public static bool IsButtonAt(int x, int y)
    {
        var text = new char[16];
        var length = GetClassName(WindowFromPoint(new Point { X = x, Y = y }), text, text.Length);
        return new string(text, 0, length) == ButtonClass;
    }

    /// <summary>Where the mouse is, in pixels.</summary>
    public static (int X, int Y) Cursor() => GetCursorPos(out var point) ? (point.X, point.Y) : (int.MinValue, int.MinValue);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, char[] text, int length);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}
