using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AutoTestRunner.Building;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// The list that drops down under the caret when ${ is typed in a box, of the names that can go there: the kept values
/// and suite variables in ink, and readFile and the suite's helpers in the step colour with brackets, as they do
/// something rather than hold a value. Up and Down move through it, Enter or Tab puts the one picked in, Esc closes it.
/// </summary>
internal sealed class TypeAheadList
{
    private readonly TextBox _box;
    private readonly Func<IReadOnlyList<Suggestion>> _all;
    private readonly ListBox _list = new() { MaxHeight = 220, MinWidth = 300, BorderThickness = new Thickness(0), Focusable = false };
    private readonly Popup _popup;
    private int _start;

    private TypeAheadList(TextBox box, Func<IReadOnlyList<Suggestion>> all)
    {
        (_box, _all) = (box, all);
        _popup = new Popup
        {
            Child = new Border { Child = _list, Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) },
            PlacementTarget = box, Placement = PlacementMode.Bottom,
        };
        // A click on a name puts it in without taking the keyboard from the box, so typing goes on where it was.
        _list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, (DependencyObject)e.OriginalSource) is ListBoxItem item)
            {
                _list.SelectedItem = item;
                Put();
                e.Handled = true;
            }
        };
        box.TextChanged += (_, _) => Update();
        box.SelectionChanged += (_, _) => Update();
        box.PreviewKeyDown += Pressed;
        box.LostKeyboardFocus += (_, _) => _popup.IsOpen = false;
    }

    /// <summary>Gives a box the list, of what all gives: it is asked each time the list opens, as kept values change.</summary>
    public static void Attach(TextBox box, Func<IReadOnlyList<Suggestion>> all) => _ = new TypeAheadList(box, all);

    private void Update()
    {
        if (!_box.IsKeyboardFocused || TypeAhead.Typing(_box.Text, _box.CaretIndex) is not { } typing)
        {
            _popup.IsOpen = false;
            return;
        }
        var matching = TypeAhead.Matching(_all(), typing.Typed);
        if (matching.Count == 0)
        {
            _popup.IsOpen = false;
            return;
        }
        _start = typing.Start;
        _list.Items.Clear();
        foreach (var suggestion in matching)
            _list.Items.Add(new ListBoxItem { Content = Line(suggestion), Tag = suggestion, Focusable = false, Padding = new Thickness(6, 2, 6, 2), ToolTip = suggestion.Detail });
        _list.SelectedIndex = 0;
        _popup.PlacementRectangle = _box.GetRectFromCharacterIndex(_start);
        _popup.IsOpen = true;
    }

    private static DockPanel Line(Suggestion suggestion)
    {
        var line = new DockPanel();
        var detail = Ui.Words(suggestion.Detail.Length > 46 ? suggestion.Detail[..46] + "…" : suggestion.Detail, 11.5, Ui.Faint, wrap: false);
        detail.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(detail, Dock.Right);
        line.Children.Add(detail);
        line.Children.Add(suggestion.IsCall
            ? new TextBlock { Text = suggestion.Name + "()", FontFamily = Ui.Mono, FontSize = 12, Foreground = Ui.Step, FontWeight = FontWeights.SemiBold }
            : new TextBlock { Text = suggestion.Name, FontFamily = Ui.Mono, FontSize = 12, Foreground = Ui.Ink });
        return line;
    }

    private void Pressed(object sender, KeyEventArgs e)
    {
        if (!_popup.IsOpen)
            return;
        var count = _list.Items.Count;
        if (e.Key is Key.Down or Key.Up)
        {
            _list.SelectedIndex = (_list.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1)) % count;
            _list.ScrollIntoView(_list.SelectedItem);
        }
        else if (e.Key is Key.Enter or Key.Tab)
            Put();
        else if (e.Key == Key.Escape)
            _popup.IsOpen = false;
        else
            return;
        // Handled, so Enter doesn't press the window's default button and Esc doesn't close the window.
        e.Handled = true;
    }

    private void Put()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: Suggestion suggestion })
            return;
        var (text, caret) = TypeAhead.Put(_box.Text, _start, _box.CaretIndex, suggestion);
        _box.Text = text;
        _box.CaretIndex = caret;
    }
}
