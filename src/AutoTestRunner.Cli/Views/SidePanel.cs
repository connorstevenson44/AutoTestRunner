using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// A panel down the right of the screen, beside the app: the builder's and the run's. It stays on top, and showing it
/// leaves the app with the focus.
/// </summary>
internal abstract class SidePanel : Window
{
    private volatile nint _hwnd;

    protected SidePanel(string title)
    {
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = title;
        Width = 460;
        Topmost = true;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Background = Brushes.White;
        FontFamily = Ui.Text;
        Foreground = Ui.Ink;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width;
            Top = area.Top;
            Height = area.Height;
        };
    }

    /// <summary>The panel's window, once it has one, so a key pressed in it can be told from one pressed elsewhere. Read from any thread.</summary>
    public nint Handle => _hwnd;

    /// <summary>Where the panel is on the screen, in pixels.</summary>
    public ScreenRect? Bounds => _hwnd == 0 ? null : Native.Bounds(_hwnd);
}
