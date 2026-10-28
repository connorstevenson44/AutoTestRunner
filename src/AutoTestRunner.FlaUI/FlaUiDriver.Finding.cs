using System.Drawing;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;

namespace AutoTestRunner.FlaUI;

public sealed partial class FlaUiDriver
{
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
                    .Select(_automation.FromHandle));

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

            // A button on the same row as another control is looked for across the window, level with its middle.
            int? middle = null;
            if (selector.SameRowAs is { } rowOf)
            {
                if (TryFindElement(rowOf, scroll: false) is not { } anchor)
                    return default;
                middle = Middle(anchor);
                root = MainWindow();
            }

            // Each control's name, help text and place are read one by one rather than asked for with the find, as a find
            // that brings them with it kept NetDeploy's window from drawing, and its timers from running, while a wait
            // looked for its log editor again and again.
            var candidates = root.FindAllDescendants(Condition(selector)).Where(element => Fits(selector, element) && IsShown(element));
            if (middle is { } level)
                candidates = candidates.Where(element => Math.Abs(Middle(element) - level) <= 14);
            return pick(candidates);
        }
        catch (StuckException)
        {
            throw;
        }
        catch (Exception ex) when (Gone(ex))
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

    private static string Name(AutomationElement element) => OrIfGone(() => element.Properties.Name.ValueOrDefault ?? "", "");

    private static string HelpText(AutomationElement element) => OrIfGone(() => element.Properties.HelpText.ValueOrDefault ?? "", "");

    private static bool IsShown(AutomationElement element) => OrIfGone(() => element.BoundingRectangle is { Width: > 0, Height: > 0 }, false);

    private static int Middle(AutomationElement element)
    {
        var box = element.BoundingRectangle;
        return (box.Top + box.Bottom) / 2;
    }

    // A request for these properties of each control found, which stays usable once the request is no longer active.
    // A find made while it is active searches the request's view rather than the control view, so it asks for the
    // control view too: WPF keeps a collapsed control in the raw view with the size it last had, so it would be found
    // and look shown.
    private CacheRequest Facts(params PropertyId[] properties)
    {
        var request = new CacheRequest
        {
            AutomationElementMode = AutomationElementMode.Full,
            TreeScope = TreeScope.Element,
            TreeFilter = new PropertyCondition(_automation.PropertyLibrary.Element.IsControlElement, true),
        };
        foreach (var property in properties)
            request.Add(property);
        return request;
    }

    // Scrolls each long list in the window, up to its top and then down to its end, until something that matches is
    // built. A list is at its end once a few steps build no new rows.
    private AutomationElement? ScrollTo(Selector selector)
    {
        foreach (var list in MainWindow().FindAllDescendants(Conditions.ByControlType(ControlType.List)))
        {
            var box = OrIfGone(() => list.BoundingRectangle, Rectangle.Empty);
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
}
