using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Asks what a new case is called and where it starts: from a fresh start of the app, or where another case leaves
/// off, which the builder gets to by playing that case and the ones it needs first.
/// </summary>
internal static class NewCaseWindow
{
    public static BuildRequest? Ask(Window? owner, Suite suite)
    {
        BuildRequest? request = null;
        var window = new Window
        {
            Title = "New case", Owner = owner, SizeToContent = SizeToContent.Height, Width = 520, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Background = Brushes.White, FontFamily = Ui.Text, Foreground = Ui.Ink,
        };
        window.Resources.MergedDictionaries.Add(Ui.Theme);
        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        stack.Children.Add(Ui.Heading("What it does"));
        var name = new TextBox { FontSize = 14, Padding = new Thickness(6, 5, 6, 5), Text = "" };
        name.ToolTip = "Named for what it tries, such as Take an order with gift wrap";
        stack.Children.Add(name);

        stack.Children.Add(Ui.Heading("Starts from", top: 16));
        var fresh = new RadioButton { Content = $"A fresh start of {suite.App.Window}", IsChecked = true, Margin = new Thickness(0, 2, 0, 6), FontSize = 13 };
        var after = new RadioButton { Content = "Where a case leaves off", Margin = new Thickness(0, 2, 0, 4), FontSize = 13, IsEnabled = suite.Cases.Count > 0 };
        var cases = new ComboBox { Margin = new Thickness(22, 2, 0, 0), IsEnabled = false, FontSize = 13 };
        foreach (var item in suite.Cases)
            cases.Items.Add(new ComboBoxItem { Content = $"{item.Number}. {item.Name}", Tag = item });
        cases.SelectedIndex = suite.Cases.Count - 1;
        after.Checked += (_, _) => cases.IsEnabled = true;
        fresh.Checked += (_, _) => cases.IsEnabled = false;
        stack.Children.Add(fresh);
        stack.Children.Add(after);
        stack.Children.Add(cases);
        var note = Ui.Words("The builder plays that case, and the ones it carries on from, to get the app there first.", 11.5, Ui.Muted);
        note.Margin = new Thickness(22, 6, 0, 0);
        stack.Children.Add(note);

        var build = Ui.LabelledButton(Icons.Record, "Start building", () =>
        {
            var start = after.IsChecked == true && cases.SelectedItem is ComboBoxItem { Tag: Case item } ? item : null;
            request = new BuildRequest(suite, name.Text.Trim().Length > 0 ? name.Text.Trim() : "New case", After: start);
            window.Close();
        }, "MainButton", glyphColor: Ui.Brush("#FF8A80"));
        build.IsDefault = true;
        build.HorizontalAlignment = HorizontalAlignment.Right;
        build.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(build);
        window.Content = stack;
        window.Loaded += (_, _) => name.Focus();
        window.ShowDialog();
        return request;
    }
}
