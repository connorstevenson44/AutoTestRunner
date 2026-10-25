using System.Runtime.InteropServices;
using System.Text;
using Walkthrough.Driving;

namespace Walkthrough.FlaUI;

/// <summary>
/// The Win32 calls UI Automation doesn't cover: listing an app's top-level windows (message boxes and file pickers are
/// Win32 windows of class #32770, not WPF), placing the app's window, and the screen's work area.
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
            GetWindowThreadProcessId(hwnd, out var processId);
            if (processIds.Contains((int)processId) && IsWindowVisible(hwnd))
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
        var text = new StringBuilder(length + 1);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    public static string ClassName(nint hwnd)
    {
        var text = new StringBuilder(256);
        GetClassName(hwnd, text, text.Capacity);
        return text.ToString();
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int length);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder text, int length);

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
