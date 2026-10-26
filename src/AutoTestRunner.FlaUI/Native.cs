using System.Runtime.InteropServices;
using AutoTestRunner.Driving;

namespace AutoTestRunner.FlaUI;

/// <summary>
/// The Win32 calls UI Automation doesn't cover: listing an app's top-level windows (message boxes and file pickers are
/// Win32 windows of class #32770, not WPF) and the controls in a picker, placing the app's window, the screen's work
/// area, and a click that leaves the mouse where it was.
/// </summary>
internal static class Native
{
    public const string DialogClass = "#32770";

    /// <summary>The visible top-level windows of these processes, in z-order, front first.</summary>
    public static List<nint> Windows(IReadOnlySet<int> processIds)
    {
        var found = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            // A thread id of 0 means the window has gone.
            if (GetWindowThreadProcessId(hwnd, out var processId) != 0 && processIds.Contains((int)processId) && IsWindowVisible(hwnd))
                found.Add(hwnd);
            return true;
        }, 0);
        return found;
    }

    public static string Title(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length == 0)
            return "";
        var text = new char[length + 1];
        var copied = GetWindowText(hwnd, text, text.Length);
        return new string(text, 0, copied);
    }

    public static string ClassName(nint hwnd)
    {
        var text = new char[256];
        var copied = GetClassName(hwnd, text, text.Length);
        return new string(text, 0, copied);
    }

    /// <summary>The desktop window, the parent of every top-level window.</summary>
    public static nint Desktop { get; } = GetDesktopWindow();

    public static bool Exists(nint hwnd) => hwnd != 0 && IsWindow(hwnd);

    public static bool IsVisible(nint hwnd) => IsWindow(hwnd) && IsWindowVisible(hwnd);

    public static ScreenRect WorkArea()
    {
        var area = default(Rect);
        SystemParametersInfo(0x0030, 0, ref area, 0);
        return new ScreenRect(area.Left, area.Top, area.Right, area.Bottom);
    }

    public static ScreenRect Bounds(nint hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public static void Place(nint hwnd, ScreenRect where)
    {
        ShowWindow(hwnd, 9); // SW_RESTORE
        MoveWindow(hwnd, where.Left, where.Top, where.Right - where.Left, where.Bottom - where.Top, true);
        Activate(hwnd);
    }

    public static void Activate(nint hwnd) => SetForegroundWindow(hwnd);

    public static void Close(nint hwnd) => PostMessage(hwnd, 0x0010, 0, 0); // WM_CLOSE

    /// <summary>The child windows of a window, at any depth, of a Win32 class such as Edit or ToolbarWindow32.</summary>
    public static List<nint> Children(nint hwnd, string className)
    {
        var found = new List<nint>();
        EnumChildWindows(hwnd, (child, _) =>
        {
            if (ClassName(child) == className)
                found.Add(child);
            return true;
        }, 0);
        return found;
    }

    /// <summary>The child window of a dialog with this control id, at any depth, such as a file picker's name box; or 0.</summary>
    public static nint Control(nint dialog, int id)
    {
        nint found = 0;
        EnumChildWindows(dialog, (child, _) =>
        {
            if (GetDlgCtrlID(child) != id || !IsWindowVisible(child))
                return true;
            found = child;
            return false;
        }, 0);
        return found;
    }

    /// <summary>
    /// Clicks the left button at a point on the screen, then puts the mouse back where it is now. The three go to
    /// Windows in one SendInput, which plays them in order with nothing in between, so the person's mouse doesn't jump.
    /// </summary>
    public static void ClickAt(int x, int y)
    {
        if (!GetCursorPos(out var back))
            back = new Point { X = x, Y = y };
        // Absolute places are 0 to 65535 across all the screens, from the top left of the leftmost.
        var (left, top, width, height) = (GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        Input At(int px, int py, uint button) => new()
        {
            Type = 0, // INPUT_MOUSE
            Mouse = new MouseInput { X = Normal(px - left, width), Y = Normal(py - top, height), Flags = button | 0x0001 | 0x8000 | 0x4000 }, // move, absolute, all screens
        };
        Input[] inputs = [At(x, y, 0x0002), At(x, y, 0x0004), At(back.X, back.Y, 0)]; // left down, left up, back
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    // A pixel as an absolute place: the smallest one Windows turns back into that pixel.
    private static int Normal(int pixel, int size) => size <= 0 ? 0 : (int)(((long)pixel * 65536 + size - 1) / size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nint Extra;
    }

    // INPUT with the mouse's part of its union, the largest, so it has INPUT's size.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, [Out] char[] text, int length);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, [Out] char[] text, int length);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref Rect value, uint winIni);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(nint hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
