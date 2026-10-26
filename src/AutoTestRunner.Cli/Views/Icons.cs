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
    public const string Values = "", Repeat = "", Branch = "", Macro = "", Repair = "", Blocked = "";
    public const string NewFolder = "", Ids = "", Controls = "", More = "";
    public const string Setting = "", Service = "", Calendar = "", Log = "", Globe = "", Paste = "";
    public const string Computer = "", Process = "", History = "";
    public const string Mouse = "", Menu = "", Clock = "", Slider = "";

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
        ActivityKind.Wait => Wait,
        ActivityKind.Check => CheckList,
        _ => Info,
    };

    public static string Of(SubjectKind kind) => kind switch
    {
        SubjectKind.File => Document,
        SubjectKind.Folder => Folder,
        SubjectKind.Setting or SubjectKind.Registry => Setting,
        SubjectKind.Log => Log,
        SubjectKind.Service => Service,
        SubjectKind.Task => Calendar,
        SubjectKind.Process => Process,
        SubjectKind.Program => Console,
        SubjectKind.Control => Pointer,
        SubjectKind.EventLog => History,
        SubjectKind.Web => Globe,
        SubjectKind.Clipboard => Paste,
        SubjectKind.Machine => Computer,
        _ => Values,
    };

    public static string Of(CaseStatus status) => status switch
    {
        CaseStatus.Passed => Passed,
        CaseStatus.Failed => Failed,
        CaseStatus.Skipped => Skipped,
        _ => Stopped,
    };
}
