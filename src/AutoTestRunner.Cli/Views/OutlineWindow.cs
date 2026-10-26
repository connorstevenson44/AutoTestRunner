using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// An amber outline around a control of the app, with a label under it saying how a step will find it, as the run
/// outlines each control it uses; or a grey one saying why the builder's pick tool can't be used on it. The mouse goes straight through it to the app. It is placed in screen pixels, as UI
/// Automation gives them, whatever the screen's scale.
/// </summary>
internal sealed class OutlineWindow : Window
{
    private const int Gap = 3;

    private readonly Border _box;
    private readonly Border _label;
    private readonly Border _dot;
    private readonly TextBlock _text;
    private nint _hwnd;

    public OutlineWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        _box = new Border { BorderBrush = Ui.Amber, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3) };
        _dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        _text = new TextBlock { Foreground = Brushes.White, FontFamily = Ui.Mono, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(_dot);
        line.Children.Add(_text);
        _label = new Border
        {
            Background = Ui.Ink, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3, 8, 3), Child = line,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_label, 1);
        grid.Children.Add(_box);
        grid.Children.Add(_label);
        Content = grid;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Clicks and the mouse go through to whatever is under it, and it never takes the focus.
            _ = SetWindowLong(_hwnd, ExtendedStyle, GetWindowLong(_hwnd, ExtendedStyle) | Transparent | ToolWindow | NoActivate);
        };
    }

    /// <summary>Outlines a place on the screen, with how it is found under it; null takes the label away.</summary>
    public void Show(ScreenRect box, string? label, TargetQuality quality = TargetQuality.Sturdy)
    {
        _box.BorderBrush = Ui.Amber;
        Place(box, label, Quality(quality));
    }

    /// <summary>Outlines a control in grey, with why the pick tool can't be used on it under it.</summary>
    public void Refuse(ScreenRect box, string why)
    {
        _box.BorderBrush = Ui.Faint;
        Place(box, why, Ui.Fail);
    }

    private void Place(ScreenRect box, string? label, Brush dot)
    {
        _label.Visibility = label is null ? Visibility.Collapsed : Visibility.Visible;
        _text.Text = label ?? "";
        _dot.Background = dot;
        if (!IsVisible)
            Show();
        // Placed in pixels: the outline sits just outside the control, and the label below it.
        var scale = VisualTreeHelper.GetDpi(this);
        var labelHeight = label is null ? 0 : (int)Math.Ceiling(24 * scale.DpiScaleY);
        // Wide enough for the label's words, at about 7 units a letter in its font, beside the dot.
        var labelWidth = Math.Max(260, 7 * (label?.Length ?? 0) + 40);
        SetWindowPos(_hwnd, -1, box.Left - Gap, box.Top - Gap, Math.Max(box.Right - box.Left + 2 * Gap, (int)(labelWidth * scale.DpiScaleX)),
            box.Bottom - box.Top + 2 * Gap + labelHeight, NoActivateFlag);
        _box.Width = (box.Right - box.Left + 2 * Gap) / scale.DpiScaleX;
        _box.HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>The colour of the dot that says how sturdy a way of finding a control is.</summary>
    public static Brush Quality(TargetQuality quality) => quality switch
    {
        TargetQuality.Sturdy => Ui.Pass,
        TargetQuality.Wording => Ui.Brush("#D08700"),
        _ => Ui.Fail,
    };

    private const int ExtendedStyle = -20, Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000;
    private const uint NoActivateFlag = 0x0010;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
