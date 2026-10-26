using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>
/// The app under test, as a list of controls. Clicking a button runs what OnClick says, so a test can script the app:
/// Save sets the status bar, for example. A control that isn't there is stuck straight away, without waiting.
/// </summary>
internal sealed class FakeDriver : IDriver
{
    public List<FakeControl> Controls { get; } = [];
    public Dictionary<string, Action> OnClick { get; } = [];
    public List<string> Done { get; } = [];
    public bool Started { get; private set; }

    public FakeControl Add(string type, string name, string? id = null, string? helpText = null)
    {
        var control = new FakeControl(type, name, id, helpText);
        Controls.Add(control);
        return control;
    }

    public void Start(AppSettings app, string exe)
    {
        Started = true;
        Done.Add($"start {exe}");
    }

    public void Close() => Done.Add("close");

    public void CloseAll(string exe) => Done.Add("close all");

    public IControl Find(Target target, TimeSpan timeout) => TryFind(target) ?? throw new StuckException($"find {target.What}");

    public IControl? TryFind(Target target) =>
        target.Selectors.Select(selector => Controls.FirstOrDefault(control => selector.Matches(control.AutomationId, control.Name, control.Type, control.HelpText, null)))
            .FirstOrDefault(control => control is not null);

    public void WaitGone(Target target, TimeSpan timeout)
    {
        if (TryFind(target) is not null)
            throw new StuckException($"see {target.What} go");
    }

    public void Point(IControl control, string what) => Done.Add($"point {control.Name}");

    public void Click(IControl control, ClickHow how)
    {
        Done.Add(how switch { ClickHow.Right => $"right-click {control.Name}", ClickHow.Double => $"double-click {control.Name}", _ => $"click {control.Name}" });
        if (OnClick.TryGetValue(control.Name, out var reaction))
            reaction();
    }

    public void Type(IControl control, string text, bool secret)
    {
        ((FakeControl)control).Value = text;
        Done.Add($"type {(secret ? "****" : text)}");
    }

    public void Choose(IControl control, string option) => ((FakeControl)control).Selection = option;

    public void SetChecked(IControl control, bool isChecked) => Done.Add($"tick {control.Name} {isChecked}");

    public bool ReplaceText(IControl control, string pattern, string replacement)
    {
        var fake = (FakeControl)control;
        var regex = new System.Text.RegularExpressions.Regex(pattern);
        if (!regex.IsMatch(fake.Value ?? ""))
            return false;
        fake.Value = regex.Replace(fake.Value!, replacement, 1);
        return true;
    }

    public void Drag(IControl control, IControl onto) => Done.Add($"drag {control.Name} onto {onto.Name}");

    public void Menu(IReadOnlyList<string> path)
    {
        Done.Add($"menu {string.Join(" > ", path)}");
        if (OnClick.TryGetValue(path[^1], out var reaction))
            reaction();
    }

    public void Expand(IControl control) => Done.Add($"expand {control.Name}");

    public void SetValue(IControl control, string value)
    {
        ((FakeControl)control).Value = value;
        Done.Add($"set {control.Name} {value}");
    }

    public int Rows(IControl control) => ((FakeControl)control).Rows;

    public void UseWindow(string title, TimeSpan timeout) => Done.Add($"use {title}");

    public void Key(string keys) => Done.Add($"key {keys}");

    public string WaitDialog(string title, TimeSpan timeout) => throw new StuckException($"see the \"{title}\" message");

    public void PressDialogButton(string title, DialogButton button) { }

    public string? OtherWindow() => null;

    public void PickFile(string title, string path, bool folder) => Done.Add($"pick {path}");

    public void Screenshot(string path) => File.WriteAllText(path, "png");

    public string DumpTree() => string.Join("\n", Controls.Select(control => $"{control.Type} \"{control.Name}\""));

    public void Dispose() { }
}
