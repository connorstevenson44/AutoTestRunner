using System.Windows;
using System.Windows.Controls;
using AutoTestRunner.Building;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Asks before a case is changed from a step: lists the steps after it that are removed, so new ones can be recorded
/// from there, and the cases that carry on from it, each ticked to show a warning in the launcher that it needs checking.
/// </summary>
internal static class EditFromWindow
{
    /// <summary>The cases ticked to be marked, or null when the change was called off.</summary>
    public static IReadOnlyList<Case>? Ask(Window owner, int number, IReadOnlyList<DraftStep> later, IReadOnlyList<Case> carryingOn)
    {
        IReadOnlyList<Case>? ticked = null;
        var window = Ui.Dialog($"Edit from step {number}", owner, 500, topmost: true, onScreen: true);
        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        var removed = later.Count == 1 ? $"Step {number + 1} will be removed" : $"Steps {number + 1} to {number + later.Count} will be removed";
        stack.Children.Add(Ui.IconText(Icons.Delete, removed, Ui.Hold, 14));
        var intro = Ui.Words($"Recording starts again after step {number}. The case file only changes when you save.", 12.5, Ui.Muted);
        intro.Margin = new Thickness(0, 4, 0, 8);
        stack.Children.Add(intro);
        var lines = new StackPanel();
        foreach (var (step, index) in later.Select((step, index) => (step, index)))
        {
            var line = Ui.Words($"{number + index + 1}.  {step.Text}", 12.5, Ui.Ink, wrap: false);
            line.TextDecorations = TextDecorations.Strikethrough;
            line.Margin = new Thickness(8, 1, 0, 1);
            lines.Children.Add(line);
        }
        stack.Children.Add(new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(2, 4, 2, 4),
            Child = new ScrollViewer { Content = lines, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        });

        var boxes = new List<(Case Case, CheckBox Box)>();
        if (carryingOn.Count > 0)
        {
            stack.Children.Add(Ui.Heading("Cases that start where this one ends", top: 16));
            var why = Ui.Words("Changing this case may break them. Ticked cases get a warning in the launcher until you clear it.", 12, Ui.Muted);
            why.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(why);
            foreach (var item in carryingOn)
            {
                var box = new CheckBox { Content = $"{item.Number}. {item.Name}", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 13 };
                boxes.Add((item, box));
                stack.Children.Add(box);
            }
        }

        var cancel = Ui.LabelledButton(Icons.Cross, "Cancel", window.Close);
        var confirm = Ui.LabelledButton(Icons.Tick, "Confirm", () =>
        {
            ticked = [.. boxes.Where(pair => pair.Box.IsChecked == true).Select(pair => pair.Case)];
            window.Close();
        }, "MainButton");
        stack.Children.Add(Ui.Answers(cancel, confirm));
        window.Content = stack;
        window.ShowDialog();
        return ticked;
    }
}
