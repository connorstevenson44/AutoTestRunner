using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// A drop-down of a suite's tags, each with a tick, and a box at its top that narrows the list to the tags with what is
/// typed in them: Enter ticks the first shown. Shut, it shows the tags ticked.
/// </summary>
internal sealed class TagPicker : ToggleButton
{
    private readonly HashSet<string> _on = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CheckBox> _boxes = [];
    private readonly TextBlock _shown = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox _search = new() { Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _nothing = Ui.Words("", 12, Ui.Muted);
    private readonly Popup _popup;

    /// <param name="tags">The tags to offer, in order.</param>
    /// <param name="on">Those ticked to start with.</param>
    public TagPicker(IEnumerable<string> tags, IEnumerable<string> on)
    {
        Style = (Style)Ui.Theme["PlainButton"];
        Padding = new Thickness(10, 6, 8, 6);
        MaxWidth = 320;
        ToolTip = "Tick the cases with any of the tags chosen";
        AutomationProperties.SetName(this, "Tags");
        var content = new DockPanel();
        var chevron = Ui.Glyph(Icons.Expand, 8, Ui.Muted);
        chevron.Margin = new Thickness(8, 1, 0, 0);
        DockPanel.SetDock(chevron, Dock.Right);
        content.Children.Add(chevron);
        content.Children.Add(Ui.Glyph(Icons.Tag, 14));
        content.Children.Add(_shown);
        Content = content;

        var list = new StackPanel();
        foreach (var tag in tags)
        {
            var box = new CheckBox { Content = tag, Tag = tag, Margin = new Thickness(2, 3, 2, 3), IsChecked = on.Contains(tag, StringComparer.OrdinalIgnoreCase) };
            if (box.IsChecked == true)
                _on.Add(tag);
            box.Checked += (_, _) => Turn(tag, true);
            box.Unchecked += (_, _) => Turn(tag, false);
            _boxes.Add(box);
            list.Children.Add(box);
        }
        _nothing.Visibility = Visibility.Collapsed;
        list.Children.Add(_nothing);
        AutomationProperties.SetName(_search, "Find a tag");
        _search.TextChanged += (_, _) => Narrow();
        _search.PreviewKeyDown += OnSearchKey;
        var clear = Ui.LabelledButton(Icons.Cross, "Clear", () =>
        {
            foreach (var box in _boxes)
                box.IsChecked = false;
        }, "IconButton");
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        clear.Padding = new Thickness(4, 3, 6, 3);
        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(_search);
        stack.Children.Add(new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        stack.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 4, 0, 4) });
        stack.Children.Add(clear);
        _popup = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), MinWidth = 220, Child = stack },
        };
        // A popup is a window of its own, so it takes none of the launcher's font.
        TextElement.SetFontFamily(_popup.Child, Ui.Text);
        TextElement.SetForeground(_popup.Child, Ui.Ink);
        _popup.Opened += (_, _) =>
        {
            _search.Clear();
            // Once the popup's window is shown, so typing goes straight into the box.
            Dispatcher.BeginInvoke(() => Keyboard.Focus(_search), System.Windows.Threading.DispatcherPriority.Input);
            // A click on the button while it is open closes it; it mustn't then open it again.
            IsHitTestVisible = false;
        };
        _popup.Closed += (_, _) =>
        {
            IsChecked = false;
            IsHitTestVisible = true;
        };
        Checked += (_, _) => _popup.IsOpen = true;
        Show();
    }

    /// <summary>The tags ticked; none means every case.</summary>
    public IReadOnlySet<string> On => _on;

    /// <summary>A tag was ticked or unticked.</summary>
    public event Action? Changed;

    private void Turn(string tag, bool on)
    {
        if (on)
            _on.Add(tag);
        else
            _on.Remove(tag);
        Show();
        Changed?.Invoke();
    }

    private void Show()
    {
        var ticked = _boxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag).ToList();
        _shown.Text = ticked.Count == 0 ? "Any tag" : string.Join(", ", ticked);
        _shown.Foreground = ticked.Count == 0 ? Ui.Muted : Ui.Ink;
    }

    private void Narrow()
    {
        var typed = _search.Text.Trim();
        foreach (var box in _boxes)
            box.Visibility = ((string)box.Tag).Contains(typed, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        _nothing.Text = $"No tag has \"{typed}\" in it.";
        _nothing.Visibility = _boxes.Any(box => box.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Enter ticks or unticks the first tag shown, Down goes into the list, and Esc shuts it.
    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        var first = _boxes.FirstOrDefault(box => box.Visibility == Visibility.Visible);
        switch (e.Key)
        {
            case Key.Enter when first is not null:
                first.IsChecked = first.IsChecked != true;
                _search.SelectAll();
                break;
            case Key.Down when first is not null:
                first.Focus();
                break;
            case Key.Escape:
                _popup.IsOpen = false;
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
