using System.Drawing;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;

namespace AutoTestRunner.FlaUI;

/// <summary>
/// What the builder asks of the app while a person uses it: which control is under the mouse, what it says about
/// itself and the controls around it, what a box holds and whether a tick box is ticked. Only the app's own controls
/// count, not the builder's or another program's.
/// <para>
/// Each question UI Automation answers is a call into the app, answered on the app's UI thread, so asking many makes
/// the app stutter while the mouse is over it. A control and the controls around it are read with a cache request, as
/// Microsoft advises: each comes with everything the builder wants of it in one call, rather than one call for each
/// thing. https://learn.microsoft.com/windows/win32/winauto/uiauto-cachingforclients
/// </para>
/// </summary>
public sealed class FlaUiInspector(FlaUiDriver driver)
{
    // How far up the tree a control's ancestors are read: far enough for a row in a list in a tab.
    private const int MostAncestors = 12;

    // How far up a control is followed to find the window it is in, past the ancestors kept: a cell in a list in a tab
    // in a styled window can be thirty levels down. Knowing it is in the main window spares a search of the window for
    // an open drop-down.
    private const int MostLevels = 80;

    // How long a control's process not being the app's is trusted before the app's processes are read again, in
    // milliseconds: reading them asks Windows for every process there is.
    private const int ProcessesFor = 1000;

    private readonly CacheRequest _facts = FactsRequest(driver);
    private IReadOnlySet<int> _processes = new HashSet<int>();
    private long _processesRead;

    /// <summary>The ids of the processes running the app's exe, whose windows are the app's.</summary>
    public IReadOnlySet<int> Processes()
    {
        _processes = driver.ProcessIds();
        _processesRead = Environment.TickCount64;
        return _processes;
    }

    /// <summary>The app's control at a point on the screen, in pixels, or null when the point isn't on the app.</summary>
    public InspectedControl? At(int x, int y)
    {
        try
        {
            using var cached = _facts.Activate();
            var element = driver.Automation.FromPoint(new Point(x, y));
            return element is null || !IsApp(element) ? null : Inspect(element);
        }
        catch (Exception ex) when (FlaUiDriver.Gone(ex))
        {
            return null;
        }
    }

