using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AutoTestRunner.FlaUI;

public sealed partial class FlaUiDriver
{
    public void Point(IControl control, string what)
    {
        var element = ((FlaUiControl)control).Element;
        try
        {
            if (element.Patterns.ScrollItem.IsSupported)
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
        }
        catch (COMException)
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
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
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
            catch (COMException ex)
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
            var item = parent;
            if (!OrIfGone(() => item.Patterns.Scroll.IsSupported && item.Patterns.Scroll.Pattern.VerticallyScrollable.ValueOrDefault, false))
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
            catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
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
        catch (Exception ex) when (Gone(ex))
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
        catch (COMException)
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
        catch (COMException)
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

    // A key by its name, as KeyNames spells it; any other is a VirtualKeyShort name, such as F5 or SHIFT.
    private static VirtualKeyShort KeyCode(string key) => KeyNames.Canonical(key) switch
    {
        "CTRL" => VirtualKeyShort.CONTROL,
        "ENTER" => VirtualKeyShort.RETURN,
        "ESC" => VirtualKeyShort.ESCAPE,
        "BACKSPACE" => VirtualKeyShort.BACK,
        "PAGEUP" => VirtualKeyShort.PRIOR,
        "PAGEDOWN" => VirtualKeyShort.NEXT,
        "WIN" => VirtualKeyShort.LWIN,
        { Length: 1 } letter when char.IsLetterOrDigit(letter[0]) => Enum.Parse<VirtualKeyShort>("KEY_" + letter),
        var other => Enum.TryParse<VirtualKeyShort>(other, out var code) ? code : throw new ScriptException($"There is no key called {key}."),
    };
}
