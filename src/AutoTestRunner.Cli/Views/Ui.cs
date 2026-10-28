using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutoTestRunner.Running;

namespace AutoTestRunner.Cli.Views;

/// <summary>autotest's colours, and the small pieces its windows are built from.</summary>
internal static class Ui
{
    public static readonly Brush Ink = Brush("#1A2633"), Muted = Brush("#5B6B7C"), Faint = Brush("#8A97A5"), Line = Brush("#E1E6EC");
    public static readonly Brush Surface = Brush("#F5F7FA"), Accent = Brush("#0F6CBD"), AccentSoft = Brush("#E3EEF9"), AccentEdge = Brush("#C3DAF2");
    public static readonly Brush Pass = Brush("#107C10"), PassSoft = Brush("#E6F4E6"), Fail = Brush("#C42B1C"), FailSoft = Brush("#FBE9E7");
    public static readonly Brush Hold = Brush("#9A5B00"), HoldSoft = Brush("#FFF4E0"), Step = Brush("#6A3D9A"), StepSoft = Brush("#F1EAF8");
    // A pick tool's colour, as the outline round the control it is used on: the outline, its ink, and its band.
    public static readonly Brush Amber = Brush("#FF8C00"), AmberInk = Brush("#B35C00"), AmberSoft = Brush("#FFF4E5");
    public static readonly Brush Pending = Brush("#D5DCE4"), Skip = Brush("#A0AAB4");
    // Databases and queues: what the app keeps and sends, as against its controls.
    public static readonly Brush Data = Brush("#0B6A6A"), DataSoft = Brush("#E3F3F2"), DataEdge = Brush("#B5DCD9");
    public static readonly Brush Terminal = Brush("#1E1E1E"), TerminalText = Brush("#D4D4D4"), TerminalError = Brush("#F48771");
    public static readonly Brush TerminalCommand = Brush("#9CDCFE"), TerminalScript = Brush("#8A8A8A");
    public static readonly FontFamily Text = new("Segoe UI"), Mono = new("Cascadia Mono, Consolas, Courier New");

    public static ResourceDictionary Theme { get; } = new() { Source = new Uri("pack://application:,,,/autotest;component/Views/Theme.xaml") };

    public static Brush Of(CaseStatus status) => status switch
    {
        CaseStatus.Passed => Pass,
        CaseStatus.Failed => Fail,
        CaseStatus.Skipped => Skip,
        _ => Hold,
    };

