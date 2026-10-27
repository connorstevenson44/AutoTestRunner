using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace AutoTestRunner.FlaUI;

/// <summary>
/// Drives a WPF app through UI Automation with FlaUI, slowly enough to watch: it finds each control, moves the mouse
/// to it, outlines it, then uses it. The workarounds are the AutoHotkey runner's, kept here so scripts never see them:
/// rows of virtualised lists built by scrolling, buttons that only show while their row is hovered, Win32 message
/// boxes and file pickers found by window class and control id, and any other window of the app's stopping a wait
/// straight away, saying what it shows. In a manual run it never moves the mouse or presses a key: it only outlines.
/// </summary>
public sealed class FlaUiDriver : IDriver
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
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
        if (app.Arguments is { } arguments)
            start.Arguments = arguments;
        Process.Start(start);

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
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
        if (arguments is not null)
            start.Arguments = arguments;
        Process.Start(start);
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

    /// <summary>Drives an app that is open already, by its exe and its window's title; false when it isn't open.</summary>
    public bool Attach(string exe, string window)
    {
        _processName = Path.GetFileNameWithoutExtension(exe);
        _windowTitle = window;
        _main = AppWindows().FirstOrDefault(hwnd => Native.Title(hwnd) == window);
        return _main != 0;
    }

    /// <summary>
    /// Drives another of the app's windows once the one driven has closed, as when a sign-in window opens the main
    /// window. Nothing changes while the window driven is still open.
    /// </summary>
    public void Follow(nint window)
    {
        if (!Native.Exists(_main) && AppWindows().Contains(window))
            (_main, _windowTitle) = (window, Native.Title(window));
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

    // ===== Finding controls =====

    public IControl Find(Target target, TimeSpan timeout)
    {
        var retried = false;
        while (true)
        {
            var end = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Environment.TickCount64 < end)
            {
                _control.Gate();
                if (TryFindElement(target, scroll: true) is { } found)
                    return new FlaUiControl(found);
                // In a manual run another window may be the person's doing, and they can close it.
                if (!_control.Manual && target.Selectors.All(selector => selector.Within is null) && OtherWindow() is { } other)
                    throw new StuckException($"find {target.What}", $"{_windowTitle} is showing another window instead: {other}");
                _control.Wait(200);
            }
            // A button that only shows while the mouse is over its row: point at the row again, in case the mouse
            // moved off it or the row was scrolled meanwhile, and look once more.
            var anchor = target.Selectors.Select(selector => selector.SameRowAs ?? selector.Within).FirstOrDefault(scope => scope is not null);
            if (retried || anchor is null || TryFindElement(anchor, scroll: false) is not { } row)
                throw new StuckException($"find {target.What}");
            retried = true;
            Point(new FlaUiControl(row), "");
            _control.Wait(300);
        }
    }

    public IControl? TryFind(Target target) => TryFindElement(target, scroll: false) is { } found ? new FlaUiControl(found) : null;

    public void WaitGone(Target target, TimeSpan timeout)
    {
        var end = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < end)
        {
            _control.Gate();
            if (TryFindElement(target, scroll: false) is null)
                return;
            _control.Wait(200);
        }
        throw new StuckException($"see {target.What} go");
    }

    private AutomationElement? TryFindElement(Target target, bool scroll)
    {
        foreach (var selector in target.Selectors)
        {
            if (TryFindElement(selector) is { } found)
                return found;
            if (scroll && selector.ScrollToFind && ScrollTo(selector) is { } built)
                return built;
        }
        return null;
    }

    private AutomationElement? TryFindElement(Selector selector) => Shown(selector, element => element.FirstOrDefault());

    /// <summary>
    /// How many controls on screen a target finds, by the first of its selectors that finds any: 1 means it finds
    /// just the one meant. The builder asks, to choose how a step finds its control.
    /// </summary>
    public int Count(Target target)
    {
        foreach (var selector in target.Selectors)
            if (Shown(selector, found => found.Take(50).Count()) is > 0 and var count)
                return count;
        return 0;
    }

    // The controls on screen a selector matches, given to pick: the first, or how many. When the tree changes while it
    // is read, as it does while a view is built, it finds nothing, and the next look will see it.
    private T? Shown<T>(Selector selector, Func<IEnumerable<AutomationElement>, T> pick)
    {
        try
        {
            if (selector.Type == "Window")
                return pick(AppWindows().Where(hwnd => hwnd != _main && (selector.Name is null || selector.NameMatches(Native.Title(hwnd))))
                    .Select(hwnd => _automation.FromHandle(hwnd)));

            AutomationElement root;
            if (selector.Within is { } within)
            {
                if (TryFindElement(within, scroll: false) is not { } scope)
                    return default;
                root = scope;
            }
            else
                root = MainWindow();

            if (selector.Cell is { } cell)
                return pick(Cells(root, cell));

            var candidates = root.FindAllDescendants(Condition(selector)).Where(element => Fits(selector, element));
            if (selector.SameRowAs is { } rowOf)
            {
                if (TryFindElement(rowOf, scroll: false) is not { } anchor)
                    return default;
                var row = anchor.BoundingRectangle;
                var middle = (row.Top + row.Bottom) / 2;
                candidates = MainWindow().FindAllDescendants(Condition(selector))
                    .Where(element => Fits(selector, element))
                    .Where(element => IsShown(element) && Math.Abs((element.BoundingRectangle.Top + element.BoundingRectangle.Bottom) / 2 - middle) <= 14);
            }
            return pick(candidates.Where(IsShown));
        }
        catch (StuckException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        {
            return default;
        }
    }

    // A grid's cell, in the row one of whose cells says what the selector's row does, under its column's header: in the
    // grid it is inside, or the grids inside that.
    private IEnumerable<AutomationElement> Cells(AutomationElement root, Scripting.GridCell cell)
    {
        var grids = root.FindAllDescendants(Conditions.ByControlType(ControlType.DataGrid).Or(Conditions.ByControlType(ControlType.Table)));
        foreach (var grid in grids.Prepend(root).Where(grid => grid.Patterns.Grid.IsSupported))
        {
            var pattern = grid.Patterns.Grid.Pattern;
            var column = ColumnIndex(grid, cell.Column);
            if (column < 0)
                continue;
            var (rows, columns) = (pattern.RowCount.ValueOrDefault, pattern.ColumnCount.ValueOrDefault);
            for (var row = 0; row < rows; row++)
            {
                if (Enumerable.Range(0, columns).Any(index => pattern.GetItem(row, index) is { } item && CellText(item) == cell.Row)
                    && pattern.GetItem(row, column) is { } found)
                {
                    yield return found;
                    break;
                }
            }
        }
    }

    private static int ColumnIndex(AutomationElement grid, string header)
    {
        var headers = grid.Patterns.Table.IsSupported
            ? grid.Patterns.Table.Pattern.ColumnHeaders.ValueOrDefault ?? []
            : grid.FindAllDescendants(grid.ConditionFactory.ByControlType(ControlType.HeaderItem));
        return Array.FindIndex(headers, item => Name(item).Trim() == header);
    }

    // What a cell says: its value, its name, or the text inside it.
    private static string CellText(AutomationElement cell)
    {
        if (cell.Patterns.Value.IsSupported && cell.Patterns.Value.Pattern.Value.ValueOrDefault is { Length: > 0 } value)
            return value;
        if (Name(cell) is { Length: > 0 } name)
            return name;
        return cell.FindFirstDescendant(cell.ConditionFactory.ByControlType(ControlType.Text)) is { } text ? Name(text) : "";
    }

    // Names and help texts compared by startsWith or contains are read from each control, as UI Automation can only
    // compare them exactly.
    private static bool Fits(Selector selector, AutomationElement element) =>
        selector.Match == NameMatch.Exact || selector.TextMatches(Name(element), HelpText(element));

    // Exact names are matched by UI Automation itself, which is much faster than reading every name.
    private ConditionBase Condition(Selector selector)
    {
        var parts = new List<ConditionBase>();
        if (selector.Type is { } type)
            parts.Add(Conditions.ByControlType(Enum.Parse<ControlType>(type, ignoreCase: true)));
        if (selector.Id is { } id)
            parts.Add(Conditions.ByAutomationId(id));
        if (selector.HelpText is { } help && selector.Match == NameMatch.Exact)
            parts.Add(Conditions.ByHelpText(help));
        if (selector.ClassName is { } className)
            parts.Add(Conditions.ByClassName(className));
        if (selector.Name is { } name && selector.Match == NameMatch.Exact)
            parts.Add(Conditions.ByName(name));
        return parts.Count switch
        {
            0 => TrueCondition.Default,
            1 => parts[0],
            _ => new AndCondition(parts.ToArray()),
        };
    }

    private static string Name(AutomationElement element)
    {
        try
        {
            return element.Properties.Name.ValueOrDefault ?? "";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "";
        }
    }

    private static string HelpText(AutomationElement element)
    {
        try
        {
            return element.Properties.HelpText.ValueOrDefault ?? "";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "";
        }
    }

    private static bool IsShown(AutomationElement element)
    {
        try
        {
            var box = element.BoundingRectangle;
            return box.Width > 0 && box.Height > 0;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    // Scrolls each long list in the window, up to its top and then down to its end, until something that matches is
    // built. A list is at its end once a few steps build no new rows.
    private AutomationElement? ScrollTo(Selector selector)
    {
        foreach (var list in MainWindow().FindAllDescendants(Conditions.ByControlType(ControlType.List)))
        {
            Rectangle box;
            try
            {
                box = list.BoundingRectangle;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                continue;
            }
            if (box.Height < 120)
                continue;
            var scroller = list.Patterns.Scroll.IsSupported && list.Patterns.Scroll.Pattern.VerticallyScrollable.ValueOrDefault ? list : null;
            foreach (var down in new[] { false, true })
            {
                string last = "";
                var still = 0;
                for (var step = 0; step < 400 && still < 5; step++)
                {
                    if (TryFindElement(selector with { Within = null, ScrollToFind = false }) is { } found)
                        return found;
                    var shown = string.Join("|", list.FindAllDescendants(Conditions.ByControlType(ControlType.Text)).Select(Name));
                    still = shown == last ? still + 1 : 0;
                    last = shown;
                    ScrollStep(scroller, down, Rect(box), page: true);
                }
            }
        }
        return null;
    }

    // ===== Using controls =====

    public void Point(IControl control, string what)
    {
        var element = ((FlaUiControl)control).Element;
        try
        {
            if (element.Patterns.ScrollItem.IsSupported)
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Not every row can scroll itself into view; BringIntoView does the rest.
        }
        BringIntoView(element);
        var box = Rect(element.BoundingRectangle);
        _keepClear(box);
        // The person's mouse, in a manual run, and the run's view keeps the outline: FlaUI's outline is four windows
        // made and thrown away each time, which under a mouse that is moving makes it flicker.
        if (_control.Manual)
            return;
        Mouse.MovePixelsPerMillisecond = _control.MouseSpeed == 0 ? 10_000 : 30.0 / _control.MouseSpeed;
        Mouse.MoveTo(Middle(box));
        if (_control.Unattended)
            return;
        try
        {
            element.DrawHighlight(false, Color.DarkOrange, TimeSpan.FromMilliseconds(_control.Pace + 500));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The outline is a courtesy; the step goes on without it.
        }
        _control.Wait(_control.Pace);
    }

    // Scrolls the lists a control is in until all of it is in view, or its middle when it is taller than the list.
    // Half a row isn't enough: the buttons that show when the mouse is over a row sit in its middle.
    private void BringIntoView(AutomationElement element)
    {
        var last = int.MinValue;
        var still = 0;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            ScreenRect view, box;
            AutomationElement? list;
            try
            {
                view = ScrolledArea(element, out list);
                box = Rect(element.BoundingRectangle);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Log($"Couldn't scroll to a control, as it couldn't be told where it is: {ex.Message}");
                return;
            }
            if (list is null || Fits(box, view))
                return;
            if (view.Bottom - view.Top < 10)
            {
                Log("Couldn't scroll to a control, as the lists it is in leave no room to show it.");
                return;
            }
            still = box.Top == last ? still + 1 : 0;
            if (still == 3)
            {
                Log("Couldn't scroll to a control, as scrolling didn't move it.");
                return;
            }
            last = box.Top;
            ScrollStep(list, box.Bottom > view.Bottom, view, page: false);
        }
    }

    private static bool Fits(ScreenRect box, ScreenRect view)
    {
        if (box.Bottom - box.Top > view.Bottom - view.Top)
        {
            var middle = (box.Top + box.Bottom) / 2;
            return middle >= view.Top && middle <= view.Bottom;
        }
        return box.Top >= view.Top && box.Bottom <= view.Bottom;
    }

    // The part of the screen the lists a control is in leave it, and the innermost of them, which is the one to
    // scroll. Only containers that scroll up and down count.
    private static ScreenRect ScrolledArea(AutomationElement element, out AutomationElement? list)
    {
        var area = new ScreenRect(-100_000, -100_000, 100_000, 100_000);
        list = null;
        for (var parent = element.Parent; parent is not null && parent.Properties.ControlType.ValueOrDefault != ControlType.Window; parent = parent.Parent)
        {
            bool scrolls;
            try
            {
                scrolls = parent.Patterns.Scroll.IsSupported && parent.Patterns.Scroll.Pattern.VerticallyScrollable.ValueOrDefault;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                scrolls = false;
            }
            if (!scrolls)
                continue;
            var box = Rect(parent.BoundingRectangle);
            area = new ScreenRect(Math.Max(area.Left, box.Left), Math.Max(area.Top, box.Top), Math.Min(area.Right, box.Right), Math.Min(area.Bottom, box.Bottom));
            list ??= parent;
        }
        return area;
    }

    // Scrolls a list a line, or a page, through UI Automation where the list can, so it works whatever window has the
    // focus. WPF refuses the whole call unless the other direction is given as NoAmount. Otherwise with the wheel.
    private void ScrollStep(AutomationElement? list, bool down, ScreenRect area, bool page)
    {
        if (list is not null)
        {
            var amount = (page, down) switch
            {
                (true, true) => ScrollAmount.LargeIncrement,
                (true, false) => ScrollAmount.LargeDecrement,
                (false, true) => ScrollAmount.SmallIncrement,
                _ => ScrollAmount.SmallDecrement,
            };
            try
            {
                list.Patterns.Scroll.Pattern.Scroll(ScrollAmount.NoAmount, amount);
                Thread.Sleep(120);
                return;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
            {
                if (!_wheelNoted)
                {
                    _wheelNoted = true;
                    Log($"Scrolling through UI Automation failed, so the mouse wheel is used: {ex.Message}");
                }
            }
        }
        // The mouse is the person's in a manual run.
        if (_control.Manual)
            return;
        var spot = new ScreenRect(area.Left + 20, (area.Top + area.Bottom) / 2 - 5, area.Left + 40, (area.Top + area.Bottom) / 2 + 5);
        _keepClear(spot);
        Mouse.MoveTo(Middle(spot));
        Native.Activate(_main);
        Mouse.Scroll(down ? -1 : 1);
        Thread.Sleep(120);
    }

    public void Click(IControl control, ClickHow how)
    {
        var element = ((FlaUiControl)control).Element;
        if (how == ClickHow.Invoke)
        {
            if (element.Patterns.Invoke.IsSupported)
            {
                element.Patterns.Invoke.Pattern.Invoke();
                return;
            }
            if (element.Patterns.Toggle.IsSupported)
            {
                element.Patterns.Toggle.Pattern.Toggle();
                return;
            }
            if (element.Patterns.SelectionItem.IsSupported)
            {
                element.Patterns.SelectionItem.Pattern.Select();
                return;
            }
        }
        Native.Activate(_main);
        var middle = Middle(Rect(element.BoundingRectangle));
        switch (how)
        {
            case ClickHow.Right:
                Mouse.RightClick(middle);
                break;
            case ClickHow.Double:
                Mouse.DoubleClick(middle);
                break;
            default:
                Mouse.LeftClick(middle);
                break;
        }
    }

    // In small moves, so the app sees a drag start before the control arrives.
    public void Drag(IControl control, IControl onto)
    {
        var from = Middle(Rect(((FlaUiControl)control).Element.BoundingRectangle));
        var box = Rect(((FlaUiControl)onto).Element.BoundingRectangle);
        _keepClear(box);
        Native.Activate(_main);
        Mouse.MoveTo(from);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(150);
        Mouse.MoveTo(new Point(from.X + 12, from.Y + 12));
        Thread.Sleep(150);
        Mouse.MoveTo(Middle(box));
        Thread.Sleep(250);
        Mouse.Up(MouseButton.Left);
        Wait.UntilInputIsProcessed();
    }

    public void Menu(IReadOnlyList<string> path)
    {
        for (var index = 0; index < path.Count; index++)
        {
            var item = WaitMenuItem(path[index]);
            Point(new FlaUiControl(item), path[index]);
            if (index < path.Count - 1 && item.Patterns.ExpandCollapse.IsSupported)
                item.Patterns.ExpandCollapse.Pattern.Expand();
            else if (item.Patterns.Invoke.IsSupported)
                item.Patterns.Invoke.Pattern.Invoke();
            else
                Mouse.LeftClick(Middle(Rect(item.BoundingRectangle)));
            _control.Wait(250);
        }
    }

    private AutomationElement WaitMenuItem(string name)
    {
        var end = Environment.TickCount64 + (long)_control.Scale(TimeSpan.FromSeconds(10)).TotalMilliseconds;
        while (true)
        {
            _control.Gate();
            var items = MenuElements();
            if (items.FirstOrDefault(item => Name(item) == name) is { } found)
                return found;
            if (Environment.TickCount64 > end)
                throw new StuckException($"find {name} in the menu", items.Count == 0 ? "No menu is open." : $"The menu has: {string.Join(", ", items.Select(Name).Where(text => text.Length > 0))}");
            _control.Wait(200);
        }
    }

    public IReadOnlyList<IControl> MenuItems() => [.. MenuElements().Select(item => new FlaUiControl(item))];

    // The menu items on screen: in the main window's menu bar, or in a menu open in a popup window of its own.
    private List<AutomationElement> MenuElements()
    {
        var ids = ProcessIds();
        var condition = Conditions.ByControlType(ControlType.MenuItem);
        try
        {
            return [.. _automation.GetDesktop().FindAllChildren()
                .Where(top => ids.Contains(top.Properties.ProcessId.ValueOrDefault))
                .SelectMany(top => top.FindAllDescendants(condition))
                .Where(IsShown)];
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        {
            return [];
        }
    }

    public void Expand(IControl control)
    {
        var element = ((FlaUiControl)control).Element;
        if (element.Patterns.ExpandCollapse.IsSupported)
        {
            if (element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault != ExpandCollapseState.Expanded)
                element.Patterns.ExpandCollapse.Pattern.Expand();
            return;
        }
        Native.Activate(_main);
        Mouse.DoubleClick(Middle(Rect(element.BoundingRectangle)));
    }

    public void SetValue(IControl control, string value)
    {
        var element = ((FlaUiControl)control).Element;
        if (element.Patterns.RangeValue.IsSupported && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            element.Patterns.RangeValue.Pattern.SetValue(number);
            return;
        }
        // A date picker's text is in the box inside it.
        var box = element.Patterns.Value.IsSupported ? element : element.FindFirstDescendant(Conditions.ByControlType(ControlType.Edit));
        if (box is null || !box.Patterns.Value.IsSupported)
            throw new StuckException($"set {Name(element)} to {value}", "It has no value that can be set: it isn't a slider, a number box or a box to type in.");
        box.Patterns.Value.Pattern.SetValue(value);
    }

    public string? ValueOf(IControl control)
    {
        var element = ((FlaUiControl)control).Element;
        if (element.Patterns.RangeValue.IsSupported)
            return element.Patterns.RangeValue.Pattern.Value.ValueOrDefault.ToString(CultureInfo.InvariantCulture);
        var box = element.Patterns.Value.IsSupported ? element : element.FindFirstDescendant(Conditions.ByControlType(ControlType.Edit));
        return box is not null && box.Patterns.Value.IsSupported ? box.Patterns.Value.Pattern.Value.ValueOrDefault : null;
    }

    // A grid's rows, or the items in a list or tree: all of them, including those scrolled out of view.
    public int Rows(IControl control)
    {
        var element = ((FlaUiControl)control).Element;
        if (element.Patterns.Grid.IsSupported)
            return element.Patterns.Grid.Pattern.RowCount.ValueOrDefault;
        return element.FindAllChildren(Conditions.ByControlType(ControlType.ListItem).Or(Conditions.ByControlType(ControlType.DataItem))
            .Or(Conditions.ByControlType(ControlType.TreeItem))).Length;
    }

    // A key at a time so it can be watched, then Tab so the value is taken.
    public void Type(IControl control, string text, bool secret)
    {
        var element = ((FlaUiControl)control).Element;
        try
        {
            element.Focus();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Mouse.LeftClick(Middle(Rect(element.BoundingRectangle)));
        }
        Thread.Sleep(150);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        if (text.Length == 0)
            Keyboard.Type(VirtualKeyShort.DELETE);
        foreach (var character in text)
        {
            Keyboard.Type(character);
            Thread.Sleep(_control.KeyDelay);
        }
        Wait.UntilInputIsProcessed();
        if (!secret && element.Patterns.Value.IsSupported && element.Patterns.Value.Pattern.Value.ValueOrDefault != text)
        {
            Log("Typing didn't land in full, so the value was set directly");
            element.Patterns.Value.Pattern.SetValue(text);
        }
        Keyboard.Type(VirtualKeyShort.TAB);
    }

    public void Choose(IControl control, string option)
    {
        var element = ((FlaUiControl)control).Element;
        element.Patterns.ExpandCollapse.Pattern.Expand();
        _control.Wait(_control.Pace);
        // A WPF drop-down's items are inside it, wherever its window is, even while its list shows in a popup.
        var condition = Conditions.ByControlType(ControlType.ListItem).And(Conditions.ByName(option));
        var end = Environment.TickCount64 + (long)_control.Scale(TimeSpan.FromSeconds(10)).TotalMilliseconds;
        AutomationElement? item;
        while ((item = element.FindFirstDescendant(condition)) is null)
        {
            if (Environment.TickCount64 > end)
                throw new StuckException($"find the {option} option", $"The drop-down has: {string.Join(", ", element.FindAllDescendants(Conditions.ByControlType(ControlType.ListItem)).Select(Name))}");
            _control.Wait(200);
        }
        Point(new FlaUiControl(item), option);
        item.Patterns.SelectionItem.Pattern.Select();
        try
        {
            element.Patterns.ExpandCollapse.Pattern.Collapse();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Picking an item closes most drop-downs by itself.
        }
    }

    public void SetChecked(IControl control, bool isChecked)
    {
        var toggle = ((FlaUiControl)control).Element.Patterns.Toggle.Pattern;
        if ((toggle.ToggleState.Value == ToggleState.On) != isChecked)
            toggle.Toggle();
    }

    // The text goes in in one go, as a paste would, so an editor that marks changes marks it as one.
    public bool ReplaceText(IControl control, string pattern, string replacement)
    {
        var value = ((FlaUiControl)control).Element.Patterns.Value.Pattern;
        var text = value.Value.ValueOrDefault ?? "";
        var regex = new Regex(pattern, RegexOptions.Multiline);
        if (!regex.IsMatch(text))
            return false;
        value.SetValue(regex.Replace(text, replacement, 1));
        return true;
    }

    public void Key(string keys)
    {
        var parts = keys.Split('+', StringSplitOptions.TrimEntries).Select(KeyCode).ToArray();
        if (parts.Length == 1)
            Keyboard.Type(parts[0]);
        else
            Keyboard.TypeSimultaneously(parts);
    }

    private static VirtualKeyShort KeyCode(string key) => key.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => VirtualKeyShort.CONTROL,
        "ALT" => VirtualKeyShort.ALT,
        "SHIFT" => VirtualKeyShort.SHIFT,
        "ENTER" or "RETURN" => VirtualKeyShort.RETURN,
        "ESC" or "ESCAPE" => VirtualKeyShort.ESCAPE,
        "TAB" => VirtualKeyShort.TAB,
        "DEL" or "DELETE" => VirtualKeyShort.DELETE,
        "SPACE" => VirtualKeyShort.SPACE,
        "BACKSPACE" => VirtualKeyShort.BACK,
        "INS" => VirtualKeyShort.INSERT,
        "PAGEUP" or "PGUP" => VirtualKeyShort.PRIOR,
        "PAGEDOWN" or "PGDN" => VirtualKeyShort.NEXT,
        "WIN" or "WINDOWS" => VirtualKeyShort.LWIN,
        { Length: 1 } letter when char.IsLetterOrDigit(letter[0]) => Enum.Parse<VirtualKeyShort>("KEY_" + letter),
        var other => Enum.TryParse<VirtualKeyShort>(other, out var code) ? code : throw new ScriptException($"There is no key called {key}."),
    };

    // ===== Other windows: message boxes and file pickers =====

    // One of the app's windows by its title. A dialog is a Windows message box or file picker; otherwise one of the
    // app's own windows.
    private nint FindWindow(string title, bool dialog) =>
        AppWindows().FirstOrDefault(hwnd => hwnd != _main && Native.Title(hwnd) == title && (Native.ClassName(hwnd) == Native.DialogClass) == dialog);

    private nint WaitWindow(string title, bool dialog, string what, TimeSpan timeout)
    {
        var end = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < end)
        {
            _control.Gate();
            if (FindWindow(title, dialog) is var hwnd and not 0)
                return hwnd;
            if (OtherWindowHandle() is var other and not 0 && Native.Title(other) != title)
                throw new StuckException($"see {what}", $"{_windowTitle} is showing another window instead: {Describe(other)}");
            _control.Wait(200);
        }
        throw new StuckException($"see {what}");
    }

    public string WaitDialog(string title, TimeSpan timeout)
    {
        var hwnd = WaitWindow(title, dialog: true, $"the \"{title}\" message", timeout);
        return MessageText(hwnd);
    }

    internal string MessageText(nint hwnd)
    {
        var texts = _automation.FromHandle(hwnd).FindAllDescendants(Conditions.ByControlType(ControlType.Text)).Select(Name).Where(text => text.Length > 0);
        return Regex.Replace(string.Join(" ", texts), @"\s+", " ").Trim();
    }

    public void PressDialogButton(string title, DialogButton button)
    {
        var hwnd = FindWindow(title, dialog: true);
        if (hwnd == 0)
            throw new StuckException($"see the \"{title}\" message");
        var dialog = _automation.FromHandle(hwnd);
        // Message box buttons have their command ids as automation ids, whatever the language.
        var found = Find(new Target([new Selector(Id: ((int)button).ToString(CultureInfo.InvariantCulture), Type: "Button", Within: new Target([new Selector(Type: "Window", Name: title)], title))],
            $"the {button} button"), _control.Scale(TimeSpan.FromSeconds(5)));
        Point(found, button.ToString());
        Click(found, ClickHow.Invoke);
        var end = Environment.TickCount64 + 10_000;
        while (Native.IsVisible(hwnd) && Environment.TickCount64 < end)
            Thread.Sleep(100);
        GC.KeepAlive(dialog);
    }

    // The name box is control 1148 in a file picker, 1152 in a folder picker, and Open or Select Folder is control 1.
    // A folder picker's first Select Folder can just go into the typed folder, so it may take more than one.
    public void PickFile(string title, string path, bool folder)
    {
        var hwnd = WaitWindow(title, dialog: true, $"the {title} picker", _control.Scale(TimeSpan.FromSeconds(30)));
        var window = new Target([new Selector(Type: "Window", Name: title)], title);
        var box = Find(new Target([new Selector(Id: folder ? "1152" : "1148", Type: "Edit", Within: window)], "the name box"), _control.Scale(TimeSpan.FromSeconds(10)));
        Point(box, path);
        Type(box, path, secret: false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (TryFind(new Target([new Selector(Id: "1", Type: "Button", Within: window)], "Open")) is { } open)
            {
                Point(open, "Open it");
                Click(open, ClickHow.Invoke);
            }
            var end = Environment.TickCount64 + 3_000;
            while (Native.IsVisible(hwnd) && Environment.TickCount64 < end)
                Thread.Sleep(100);
            if (!Native.IsVisible(hwnd))
                return;
        }
        throw new StuckException($"pick {path} in the {title} picker");
    }

    public string? OtherWindow() => OtherWindowHandle() is var hwnd and not 0 ? Describe(hwnd) : null;

    // Any visible window of the app's with a title, other than its main one: a message box, a dialog, a step's inputs.
    private nint OtherWindowHandle() =>
        _processName.Length == 0 ? 0 : AppWindows().FirstOrDefault(hwnd => hwnd != _main && Native.Title(hwnd).Length > 0);

    private string Describe(nint hwnd)
    {
        var text = "";
        try
        {
            text = MessageText(hwnd);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Described by its title alone.
        }
        return $"\"{Native.Title(hwnd)}\": {(text.Length > 400 ? text[..400] : text)}";
    }

    // ===== Diagnostics =====

    public void Screenshot(string path) => global::FlaUI.Core.Capturing.Capture.Screen().ToFile(path);

    public string DumpTree()
    {
        var text = new StringBuilder();
        var walker = _automation.TreeWalkerFactory.GetControlViewWalker();
        foreach (var hwnd in AppWindows())
        {
            text.Append(CultureInfo.InvariantCulture, $"===== {Native.Title(hwnd)} ({Native.ClassName(hwnd)})\r\n");
            var count = 0;
            void Walk(AutomationElement element, int depth)
            {
                if (++count > 5000 || depth > 60)
                    return;
                string line;
                try
                {
                    var box = element.BoundingRectangle;
                    var properties = element.Properties;
                    line = $"{properties.ControlType.ValueOrDefault} \"{properties.Name.ValueOrDefault}\""
                        + (properties.AutomationId.ValueOrDefault is { Length: > 0 } id ? $" id={id}" : "")
                        + (properties.HelpText.ValueOrDefault is { Length: > 0 } help ? $" help=\"{help}\"" : "")
                        + (properties.ClassName.ValueOrDefault is { Length: > 0 } className ? $" class={className}" : "")
                        + $" ({box.Left},{box.Top},{box.Right},{box.Bottom})";
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    line = $"(couldn't be read: {ex.Message})";
                }
                text.Append(' ', depth * 2).Append(line).Append("\r\n");
                for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                    Walk(child, depth + 1);
            }
            try
            {
                Walk(_automation.FromHandle(hwnd), 0);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                text.Append(CultureInfo.InvariantCulture, $"(the window couldn't be read: {ex.Message})\r\n");
            }
        }
        return text.ToString();
    }

    /// <summary>Whether the keyboard is in a text box that can be typed in, in any app, so the panel's keys stay out of it.</summary>
    public bool IsTyping()
    {
        try
        {
            var focused = _automation.FocusedElement();
            var type = focused?.Properties.ControlType.ValueOrDefault;
            return (type == ControlType.Edit || type == ControlType.Document)
                && !(focused!.Patterns.Value.IsSupported && focused.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private static ScreenRect Rect(Rectangle box) => new(box.Left, box.Top, box.Right, box.Bottom);

    private static Point Middle(ScreenRect box) => new((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);

    public void Dispose() => _automation.Dispose();
}
