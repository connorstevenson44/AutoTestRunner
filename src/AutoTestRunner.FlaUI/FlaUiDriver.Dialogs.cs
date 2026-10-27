using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;
using FlaUI.Core.Definitions;

namespace AutoTestRunner.FlaUI;

public sealed partial class FlaUiDriver
{
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
        // Message box buttons have their command ids as automation ids, whatever the language.
        var found = Find(new Target([new Selector(Id: ((int)button).ToString(CultureInfo.InvariantCulture), Type: "Button", Within: new Target([new Selector(Type: "Window", Name: title)], title))],
            $"the {button} button"), _control.Scale(TimeSpan.FromSeconds(5)));
        Point(found, button.ToString());
        Click(found, ClickHow.Invoke);
        var end = Environment.TickCount64 + 10_000;
        while (Native.IsVisible(hwnd) && Environment.TickCount64 < end)
            Thread.Sleep(100);
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
        catch (COMException)
        {
            // Described by its title alone.
        }
        return $"\"{Native.Title(hwnd)}\": {(text.Length > 400 ? text[..400] : text)}";
    }
}