    public static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    public static TextBlock Glyph(string glyph, double size = 16, Brush? color = null)
    {
        var text = new TextBlock { FontFamily = Icons.Font, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (Icons.Drawn(glyph, size) is { } drawn)
            text.Inlines.Add(new InlineUIContainer(drawn) { BaselineAlignment = BaselineAlignment.Center });
        else
            text.Text = glyph;
        if (color is not null)
            text.Foreground = color;
        return text;
    }

    /// <summary>A glyph inside a line of text, such as a check's subject: a run of the icon font, or a drawn icon.</summary>
    public static Inline GlyphInline(string glyph, double size, Brush color) =>
        Icons.Drawn(glyph, size, color) is { } drawn
            ? new InlineUIContainer(drawn) { BaselineAlignment = BaselineAlignment.Center }
            : new Run(glyph) { FontFamily = Icons.Font, Foreground = color, FontSize = size, BaselineAlignment = BaselineAlignment.Center };

    /// <summary>A glyph that turns, for something that is going on.</summary>
    public static TextBlock Spinner(double size = 12, Brush? color = null)
    {
        var glyph = Glyph(Icons.Spinner, size, color);
        // It turns about its own middle, so it is only as wide as the glyph: left to stretch, as across the rest of a
        // row, its middle is far to the right of the glyph and it swings round that instead.
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        var turn = new RotateTransform();
        glyph.RenderTransformOrigin = new Point(0.5, 0.5);
        glyph.RenderTransform = turn;
        // It turns only while it shows. WPF keeps an animation going, sixty frames a second, on a control that is hidden
        // or collapsed, so a spinner kept in a panel for when it is busy would keep the panel working all the time.
        glyph.IsVisibleChanged += (_, e) => turn.BeginAnimation(RotateTransform.AngleProperty,
            (bool)e.NewValue ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        return glyph;
    }

    public static TextBlock Words(string text, double size = 13, Brush? color = null, FontWeight? weight = null, bool wrap = true) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Ink,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A small label in capitals, over a part of a window.</summary>
    public static TextBlock Heading(string text, double top = 0) =>
        new() { Text = text.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(0, top, 0, 4) };

    /// <summary>A button that is just an icon, with what it does and its key in the tooltip.</summary>
    public static Button IconButton(string glyph, string tip, Action click, double size = 18, Thickness? padding = null)
    {
        var button = new Button { Content = Glyph(glyph, size), ToolTip = tip, Style = (Style)Theme["IconButton"] };
        if (padding is { } around)
            button.Padding = around;
        // An icon says nothing to a screen reader, or to a test that drives the window: the tip names it.
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    public static ToggleButton Toggle(string glyph, string tip, double size = 18)
    {
        var toggle = new ToggleButton { Content = Glyph(glyph, size), ToolTip = tip, Style = (Style)Theme["ToggleIcon"] };
        AutomationProperties.SetName(toggle, tip);
        return toggle;
    }

    /// <summary>A small icon and word that stays on or off, such as a filter on a list.</summary>
    public static ToggleButton Chip(string glyph, string text, string tip)
    {
        var chip = new ToggleButton { Content = IconText(glyph, text, Muted, 11.5, FontWeights.Normal), ToolTip = tip, Style = (Style)Theme["ToggleIcon"] };
        chip.Padding = new Thickness(8, 3, 9, 3);
        chip.Margin = new Thickness(0, 0, 6, 4);
        chip.Background = Surface;
        AutomationProperties.SetName(chip, text);
        // The words take the chip's colour, so that on they show in the accent colour as the glyph does.
        foreach (var part in ((StackPanel)chip.Content).Children.OfType<TextBlock>())
            part.ClearValue(TextBlock.ForegroundProperty);
        return chip;
    }

    /// <summary>A button with an icon and a word or two, for answers and things to open.</summary>
    public static Button LabelledButton(string glyph, string text, Action click, string style = "PlainButton", Brush? glyphColor = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Glyph(glyph, 14, glyphColor));
        content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Style = (Style)Theme[style] };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A dialog's two answers at its foot, side by side and the same size: back, which Esc presses, and go, which Enter does.</summary>
    public static UniformGrid Answers(Button back, Button go)
    {
        (back.IsCancel, go.IsDefault) = (true, true);
        back.Margin = new Thickness(0, 0, 4, 0);
        go.Margin = new Thickness(4, 0, 0, 0);
        var answers = new UniformGrid { Columns = 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var button in new[] { back, go })
        {
            button.MinWidth = 110;
            button.Padding = new Thickness(14, 8, 14, 8);
            answers.Children.Add(button);
        }
        return answers;
    }

    /// <summary>A rounded patch of colour around something, such as a state or a count.</summary>
    public static Border Pill(UIElement child, Brush background, double radius = 10, Thickness? padding = null) =>
        new() { Child = child, Background = background, CornerRadius = new CornerRadius(radius), Padding = padding ?? new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A glyph and a few words side by side, in one colour.</summary>
    public static StackPanel IconText(string glyph, string text, Brush color, double size = 12, FontWeight? weight = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph(glyph, size, color));
        row.Children.Add(new TextBlock { Text = text, Foreground = color, FontSize = size, FontWeight = weight ?? FontWeights.SemiBold, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>
    /// A dialog as tall as what it holds, in the panels' look: centred on its owner, or on the screen when it has none or
    /// onScreen says so, as for a dialog over the side panel.
    /// </summary>
    public static Window Dialog(string title, Window? owner, double width, bool topmost = false, bool onScreen = false)
    {
        var window = new Window
        {
            Title = title, Owner = owner, Topmost = topmost, SizeToContent = SizeToContent.Height, Width = width, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null || onScreen ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Background = Brushes.White, FontFamily = Text, Foreground = Ink,
        };
        window.Resources.MergedDictionaries.Add(Theme);
        return window;
    }

    /// <summary>Asks for a few words in a small window of its own, and returns them, or null when it was closed.</summary>
    public static string? Prompt(Window owner, string title, string message, string initial = "", string button = "Save", string glyph = Icons.Note, bool tall = true)
    {
        string? text = null;
        var window = Dialog(title, owner, 480, topmost: true, onScreen: true);
        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        var box = new TextBox { Margin = new Thickness(0, 10, 0, 0), Height = tall ? 60 : double.NaN, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Padding = new Thickness(4), Text = initial };
        var save = LabelledButton(glyph, button, () =>
        {
            text = box.Text;
            window.Close();
        }, "MainButton");
        save.IsDefault = true;
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(Words(message, 13, Ink));
        stack.Children.Add(box);
        stack.Children.Add(save);
        window.Content = stack;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        window.ShowDialog();
        return text;
    }

    /// <summary>
    /// A menu under a control of the kept values and suite variables, each as ${name} with what it holds now. The one
    /// clicked is given to put, written as ${name}.
    /// </summary>
    public static void ValuesMenu(FrameworkElement under, IReadOnlyList<(string Name, string Text)> values, Action<string> put)
    {
        var menu = new ContextMenu { PlacementTarget = under, Placement = PlacementMode.Bottom };
        foreach (var (name, now) in values)
        {
            var written = "${" + name + "}";
            var header = new DockPanel { MinWidth = 300 };
            var shown = Words(now.Length > 46 ? now[..46] + "…" : now, 11.5, Faint, wrap: false);
            shown.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(shown, Dock.Right);
            header.Children.Add(shown);
            header.Children.Add(new TextBlock { Text = written, FontFamily = Mono, FontSize = 12 });
            var item = new MenuItem { Header = header, ToolTip = now };
            item.Click += (_, _) => put(written);
            menu.Items.Add(item);
        }
        if (values.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Nothing kept yet", IsEnabled = false });
        menu.IsOpen = true;
    }

    /// <summary>Puts text in a box where its caret is, and leaves the caret after it.</summary>
    public static void InsertAtCaret(TextBox box, string text)
    {
        var at = box.CaretIndex;
        box.Text = box.Text.Insert(at, text);
        box.CaretIndex = at + text.Length;
        box.Focus();
    }

    /// <summary>How long something took, as people say it: 0.4 s, 12 s, 2 min 5 s.</summary>
    public static string Took(TimeSpan took) =>
        took.TotalSeconds < 10 ? $"{took.TotalSeconds:0.0} s" : took.TotalMinutes < 1 ? $"{took.TotalSeconds:0} s" : $"{(int)took.TotalMinutes} min {took.Seconds} s";
}
