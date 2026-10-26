using System.Drawing;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace AutoTestRunner.FlaUI;

/// <summary>
/// What the builder asks of the app while a person uses it: which control is under the mouse, what it says about
/// itself and the controls around it, what a box holds and whether a tick box is ticked. Only the app's own controls
/// count, not the builder's or another program's.
/// </summary>
public sealed class FlaUiInspector(FlaUiDriver driver)
{
    // How far up the tree a control's ancestors are read: far enough for a row in a list in a tab.
    private const int MostAncestors = 12;

    /// <summary>The ids of the processes running the app's exe, whose windows are the app's.</summary>
    public IReadOnlySet<int> Processes() => driver.ProcessIds();

    /// <summary>The app's control at a point on the screen, in pixels, or null when the point isn't on the app.</summary>
    public InspectedControl? At(int x, int y)
    {
        try
        {
            var element = driver.Automation.FromPoint(new Point(x, y));
            return element is null || !driver.ProcessIds().Contains(element.Properties.ProcessId.ValueOrDefault) ? null : Inspect(element);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>The app's control that has the keyboard, or null when another program has it.</summary>
    public InspectedControl? Focused()
    {
        try
        {
            var element = driver.Automation.FocusedElement();
            return element is null || !driver.ProcessIds().Contains(element.Properties.ProcessId.ValueOrDefault) ? null : Inspect(element);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>The app's control that has the keyboard, when it is a box that can be typed in.</summary>
    public InspectedControl? TypingIn()
    {
        if (Focused() is not { } focused || focused.Facts.Type is not (nameof(ControlType.Edit) or nameof(ControlType.Document)))
            return null;
        try
        {
            return focused.Element.Patterns.Value.IsSupported && focused.Element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault ? null : focused;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>The control as it is now: its text, value and tick, read again.</summary>
    public InspectedControl Again(InspectedControl control)
    {
        try
        {
            return Inspect(control.Element);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        {
            return control;
        }
    }

    /// <summary>What a box holds now, or null when it has gone.</summary>
    public static string? ValueOf(InspectedControl control)
    {
        try
        {
            return control.Element.Patterns.Value.IsSupported ? control.Element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "" : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>Where a target's control is on the screen, to outline it, or null when it isn't there.</summary>
    public ScreenRect? Locate(Target target)
    {
        try
        {
            return driver.TryFind(target) is FlaUiControl found && Rect(found.Element.BoundingRectangle) is { IsEmpty: false } box ? box : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException or StuckException or ScriptException)
        {
            return null;
        }
    }

    /// <summary>
    /// Passes on a click the builder held back to read a message box or file picker before it closed, or a key: the
    /// person's own input, a moment late.
    /// </summary>
    public static void Pass(int x, int y) => Mouse.Click(new Point(x, y));

    /// <inheritdoc cref="Pass(int, int)"/>
    public void Pass(string keys) => driver.Key(keys);

    /// <summary>
    /// Every control in the app's windows, as UI Automation's control view has them, each with the window it is in as
    /// its one ancestor: for the ids report.
    /// </summary>
    public IReadOnlyList<ControlFacts> Survey()
    {
        var found = new List<ControlFacts>();
        foreach (var hwnd in driver.AppWindows().Where(Native.IsVisible))
        {
            try
            {
                var window = new ControlFacts("Window", Native.Title(hwnd));
                found.AddRange(driver.Automation.FromHandle(hwnd).FindAllDescendants().Select(element => Own(element) with { Ancestors = [window] }));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
            {
                // The window closed while it was read.
            }
        }
        return found;
    }

    /// <summary>Everything UI Automation says about a control, for the builder's tip when nothing names it well.</summary>
    public static string Describe(InspectedControl control)
    {
        var facts = control.Facts;
        var lines = new List<string> { $"Type: {facts.Type}" };
        if (facts.Name.Length > 0)
            lines.Add($"Name: {facts.Name}");
        if (!string.IsNullOrEmpty(facts.Id))
            lines.Add($"AutomationId: {facts.Id}");
        if (!string.IsNullOrEmpty(facts.HelpText))
            lines.Add($"HelpText: {facts.HelpText}");
        if (!string.IsNullOrEmpty(facts.ClassName))
            lines.Add($"ClassName: {facts.ClassName}");
        if (facts.Ancestors.Count > 0)
            lines.Add("Inside: " + string.Join(" › ", facts.Ancestors.Take(4).Select(around => around.Name.Length > 0 ? $"{around.Type} \"{around.Name}\"" : around.Type)));
        return string.Join("\n", lines);
    }

    private InspectedControl Inspect(AutomationElement element)
    {
        var ancestors = new List<ControlFacts>();
        DialogFacts? dialog = null;
        var inMain = false;
        var walker = driver.Automation.TreeWalkerFactory.GetControlViewWalker();
        // Up to the window it is in; the desktop, which has no parent, isn't one of its ancestors.
        for (var parent = walker.GetParent(element); parent is not null && walker.GetParent(parent) is not null && ancestors.Count < MostAncestors; parent = walker.GetParent(parent))
        {
            if (parent.Properties.ControlType.ValueOrDefault == ControlType.Window)
            {
                var hwnd = parent.Properties.NativeWindowHandle.ValueOrDefault;
                if (hwnd == driver.Main)
                {
                    inMain = true;
                    break;
                }
                // Another of the app's windows: a Windows message box or file picker, or one of its own.
                if (Native.ClassName(hwnd) == Native.DialogClass)
                    dialog = Dialog(parent, hwnd);
                else
                    ancestors.Add(Own(parent) with { Type = "Window", Name = Native.Title(hwnd) });
                break;
            }
            ancestors.Add(Own(parent));
        }
        // A drop-down's list is a popup window of its own, outside the main window: the drop-down open in the main
        // window is what its items are in.
        if (!inMain && dialog is null && !ancestors.Any(around => around.Type == nameof(ControlType.ComboBox)) && OpenDropDown() is { } owner)
        {
            ancestors.RemoveAll(around => around.Type == nameof(ControlType.Window));
            var box = Inspect(owner).Facts;
            ancestors.Add(box with { Ancestors = [] });
            ancestors.AddRange(box.Ancestors);
        }
        var facts = Own(element) with
        {
            Ancestors = ancestors,
            Value = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value.ValueOrDefault : null,
            IsPassword = element.Properties.IsPassword.ValueOrDefault,
            Dialog = dialog,
        };
        return new InspectedControl(element, facts);
    }

    private AutomationElement? OpenDropDown() =>
        driver.Automation.FromHandle(driver.Main)
            .FindAllDescendants(driver.Automation.ConditionFactory.ByControlType(ControlType.ComboBox))
            .FirstOrDefault(box => box.Patterns.ExpandCollapse.PatternOrDefault?.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Expanded);

    private static ControlFacts Own(AutomationElement element)
    {
        var properties = element.Properties;
        return new ControlFacts(
            properties.ControlType.ValueOrDefault.ToString(),
            properties.Name.ValueOrDefault ?? "",
            Empty(properties.AutomationId.ValueOrDefault),
            Empty(properties.HelpText.ValueOrDefault),
            Empty(properties.ClassName.ValueOrDefault))
        {
            Bounds = Rect(element.BoundingRectangle),
            Checked = element.Patterns.Toggle.IsSupported ? element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault == ToggleState.On : null,
        };
    }

    // A message box's title and message, or a file picker's: whether it picks a folder, and the path it would open,
    // from its name box and, when that holds only a name, the folder its address bar shows.
    private DialogFacts Dialog(AutomationElement window, nint hwnd)
    {
        var title = Native.Title(hwnd);
        var cf = driver.Automation.ConditionFactory;
        var file = window.FindFirstDescendant(cf.ByAutomationId("1148"));
        var folder = window.FindFirstDescendant(cf.ByAutomationId("1152"));
        var box = file ?? folder;
        if (box is null)
            return new DialogFacts(title, driver.MessageText(hwnd));
        var path = box.Patterns.Value.IsSupported ? box.Patterns.Value.Pattern.Value.ValueOrDefault ?? "" : "";
        if (path.Length > 0 && !Path.IsPathRooted(path))
        {
            var address = window.FindAllDescendants(cf.ByControlType(ControlType.ToolBar))
                .Select(bar => bar.Properties.Name.ValueOrDefault ?? "")
                .FirstOrDefault(name => name.StartsWith("Address: ", StringComparison.Ordinal));
            if (address is not null)
                path = Path.Combine(address["Address: ".Length..], path);
        }
        return new DialogFacts(title, IsPicker: true, PicksFolder: file is null, Path: path.Length > 0 ? path : null);
    }

    private static string? Empty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private static ScreenRect Rect(Rectangle box) => new(box.Left, box.Top, box.Right, box.Bottom);
}
