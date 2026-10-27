using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTestRunner.Building;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Asks what a macro made of steps is called, what it does, and which of the values in the steps become its
/// parameters, each ticked with a name, so the next case can give it other values.
/// </summary>
internal static class MacroWindow
{
    /// <summary>The macro's name, what it does, and the values made parameters, with the names given; or null when closed.</summary>
    public static (string Name, string Description, IReadOnlyList<MacroValue> Parameters)? Ask(Window owner, IReadOnlyList<MacroValue> values, IReadOnlySet<string> taken)
    {
        (string, string, IReadOnlyList<MacroValue>)? answer = null;
        var window = new Window
        {
            Title = "Make a macro", Owner = owner, Topmost = true, SizeToContent = SizeToContent.Height, Width = 440, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White, FontFamily = Ui.Text, Foreground = Ui.Ink,
        };
        window.Resources.MergedDictionaries.Add(Ui.Theme);
        var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        stack.Children.Add(Ui.IconText(Icons.Macro, "Make a macro", Ui.Ink, 14));
        stack.Children.Add(Ui.Heading("Name, such as addOrder", 12));
        var name = new TextBox { FontSize = 13, Padding = new Thickness(4, 3, 4, 3), FontFamily = Ui.Mono };
        stack.Children.Add(name);
        stack.Children.Add(Ui.Heading("What it does, as the panel says it", 10));
        var description = new TextBox { FontSize = 13, Padding = new Thickness(4, 3, 4, 3) };
        stack.Children.Add(description);

        var rows = new List<(MacroValue Value, CheckBox Tick, TextBox Name)>();
        if (values.Count > 0)
        {
            stack.Children.Add(Ui.Heading("Parameters: values the next use can change", 14));
            foreach (var value in values)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var tick = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                var parameter = new TextBox { Text = value.Name, Width = 130, FontFamily = Ui.Mono, FontSize = 12, Padding = new Thickness(3, 2, 3, 2) };
                DockPanel.SetDock(tick, Dock.Left);
                DockPanel.SetDock(parameter, Dock.Left);
                tick.Checked += (_, _) => parameter.IsEnabled = true;
                tick.Unchecked += (_, _) => parameter.IsEnabled = false;
                row.Children.Add(tick);
                row.Children.Add(parameter);
                var now = Ui.Words($"now \"{value.Text}\"", 12, Ui.Muted, wrap: false);
                now.Margin = new Thickness(8, 0, 0, 0);
                now.ToolTip = $"Step {value.Step + 1}'s {value.Key}";
                row.Children.Add(now);
                stack.Children.Add(row);
                rows.Add((value, tick, parameter));
            }
        }
        var problem = Ui.Words("", 12, Ui.Fail);
        problem.Margin = new Thickness(0, 8, 0, 0);
        problem.Visibility = Visibility.Collapsed;
        stack.Children.Add(problem);

        var make = Ui.LabelledButton(Icons.Macro, "Make it", () =>
        {
            var chosen = rows.Where(row => row.Tick.IsChecked == true).Select(row => row.Value with { Name = row.Name.Text.Trim() }).ToList();
            var why = !ScriptVocabulary.IsName(name.Text.Trim()) ? "A macro's name is letters and digits, such as addOrder."
                : taken.Contains(name.Text.Trim()) ? $"The suite already has a macro called {name.Text.Trim()}."
                : chosen.FirstOrDefault(value => !ScriptVocabulary.IsName(value.Name)) is { } bad ? $"A parameter's name is letters and digits: \"{bad.Name}\" isn't."
                : chosen.GroupBy(value => value.Name).FirstOrDefault(same => same.Count() > 1) is { } twice ? $"Two parameters are called {twice.Key}."
                : null;
            if (why is not null)
            {
                (problem.Text, problem.Visibility) = (why, Visibility.Visible);
                return;
            }
            answer = (name.Text.Trim(), description.Text.Trim().Length > 0 ? description.Text.Trim() : $"Run {name.Text.Trim()}", chosen);
            window.Close();
        }, "MainButton");
        make.IsDefault = true;
        make.HorizontalAlignment = HorizontalAlignment.Left;
        make.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(make);
        window.Content = stack;
        window.Loaded += (_, _) => name.Focus();
        window.ShowDialog();
        return answer;
    }

}
