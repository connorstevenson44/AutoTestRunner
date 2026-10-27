using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;

namespace AutoTestRunner.FlaUI;

/// <summary>
/// Drives a WPF app through UI Automation with FlaUI, slowly enough to watch: it finds each control, moves the mouse
/// to it, outlines it, then uses it. The workarounds are the AutoHotkey runner's, kept here so scripts never see them:
/// rows of virtualised lists built by scrolling, buttons that only show while their row is hovered, Win32 message
/// boxes and file pickers found by window class and control id, and any other window of the app's stopping a wait
/// straight away, saying what it shows. In a manual run it never moves the mouse or presses a key: it only outlines.
/// </summary>
public sealed partial class FlaUiDriver : IDriver
{
    private readonly UIA3Automation _automation = new();
    private readonly RunControl _control;
    private readonly Func<ScreenRect?> _panel;
    private readonly Action<ScreenRect> _keepClear;
    private string _processName = "";
    private string _windowTitle = "";
    private nint _main;
    private bool _wheelNoted;

    /// <param name="control">The run's pause, step and stop controls, which every action waits on.</param>
    /// <param name="panel">Where the panel is, so the app is placed beside it; null when there is none.</param>
    /// <param name="keepClear">Moves the panel off a control that is about to be used.</param>
    /// <param name="log">Notes for the run's log, such as a workaround being used.</param>
    public FlaUiDriver(RunControl control, Func<ScreenRect?> panel, Action<ScreenRect> keepClear, Action<string> log)
    {
        (_control, _panel, _keepClear, Log) = (control, panel, keepClear, log);
    }

    private Action<string> Log { get; }

    private ConditionFactory Conditions => _automation.ConditionFactory;

    // ===== Starting and stopping =====

    public void Start(AppSettings app, string exe)
    {
        CloseAll(exe);
        _processName = Path.GetFileNameWithoutExtension(exe);
        // An app that signs in first opens that window; a useWindow step moves on to the main one.
        _windowTitle = app.StartWindow ?? app.Window;
        StartProcess(exe, app.Arguments);

        var end = Environment.TickCount64 + (long)_control.Scale(TimeSpan.FromSeconds(60)).TotalMilliseconds;
        while ((_main = AppWindows().FirstOrDefault(hwnd => Native.Title(hwnd) == _windowTitle)) == 0)
        {
            if (Environment.TickCount64 > end)
                throw new StuckException($"see {_windowTitle}'s window open", exe);
            _control.Wait(250);
        }
        Place();
    }

    /// <summary>
    /// Starts an exe whose window's title isn't known yet, as for a new suite, and drives the first window it shows.
    /// Returns the window's title.
    /// </summary>
    public string Launch(string exe, string? arguments = null)
    {
        CloseAll(exe);
        _processName = Path.GetFileNameWithoutExtension(exe);
        StartProcess(exe, arguments);
        var end = Environment.TickCount64 + 60_000;
        while ((_main = AppWindows().FirstOrDefault(hwnd => Native.IsVisible(hwnd) && Native.Title(hwnd).Length > 0)) == 0)
        {
            if (Environment.TickCount64 > end)
                throw new StuckException($"see a window of {Path.GetFileName(exe)} open", exe);
            Thread.Sleep(250);
        }
        _windowTitle = Native.Title(_main);
        Place();
        return _windowTitle;
    }

    // From the exe's own folder, as when it is opened from Explorer.
    private static void StartProcess(string exe, string? arguments) =>
        Process.Start(new ProcessStartInfo(exe, arguments ?? "") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();

    /// <summary>Drives an app that is open already, by its exe and its window's title; false when it isn't open.</summary>
    public bool Attach(string exe, string window)
    {
        _processName = Path.GetFileNameWithoutExtension(exe);
        _windowTitle = window;
        _main = AppWindows().FirstOrDefault(hwnd => Native.Title(hwnd) == window);
        return _main != 0;
    }

    public void UseWindow(string title, TimeSpan timeout)
    {
        var end = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        nint found;
        while ((found = AppWindows().FirstOrDefault(hwnd => Native.Title(hwnd) == title && Native.IsVisible(hwnd))) == 0)
        {
            _control.Gate();
            if (Environment.TickCount64 > end)
                throw new StuckException($"see the {title} window open", OtherWindow() is { } other ? $"The app is showing another window instead: {other}" : null);
            _control.Wait(250);
        }
        (_main, _windowTitle) = (found, title);
        Place();
    }

    // Puts the app beside the panel, so the panel never covers it on a wide enough screen.
    private void Place()
    {
        var area = Native.WorkArea();
        var width = area.Right - area.Left;
        if (_panel() is { } panel && width - (panel.Right - panel.Left) >= 1000)
            width -= panel.Right - panel.Left;
        Native.Place(_main, area with { Right = area.Left + width });
        _control.Wait(300);
    }

    public void Close()
    {
        if (!Native.Exists(_main))
            return;
        Native.Close(_main);
        var end = Environment.TickCount64 + 10_000;
        while (Native.Exists(_main) && Environment.TickCount64 < end)
            Thread.Sleep(200);
        if (Native.Exists(_main))
            CloseAll(_processName);
    }

    public void CloseAll(string exe)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // It ended by itself meanwhile, or belongs to someone else.
            }
        }
    }

    /// <summary>For the builder's inspector: UI Automation, the app's main window and its processes.</summary>
    internal UIA3Automation Automation => _automation;

    internal nint Main => _main;

    internal IReadOnlySet<int> ProcessIds()
    {
        var processes = Process.GetProcessesByName(_processName);
        try
        {
            return processes.Select(process => process.Id).ToHashSet();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    internal List<nint> AppWindows() => Native.Windows(ProcessIds());

    private AutomationElement MainWindow()
    {
        if (!Native.Exists(_main))
            throw new StuckException($"find {_windowTitle}'s window", "It has closed. If it crashed, look for a crash report in its logs.");
        return _automation.FromHandle(_main);
    }

    /// <summary>Whether an error means a control or window went away while it was read, as they do while the app changes.</summary>
    internal static bool Gone(Exception ex) => ex is COMException or InvalidOperationException or TimeoutException;

    // What a control says about itself, or otherwise when it went away while it was being read.
    private static T OrIfGone<T>(Func<T> read, T otherwise)
    {
        try
        {
            return read();
        }
        catch (COMException)
        {
            return otherwise;
        }
    }

    private static ScreenRect Rect(Rectangle box) => new(box.Left, box.Top, box.Right, box.Bottom);

    private static Point Middle(ScreenRect box) => new((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);

    public void Dispose() => _automation.Dispose();
}