    /// <summary>The app's control that has the keyboard, or null when another program has it.</summary>
    public InspectedControl? Focused()
    {
        try
        {
            using var cached = _facts.Activate();
            var element = driver.Automation.FocusedElement();
            return element is null || !IsApp(element) ? null : Inspect(element);
        }
        catch (Exception ex) when (FlaUiDriver.Gone(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// The app's control that has the keyboard, when it is a box that can be typed in. When it is the box given, that
    /// is returned as it is: only a box newly typed in is read, with the controls around it.
    /// </summary>
    public InspectedControl? TypingIn(InspectedControl? current = null)
    {
        try
        {
            using var cached = _facts.Activate();
            var element = driver.Automation.FocusedElement();
            if (element is null || element.Properties.ControlType.ValueOrDefault is not (ControlType.Edit or ControlType.Document) || !IsApp(element))
                return null;
            if (element.Patterns.Value.IsSupported && element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault)
                return null;
            return current is not null && current.Element.Equals(element) ? current : Inspect(element);
        }
        catch (Exception ex) when (FlaUiDriver.Gone(ex))
        {
            return null;
        }
    }

    /// <summary>The control as it is now: its text, value and tick, read again.</summary>
    public InspectedControl Again(InspectedControl control)
    {
        try
        {
            using var cached = _facts.Activate();
            return Inspect(control.Element, again: true);
        }
        catch (Exception ex) when (FlaUiDriver.Gone(ex))
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
        catch (Exception ex) when (FlaUiDriver.Gone(ex))
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
        catch (Exception ex) when (FlaUiDriver.Gone(ex) || ex is StuckException or ScriptException)
        {
            return null;
        }
    }

    /// <summary>
    /// Passes on a click the builder held back to read a message box or file picker before it closed, or a key: the
    /// person's own input, a moment late. The mouse stays where the person has it now.
    /// </summary>
    public static void Pass(int x, int y) => Native.ClickAt(x, y);

    /// <inheritdoc cref="Pass(int, int)"/>
    public void Pass(string keys) => driver.Key(keys);

    /// <summary>
    /// Every control in the app's windows, as UI Automation's control view has them, each with the window it is in as
    /// its one ancestor: for the ids report.
    /// </summary>
    public IReadOnlyList<ControlFacts> Survey()
    {
        var found = new List<ControlFacts>();
        // Every control comes with its facts in the one call that finds them all.
        using var cached = _facts.Activate();
        foreach (var hwnd in driver.AppWindows().Where(Native.IsVisible))
        {
            try
            {
                var window = new ControlFacts("Window", Native.Title(hwnd));
                found.AddRange(driver.Automation.FromHandle(hwnd).FindAllDescendants().Select(element => Own(element) with { Ancestors = [window] }));
            }
            catch (Exception ex) when (FlaUiDriver.Gone(ex))
            {
                // The window closed while it was read.
            }
        }
        return found;
    }

    // Whether a control is the app's. The app's processes are read again when it seems not to be, as the app may have
    // started again, but not more than once a second.
    private bool IsApp(AutomationElement element)
    {
        var process = element.Properties.ProcessId.ValueOrDefault;
        if (_processes.Contains(process))
            return true;
        if (Environment.TickCount64 - _processesRead < ProcessesFor)
            return false;
        return Processes().Contains(process);
    }

    // Read with the cache request active: the control and each control above it come with their facts. A control read
    // again has its own facts read afresh, as its cached ones are from when it was first read.
    private InspectedControl Inspect(AutomationElement element, bool again = false)
    {
        var ancestors = new List<ControlFacts>();
        DialogFacts? dialog = null;
        var inMain = false;
        var walker = driver.Automation.TreeWalkerFactory.GetControlViewWalker();
        // Up to the window it is in; the desktop isn't one of its ancestors. Past the ancestors kept, it is only
        // followed up to see which window it is in.
        var levels = 0;
        for (var parent = walker.GetParent(element); parent is not null && levels < MostLevels; parent = walker.GetParent(parent), levels++)
        {
            var hwnd = parent.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd != 0 && hwnd == Native.Desktop)
                break;
            if (parent.Properties.ControlType.ValueOrDefault == ControlType.Window)
            {
                if (hwnd == driver.Main)
                {
                    inMain = true;
                    break;
                }
                // Another of the app's windows: a Windows message box or file picker, or one of its own.
                if (Native.ClassName(hwnd) == Native.DialogClass)
                    dialog = Dialog(hwnd);
                else if (ancestors.Count < MostAncestors)
                    ancestors.Add(Own(parent) with { Type = "Window", Name = Native.Title(hwnd) });
                break;
            }
            if (ancestors.Count < MostAncestors)
                ancestors.Add(Own(parent));
        }
        // A drop-down's list is a popup window of its own, outside the main window: the drop-down open in the main
        // window is what its items are in. A control too deep to follow up to its window isn't in one.
        if (!inMain && dialog is null && levels < MostLevels && !ancestors.Any(around => around.Type == nameof(ControlType.ComboBox)) && OpenDropDown() is { } owner)
        {
            ancestors.RemoveAll(around => around.Type == nameof(ControlType.Window));
            var box = Inspect(owner).Facts;
            ancestors.Add(box with { Ancestors = [] });
            ancestors.AddRange(box.Ancestors);
        }
        using (again ? CacheRequest.ForceNoCache() : null)
        {
            var facts = Own(element) with
            {
                Ancestors = ancestors,
                Value = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value.ValueOrDefault : null,
                IsPassword = element.Properties.IsPassword.ValueOrDefault,
                Dialog = dialog,
            };
            return new InspectedControl(element, facts);
        }
    }

    // The drop-down open in the main window, if any: UI Automation looks for it in the app, in one call.
    private AutomationElement? OpenDropDown()
    {
        var cf = driver.Automation.ConditionFactory;
        var expanded = new PropertyCondition(driver.Automation.PropertyLibrary.ExpandCollapse.ExpandCollapseState, (int)ExpandCollapseState.Expanded);
        return driver.Automation.FromHandle(driver.Main).FindFirstDescendant(cf.ByControlType(ControlType.ComboBox).And(expanded));
    }

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
    // from its name box and, when that holds only a name, the folder its address bar shows. The name box and the
    // address bar are Win32 windows, found by their control id and class without searching the picker's folders.
    private DialogFacts Dialog(nint hwnd)
    {
        var title = Native.Title(hwnd);
        var file = Native.Control(hwnd, 1148);
        var box = file != 0 ? file : Native.Control(hwnd, 1152);
        if (box == 0)
            return new DialogFacts(title, driver.MessageText(hwnd));
        // The box typed in, inside the file picker's drop-down of names.
        var named = driver.Automation.FromHandle(box);
        if (!named.Patterns.Value.IsSupported)
            named = named.FindFirstDescendant(driver.Automation.ConditionFactory.ByControlType(ControlType.Edit)) ?? named;
        var path = named.Patterns.Value.IsSupported ? named.Patterns.Value.Pattern.Value.ValueOrDefault ?? "" : "";
        if (path.Length > 0 && !Path.IsPathRooted(path))
        {
            var address = Native.Children(hwnd, "ToolbarWindow32")
                .Select(bar => driver.Automation.FromHandle(bar).Properties.Name.ValueOrDefault ?? "")
                .FirstOrDefault(name => name.StartsWith("Address: ", StringComparison.Ordinal));
            if (address is not null)
                path = Path.Combine(address["Address: ".Length..], path);
        }
        return new DialogFacts(title, IsPicker: true, PicksFolder: file == 0, Path: path.Length > 0 ? path : null);
    }

    // What Inspect reads of each control, asked for with the control rather than one by one.
    private static CacheRequest FactsRequest(FlaUiDriver driver)
    {
        var properties = driver.Automation.PropertyLibrary;
        var patterns = driver.Automation.PatternLibrary;
        var request = new CacheRequest { AutomationElementMode = AutomationElementMode.Full, TreeScope = TreeScope.Element };
        foreach (var property in new[]
        {
            properties.Element.ControlType, properties.Element.Name, properties.Element.AutomationId, properties.Element.HelpText, properties.Element.ClassName,
            properties.Element.BoundingRectangle, properties.Element.ProcessId, properties.Element.NativeWindowHandle, properties.Element.IsPassword,
            properties.Value.Value, properties.Value.IsReadOnly, properties.Toggle.ToggleState,
        })
            request.Add(property);
        request.Add(patterns.ValuePattern);
        request.Add(patterns.TogglePattern);
        return request;
    }

    private static string? Empty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private static ScreenRect Rect(Rectangle box) => new(box.Left, box.Top, box.Right, box.Bottom);
}
