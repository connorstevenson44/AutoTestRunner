using Walkthrough.Scripting;

namespace Walkthrough.Driving;

/// <summary>A rectangle on the screen, in pixels.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public bool Overlaps(ScreenRect other) =>
        !(other.Right < Left || other.Left > Right || other.Bottom < Top || other.Top > Bottom);
}

/// <summary>A control that was found. What it says is read when asked, so it is up to date.</summary>
public interface IControl
{
    string Name { get; }
    string? Value { get; }
    string? HelpText { get; }
    string? AutomationId { get; }

    /// <summary>The picked item of a drop-down or list, or null.</summary>
    string? Selection { get; }

    bool IsEnabled { get; }
}

/// <summary>Which button of a Windows message box to press.</summary>
public enum DialogButton { Ok = 1, Cancel = 2, Yes = 6, No = 7 }

/// <summary>
/// Drives the app under test. It finds controls, shows the person what is next (the mouse, an outline) and uses
/// them. Every wait calls <see cref="RunControl.Gate"/>, so a pause or a stop takes effect, and scales its timeout by
/// <see cref="RunControl.TimeoutScale"/>. When something can't be found or used it throws <see cref="StuckException"/>.
/// The FlaUI driver is the real one; the tests use a fake.
/// </summary>
public interface IDriver : IDisposable
{
    /// <summary>Closes any copy of the app that is open, starts it, and waits for its main window.</summary>
    void Start(AppSettings app, string exe);

    void Close();

    /// <summary>Closes every running copy of the app's exe, as before a reset.</summary>
    void CloseAll(string exe);

    /// <summary>Waits for a control that is on screen.</summary>
    IControl Find(Target target, TimeSpan timeout);

    /// <summary>The control if it is on screen now, without waiting.</summary>
    IControl? TryFind(Target target);

    void WaitGone(Target target, TimeSpan timeout);

    /// <summary>Scrolls a control into view, moves the mouse to it and outlines it, labelled with what.</summary>
    void Point(IControl control, string what);

    /// <summary>Invokes, toggles or selects a control, or with useMouse clicks it.</summary>
    void Click(IControl control, bool useMouse);

    /// <summary>Types into a text box a key at a time, then moves on with Tab so the value is taken.</summary>
    void Type(IControl control, string text, bool secret);

    void Choose(IControl control, string option);

    void SetChecked(IControl control, bool isChecked);

    /// <summary>Replaces the first match of a regex in a text box's value. False when nothing matched.</summary>
    bool ReplaceText(IControl control, string pattern, string replacement);

    /// <summary>Presses a key or a combination, such as "Tab" or "Ctrl+A".</summary>
    void Key(string keys);

    /// <summary>Waits for a Windows message box by its title, and returns the message it shows.</summary>
    string WaitDialog(string title, TimeSpan timeout);

    /// <summary>Presses one of a message box's buttons, found by its command id whatever the language, and waits for it to close.</summary>
    void PressDialogButton(string title, DialogButton button);

    /// <summary>Another window of the app's that is showing, such as an error, described as its title and text; null when there is none.</summary>
    string? OtherWindow();

    /// <summary>Picks a file or folder in a Windows file picker by typing its path.</summary>
    void PickFile(string title, string path, bool folder);

    /// <summary>Saves a screenshot of the screen.</summary>
    void Screenshot(string path);

    /// <summary>The app's windows as UI Automation sees them, for working out why a control wasn't found.</summary>
    string DumpTree();
}

/// <summary>Something the run needs isn't there, or can't be used: what it was trying to do, and what it found instead.</summary>
public sealed class StuckException(string what, string? detail = null) : Exception(detail is null ? $"I couldn't {what}." : $"I couldn't {what}. {detail}")
{
    public string What { get; } = what;
    public string? Detail { get; } = detail;
}
