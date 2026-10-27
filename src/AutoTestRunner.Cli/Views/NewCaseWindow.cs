using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Asks what a new case is called and where it starts: from a fresh start of the app, where another case leaves off,
/// or after another case has run, from its reset; the builder gets there by playing that case, and those it needs.
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
        stack.Children.Add(Ui.Heading("Case name"));
        var name = new TextBox { FontSize = 14, Padding = new Thickness(6, 5, 6, 5), Text = "" };
        name.ToolTip = "What the case tests, such as Take an order with gift wrap";
        stack.Children.Add(name);

        stack.Children.Add(Ui.Heading("Starts from", top: 16));
        // Each way to start has its icon: the app started, another case's end, or another case's reset.
        static RadioButton Choice(string glyph, string text, bool enabled, string tip)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Ui.Glyph(glyph, 14, Ui.Accent);
            icon.Margin = new Thickness(0, 0, 8, 0);
            content.Children.Add(icon);
            content.Children.Add(Ui.Words(text, 13, Ui.Ink, wrap: false));
            return new RadioButton { Content = content, Margin = new Thickness(0, 2, 0, 6), IsEnabled = enabled, ToolTip = tip, VerticalContentAlignment = VerticalAlignment.Center };
        }
        var withReset = suite.Cases.Where(item => item.Reset.Count > 0).ToList();
        var fresh = Choice(Icons.Power, $"A fresh start of {suite.App.Window}", true, "The case starts the app itself.");
        var after = Choice(Icons.Forward, "The end of another case", suite.Cases.Count > 0,
            "The case carries on from where another leaves the app. The builder first plays that case, and any it needs, to get the app there.");
        var reset = Choice(Icons.Reset, "After another case has run, from its reset", withReset.Count > 0,
            withReset.Count > 0
                ? "The case starts once another, such as signing in, has run, and its reset steps have taken the app back. A run doesn't start the app again for it."
                : "No case has reset steps yet. Mark the last steps of a case, such as going back to the home screen after signing in, as its reset.");
        ToolTipService.SetShowOnDisabled(reset, true);
        fresh.IsChecked = true;
        var cases = new ComboBox { Margin = new Thickness(22, 2, 0, 0), IsEnabled = false, FontSize = 13 };
        void Fill(IEnumerable<Case> items)
        {
            cases.Items.Clear();
            foreach (var item in items)
                cases.Items.Add(new ComboBoxItem { Content = $"{item.Number}. {item.Name}", Tag = item });
            cases.SelectedIndex = cases.Items.Count - 1;
        }
        Fill(suite.Cases);
        after.Checked += (_, _) =>
        {
            cases.IsEnabled = true;
            Fill(suite.Cases);
        };
        reset.Checked += (_, _) =>
        {
            cases.IsEnabled = true;
            Fill(withReset);
        };
        fresh.Checked += (_, _) => cases.IsEnabled = false;
        stack.Children.Add(fresh);
        stack.Children.Add(after);
        stack.Children.Add(reset);
        stack.Children.Add(cases);

        var build = Ui.LabelledButton(Icons.Record, "Start building", () =>
        {
            var start = (after.IsChecked == true || reset.IsChecked == true) && cases.SelectedItem is ComboBoxItem { Tag: Case item } ? item : null;
            request = new BuildRequest(suite, name.Text.Trim().Length > 0 ? name.Text.Trim() : "New case", After: start, Reset: start is not null && reset.IsChecked == true);
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
