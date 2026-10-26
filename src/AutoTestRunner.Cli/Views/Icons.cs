using System.Windows.Media;
using AutoTestRunner.Running;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Glyphs from Windows' own icon font: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets, which has the same ones at
/// the same places, on Windows 10.
/// </summary>
internal static class Icons
{
    public static readonly FontFamily Font = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string Play = "", Pause = "", Stop = "", Next = "";
    public const string SpeedSlow = "", SpeedModest = "", SpeedFast = "";
    public const string CheckList = "", Note = "", Folder = "", Document = "", Console = "";
    public const string App = "", Pointer = "", Keyboard = "", Choose = "", Look = "";
    public const string Key = "", Message = "", Info = "", Wait = "";
    public const string Passed = "", Failed = "", Skipped = "", Stopped = "", Running = "";
    public const string Tick = "", Cross = "", Warning = "", Retry = "", Clean = "";
    public const string Copy = "", Expand = "", Collapse = "", Down = "", Back = "", Spinner = "";
    public const string Record = "", Pick = "", Edit = "", Add = "", Delete = "", Save = "", Up = "";
    public const string Admin = "", Suites = "", Name = "", Tag = "";

    public static string Of(ActivityKind kind) => kind switch
    {
        ActivityKind.App => App,
        ActivityKind.Click => Pointer,
        ActivityKind.Type => Keyboard,
        ActivityKind.Choose => Choose,
        ActivityKind.Point => Look,
        ActivityKind.Key => Key,
        ActivityKind.Dialog => Message,
        ActivityKind.File => Folder,
        ActivityKind.Command => Console,
        ActivityKind.Check => CheckList,
        _ => Info,
    };

    public static string Of(CaseStatus status) => status switch
    {
        CaseStatus.Passed => Passed,
        CaseStatus.Failed => Failed,
        CaseStatus.Skipped => Skipped,
        _ => Stopped,
    };
}
