using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Asks what the builder adds after a control is clicked: a macro, the values of its parameters, and how long to wait
/// before it, which goes in suite.json's builder.afterClick for every case built after.
/// </summary>
internal static class HookWindow
{
    /// <summary>The hook, or null when it was closed.</summary>
    public static ClickHook? Ask(Window owner, Suite suite, string element)
    {
        ClickHook? hook = null;
        var window = new Window
        {
            Title = $"After clicking {element}", Owner = owner, Topmost = true, SizeToContent = SizeToContent.Height, Width = 480, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.White, FontFamily = Ui.Text, Foreground = Ui.Ink,
        };
        window.Resources.MergedDictionaries.Add(Ui.Theme);
        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        stack.Children.Add(Ui.Words($"Each time {element} is clicked while recording, the builder adds a call of this macro after it, instead of a wait for the status text.", 13, Ui.Ink));
        stack.Children.Add(Ui.Heading("Macro", top: 12));
        var macros = new ComboBox { FontSize = 13 };
        foreach (var name in suite.Macros.Keys.Order(StringComparer.Ordinal))
            macros.Items.Add(name);
        macros.SelectedIndex = macros.Items.Count > 0 ? 0 : -1;
        stack.Children.Add(macros);
        var parameters = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var fields = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        void ShowParameters()
        {
            parameters.Children.Clear();
            fields.Clear();
            if (macros.SelectedItem is not string name)
                return;
            foreach (var (parameter, fallback) in suite.Macros[name].Parameters)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var label = Ui.Words(parameter, 12.5, Ui.Muted, wrap: false);
                label.Width = 110;
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(label);
                var box = new TextBox { FontSize = 12.5, Padding = new Thickness(4, 3, 4, 3), ToolTip = fallback is null ? "Needed" : $"Left empty, it is {fallback}" };
                fields[parameter] = box;
                row.Children.Add(box);
                parameters.Children.Add(row);
            }
        }
        macros.SelectionChanged += (_, _) => ShowParameters();
        ShowParameters();
        stack.Children.Add(parameters);
        stack.Children.Add(Ui.Heading("Wait first, in seconds", top: 12));
        var delay = new TextBox { Text = "0", FontSize = 13, Padding = new Thickness(4, 3, 4, 3), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(delay);
        var add = Ui.LabelledButton(Icons.Add, "Add to suite.json", () =>
        {
            if (macros.SelectedItem is not string name)
                return;
            var with = new JsonObject();
            foreach (var (parameter, box) in fields.Where(pair => pair.Value.Text.Trim().Length > 0))
                with[parameter] = box.Text.Trim();
            var seconds = double.TryParse(delay.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var given) ? Math.Max(0, given) : 0;
            hook = new ClickHook(element, name, with.Count > 0 ? with : null, seconds);
            window.Close();
        }, "MainButton");
        add.IsDefault = true;
        add.IsEnabled = macros.Items.Count > 0;
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(add);
        if (macros.Items.Count == 0)
            stack.Children.Add(Ui.Words("The suite has no macros yet: add one to macros/ first.", 12, Ui.Fail));
        window.Content = stack;
        window.ShowDialog();
        return hook;
    }
}
