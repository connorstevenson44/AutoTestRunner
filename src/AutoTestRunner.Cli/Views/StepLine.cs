using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AutoTestRunner.Running;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// A step's line as the panel draws it: the verb, then what it acts on, each in its own way. A control is on a badge
/// whose tip says what it is and how it was found; a value is data; a key is a key; a file shows its name, and its
/// path when pointed at. The few words between them are quiet.
/// </summary>
internal static class StepLine
{
    // A badge holds a value up to this wide; pointing at it shows the rest.
    private const double MaxBadge = 280;

    /// <summary>A new text block holding the line.</summary>
    public static TextBlock Of(Activity step, double size, bool quiet = false)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Fill(text, step, size, quiet);
        return text;
    }

    /// <summary>Puts the line in a text block, in place of what it held. Quiet draws the verb muted, as for a wait.</summary>
    public static void Fill(TextBlock text, Activity step, double size, bool quiet = false)
    {
        text.Inlines.Clear();
        text.FontSize = size;
        text.Foreground = Ui.Ink;
        // Narration is a sentence, so it reads as one.
        if (step.Parts is [{ Kind: PartKind.Word } only])
        {
            text.Inlines.Add(new Run(only.Text));
            return;
        }
        foreach (var part in step.Parts)
        {
            if (text.Inlines.Count > 0)
                text.Inlines.Add(new Run(" "));
            text.Inlines.Add(part.Kind switch
            {
                PartKind.Verb => new Run(part.Text) { FontWeight = FontWeights.SemiBold, Foreground = quiet ? Ui.Muted : Ui.Ink },
                PartKind.Word => new Run(part.Text) { Foreground = Ui.Muted },
                _ => new InlineUIContainer(Badge(part, size - 1.5)) { BaselineAlignment = BaselineAlignment.Center },
            });
        }
    }

    /// <summary>A part that isn't words: a control, window or service on a badge, a value or file as data, a key as a key.</summary>
    public static Border Badge(Part part, double size)
    {
        var (glyph, color, background, edge) = part.Kind switch
        {
            PartKind.Control => (null, Ui.Accent, Ui.AccentSoft, Ui.AccentEdge),
            PartKind.Window => (Icons.App, Ui.Accent, Ui.AccentSoft, Ui.AccentEdge),
            PartKind.Service => (Icons.Service, Ui.Accent, Ui.AccentSoft, Ui.AccentEdge),
            PartKind.Database => (Icons.Database, Ui.Data, Ui.DataSoft, Ui.DataEdge),
            PartKind.Queue => (Icons.Mail, Ui.Data, Ui.DataSoft, Ui.DataEdge),
            PartKind.File => (Icons.Document, Ui.Ink, Ui.Surface, Ui.Line),
            PartKind.Key => (null, Ui.Ink, Brushes.White, Ui.Pending),
            _ => ((string?)null, Ui.Ink, Ui.Surface, Ui.Line),
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var icon = Ui.Glyph(glyph, size - 1, color);
            icon.Margin = new Thickness(0, 0, 4, 0);
            content.Children.Add(icon);
        }
        var data = part.Kind is PartKind.Value or PartKind.Key;
        content.Children.Add(new TextBlock
        {
            Text = part.Text, FontSize = data ? size - 0.5 : size, Foreground = color, FontFamily = data ? Ui.Mono : Ui.Text,
            FontWeight = part.Kind is PartKind.Control or PartKind.Window or PartKind.Service or PartKind.Database or PartKind.Queue ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        var badge = new Border
        {
            Child = content, Background = background, BorderBrush = edge, CornerRadius = new CornerRadius(4), MaxWidth = MaxBadge,
            BorderThickness = part.Kind == PartKind.Key ? new Thickness(1, 1, 1, 2) : new Thickness(1), Padding = new Thickness(5, 0, 5, 1),
            Margin = new Thickness(0, 1, 0, 1),
        };
        var tip = part.Tip ?? (part.Text.Length > 30 ? part.Text : null);
        if (tip is not null)
        {
            badge.ToolTip = new ToolTip { Content = new TextBlock { Text = tip, FontFamily = part.Kind == PartKind.Control ? Ui.Mono : Ui.Text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap }, MaxWidth = 420 };
            ToolTipService.SetInitialShowDelay(badge, 200);
            ToolTipService.SetShowDuration(badge, 60_000);
            badge.Cursor = System.Windows.Input.Cursors.Help;
        }
        return badge;
    }
}
