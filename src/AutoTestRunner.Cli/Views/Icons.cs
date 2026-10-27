using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
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
    public const string SpeedSlow = "", SpeedModest = "", SpeedFast = "", SpeedManual = "";
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
    public const string Send = "", Receive = "", Mail = "", Package = "", Download = "";
    public const string Power = "", Reset = "", Forward = "";

    // Icons the font doesn't have, drawn instead: a database as a cylinder. Each is named by a word that no glyph is.
    public const string Database = "database";

    private static readonly Dictionary<string, Geometry> Drawings = new(StringComparer.Ordinal)
    {
        [Database] = Geometry.Parse("M2,3.5 A6,2 0 1 0 14,3.5 A6,2 0 1 0 2,3.5 M2,3.5 L2,12.5 A6,2 0 0 0 14,12.5 L14,3.5 M2,8 A6,2 0 0 0 14,8"),
    };

    /// <summary>
    /// A drawn icon at the font's size, in the colour of the text it is in, as a glyph would be; null for a glyph of
    /// the font.
    /// </summary>
    public static FrameworkElement? Drawn(string glyph, double size, Brush? color = null)
    {
        if (!Drawings.TryGetValue(glyph, out var geometry))
            return null;
        var path = new Path
        {
            Data = geometry, Stretch = Stretch.Uniform, Width = size * 0.82, Height = size * 0.9, StrokeThickness = Math.Max(1, size / 13),
            StrokeLineJoin = PenLineJoin.Round, SnapsToDevicePixels = true, Margin = new Thickness(size * 0.09, 0, size * 0.09, 0),
        };
        if (color is not null)
            path.Stroke = color;
        else
        {
            path.SetBinding(Shape.StrokeProperty, new Binding(nameof(System.Windows.Controls.TextBlock.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.TextBlock), 1),
            });
        }
        return path;
    }

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
        ActivityKind.Database => Database,
        ActivityKind.Queue => Mail,
        ActivityKind.Reset => Reset,
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
        SubjectKind.Database => Database,
        SubjectKind.Queue => Mail,
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
