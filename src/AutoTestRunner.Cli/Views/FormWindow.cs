using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using AutoTestRunner.Building;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// A small window with a step's or check's form: its fields, Try now for a check, which runs it against the app as it
/// is and says whether it passes, and the button that adds it. It sits beside the window that opened it, so the app
/// stays in sight while a control is picked.
/// </summary>
internal sealed class FormWindow : Window
{
    private readonly FormView _form;
    private readonly JsonObject _start;
    private readonly TextBlock _problem;
    private JsonObject? _answer;

    private FormWindow(Window owner, string title, string glyph, FormView form, JsonObject start, Func<JsonObject, Task<CheckResult>>? tryIt, string add)
    {
        (_form, _start) = (form, start);
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = title;
        Owner = owner;
        Topmost = true;
        ShowInTaskbar = false;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        Background = System.Windows.Media.Brushes.White;
        FontFamily = Ui.Text;
        Foreground = Ui.Ink;

        var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        stack.Children.Add(Ui.IconText(glyph, title, Ui.Ink, 14));
        stack.Children.Add(new ScrollViewer { Content = form, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 0) });
        _problem = Ui.Words("", 12, Ui.Fail);
        _problem.Margin = new Thickness(0, 8, 0, 0);
        _problem.Visibility = Visibility.Collapsed;
        stack.Children.Add(_problem);

        if (tryIt is not null)
        {
            var result = new ContentControl { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Button? tryNow = null;
            tryNow = Ui.LabelledButton(Icons.Play, "Try now", async () =>
            {
                if (Read() is not { } check)
                    return;
                tryNow!.IsEnabled = false;
                result.Content = Ui.Spinner(13, Ui.Accent);
                var answer = await tryIt(check);
                tryNow.IsEnabled = true;
                var shown = Ui.IconText(answer.Passed ? Icons.Passed : Icons.Failed, answer.Passed ? "Passes now" : "Doesn't pass now", answer.Passed ? Ui.Pass : Ui.Fail);
                shown.ToolTip = answer.Detail is { } why ? $"{answer.Label}: {why}" : answer.Label;
                result.Content = new StackPanel { Children = { shown, Ui.Words(answer.Actual, 11.5, Ui.Muted) } };
            });
            tryNow.ToolTip = "Run the check against the app as it is now";
            tryNow.Margin = new Thickness(0);
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(tryNow);
            row.Children.Add(result);
            stack.Children.Add(row);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var ok = Ui.LabelledButton(Icons.Add, add, () =>
        {
            if (Read() is { } done)
            {
                _answer = done;
                Close();
            }
        }, "MainButton");
        ok.IsDefault = true;
        var cancel = Ui.LabelledButton(Icons.Cross, "Cancel", Close);
        cancel.IsCancel = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) =>
        {
            // Beside the window that opened it, on the side away from the screen's edge.
            var area = SystemParameters.WorkArea;
            Left = owner.Left - Width - 8 >= area.Left ? owner.Left - Width - 8 : Math.Min(owner.Left + owner.ActualWidth + 8, area.Right - Width);
            Top = Math.Max(area.Top, Math.Min(owner.Top + 80, area.Bottom - ActualHeight));
            form.FocusFirst();
        };
    }

    /// <summary>
    /// Shows a step's or check's form and returns what was added, or null when it was closed. The JSON given is
    /// left as it was.
    /// </summary>
    public static JsonObject? Ask(Window owner, string title, string glyph, IReadOnlyList<FormField> fields, string key, JsonObject start, Suite suite,
        Func<IReadOnlyList<(string Name, string Text)>> values, Action<Action<TargetChoice>>? pick, Func<JsonObject, Task<CheckResult>>? tryIt = null, string add = "Add")
    {
        FormWindow? window = null;
        // While a control is picked the window fades, so the app under it can be seen and clicked.
        Action<Action<TargetChoice>>? fading = pick is null ? null : then =>
        {
            window!.Opacity = 0.25;
            window.IsHitTestVisible = false;
            pick(choice =>
            {
                window.Opacity = 1;
                window.IsHitTestVisible = true;
                then(choice);
                window.Activate();
            });
        };
        window = new FormWindow(owner, title, glyph, new FormView(fields, start, key, suite, values, fading), start, tryIt, add);
        window.ShowDialog();
        return window._answer;
    }

    // What the form holds, as the step or check; or null, saying why, when a field doesn't read.
    private JsonObject? Read()
    {
        var json = _start.DeepClone().AsObject();
        try
        {
            _form.WriteTo(json);
            _problem.Visibility = Visibility.Collapsed;
            return json;
        }
        catch (FormatException ex)
        {
            _problem.Text = ex.Message;
            _problem.Visibility = Visibility.Visible;
            return null;
        }
    }
}
