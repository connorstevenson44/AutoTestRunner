using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTestRunner.Building;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Asks before a case is changed from a step: which steps after it are taken out, so new ones can be recorded from
/// there, and which cases carry on from it, each ticked to show a warning in the launcher that it needs looking at.
/// </summary>
internal static class EditFromWindow
{
    private const int StepsShown = 6;

    /// <summary>The cases ticked to be marked, or null when the change was called off.</summary>
    public static IReadOnlyList<Case>? Ask(Window owner, int number, IReadOnlyList<DraftStep> later, IReadOnlyList<Case> carryingOn)
    {
        IReadOnlyList<Case>? ticked = null;
        var window = new Window
        {
            Title = $"Edit from step {number}", Owner = owner, Topmost = true, SizeToContent = SizeToContent.Height, Width = 500, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.White, FontFamily = Ui.Text, Foreground = Ui.Ink,
        };
        window.Resources.MergedDictionaries.Add(Ui.Theme);
        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        var taken = later.Count == 1 ? $"The step after step {number} is taken out" : $"The {later.Count} steps after step {number} are taken out";
        stack.Children.Add(Ui.IconText(Icons.Warning, taken, Ui.Hold, 14));
        var intro = Ui.Words("so you can record new ones from there. Saving writes the case without them.", 12.5, Ui.Muted);
        intro.Margin = new Thickness(0, 4, 0, 8);
        stack.Children.Add(intro);
        foreach (var (step, index) in later.Take(StepsShown).Select((step, index) => (step, index)))
        {
            var line = Ui.Words($"{number + index + 1}.  {step.Text}", 12.5, Ui.Ink, wrap: false);
            line.TextDecorations = TextDecorations.Strikethrough;
            line.Margin = new Thickness(8, 1, 0, 1);
            stack.Children.Add(line);
        }
        if (later.Count > StepsShown)
        {
            var more = Ui.Words($"and {Steps(later.Count - StepsShown)} more", 12, Ui.Muted);
            more.Margin = new Thickness(8, 1, 0, 1);
            stack.Children.Add(more);
        }

        var boxes = new List<(Case Case, CheckBox Box)>();
        if (carryingOn.Count > 0)
        {
            stack.Children.Add(Ui.Heading("Cases that carry on from this one", top: 16));
            var why = Ui.Words("They start where this case leaves off, so they may need changing too. Those ticked show a warning in the launcher until they are.", 12, Ui.Muted);
            why.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(why);
            foreach (var item in carryingOn)
            {
                var box = new CheckBox { Content = $"{item.Number}. {item.Name}", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 13 };
                boxes.Add((item, box));
                stack.Children.Add(box);
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = Ui.LabelledButton(Icons.Cross, "Keep them", window.Close);
        cancel.IsCancel = true;
        var edit = Ui.LabelledButton(Icons.Edit, "Take them out and record", () =>
        {
            ticked = [.. boxes.Where(pair => pair.Box.IsChecked == true).Select(pair => pair.Case)];
            window.Close();
        }, "MainButton");
        edit.IsDefault = true;
        edit.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(edit);
        stack.Children.Add(buttons);
        window.Content = stack;
        window.ShowDialog();
        return ticked;
    }

    private static string Steps(int count) => count == 1 ? "step" : $"{count} steps";
}
