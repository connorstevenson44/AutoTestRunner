using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AutoTestRunner.Building;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// A suite's controls. Named: each name in elements.json, how it finds its control, and where it is used, to point
/// out the control in the app, rename it everywhere, or see which are used nowhere. Without ids: the controls in the
/// app a test would use that have no AutomationId, red with nothing to find them by and amber with only their words,
/// each with the line of XAML that gives it an id.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The app session and the outline go when the window closes.")]
internal sealed class ControlsWindow : Window
{
    private readonly Options _options;
    private readonly Action _changed;
    private readonly AppSession _app;
    private readonly OutlineWindow _outline = new();
    private readonly StackPanel _named = new(), _gaps = new();
    private readonly TextBlock _appState;
    private readonly ToggleButton _unused;
    private Suite _suite;

    private ControlsWindow(Window owner, Suite suite, Options options, Action changed, bool ids)
    {
        (_suite, _options, _changed) = (suite, options, changed);
        _app = new AppSession(Dispatcher);
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = $"{suite.Name}: controls";
        Owner = owner;
        Width = 640;
        Height = Math.Min(680, SystemParameters.WorkArea.Height - 60);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = Ui.Text;
        Foreground = Ui.Ink;

        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var open = Ui.LabelledButton(Icons.Play, $"Open {suite.App.Window}", OpenApp);
        open.ToolTip = "Start the app, to point out controls in it and look for controls without ids";
        DockPanel.SetDock(open, Dock.Right);
        top.Children.Add(open);
        _appState = Ui.Words("", 12, Ui.Muted);
        top.Children.Add(_appState);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var tabs = new TabControl { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Ui.Line };
        var namedTab = new DockPanel();
        var filter = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        _unused = Ui.Chip(Icons.Warning, "Used nowhere", "Show only the names no step, check or other element uses");
        _unused.Click += (_, _) => ShowNamed();
        filter.Children.Add(_unused);
        DockPanel.SetDock(filter, Dock.Top);
        namedTab.Children.Add(filter);
        namedTab.Children.Add(new ScrollViewer { Content = _named, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tabs.Items.Add(new TabItem { Header = Ui.IconText(Icons.Controls, "Named controls", Ui.Ink, 12.5, FontWeights.Normal), Content = namedTab });

        var gapsTab = new DockPanel();
        var look = Ui.LabelledButton(Icons.Ids, "Look at the app", Survey);
        look.ToolTip = "Read every control in the app's windows as they are now. Open each screen you want checked, then look again.";
        look.HorizontalAlignment = HorizontalAlignment.Left;
        look.Margin = new Thickness(0, 8, 0, 6);
        DockPanel.SetDock(look, Dock.Top);
        gapsTab.Children.Add(look);
        gapsTab.Children.Add(new ScrollViewer { Content = _gaps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tabs.Items.Add(new TabItem { Header = Ui.IconText(Icons.Ids, "Without ids", Ui.Ink, 12.5, FontWeights.Normal), Content = gapsTab });
        tabs.SelectedIndex = ids ? 1 : 0;
        root.Children.Add(tabs);
        Content = root;

        _gaps.Children.Add(Ui.Words("Open the app, go to a screen, and click Look at the app.", 12.5, Ui.Muted));
        Closed += (_, _) =>
        {
            _outline.Close();
            _app.Dispose();
        };
        Loaded += (_, _) => Attach();
        ShowNamed();
    }

    /// <summary>Shows a suite's controls, on its named controls or its controls without ids. changed is called after a rename.</summary>
    public static void Open(Window owner, Suite suite, Options options, Action changed, bool ids) =>
        new ControlsWindow(owner, suite, options, changed, ids).ShowDialog();

    // ===== The app =====

    private VariableScope Globals() => Runner.Variables(_suite, Path.GetTempPath(), _options.Variables);

    private string Exe() => Runner.SuitePath(_suite, Globals().Expand(_suite.App.Exe));

    // Uses the app when it is open already. What the app's thread needs is read here, as a rename can change the suite.
    private void Attach()
    {
        var (exe, window) = (Exe(), _suite.App.Window);
        _app.Do((driver, _) => driver.Attach(exe, window), open => _appState.Text = open ? $"{window} is open." : $"{window} isn't open.", Say);
    }

    private void OpenApp()
    {
        var (exe, app) = (Exe(), _suite.App);
        _appState.Text = $"Starting {app.Window}…";
        _app.Do((driver, _) =>
        {
            driver.Start(app, exe);
            return true;
        }, _ => _appState.Text = $"{app.Window} is open.", Say);
    }

    private void Say(string text) => _appState.Text = text;

    // ===== Named controls =====

    private void ShowNamed()
    {
        _named.Children.Clear();
        var uses = ElementUses.Find(_suite);
        var shown = 0;
        foreach (var (name, target) in _suite.Elements.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var used = uses.GetValueOrDefault(name) ?? [];
            if (_unused.IsChecked == true && used.Count > 0)
                continue;
            shown++;
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            void Button(string glyph, string tip, Action click)
            {
                var button = Ui.IconButton(glyph, tip, click, 13);
                button.Padding = new Thickness(6, 3, 6, 3);
                buttons.Children.Add(button);
            }
            Button(Icons.Look, "Point it out in the app", () => PointOut(name));
            Button(Icons.Name, "Rename it everywhere it is used", () => Rename(name));
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            var count = Ui.Pill(Ui.Words(used.Count == 1 ? "1 use" : $"{used.Count} uses", 11, used.Count == 0 ? Ui.Hold : Ui.Muted, wrap: false),
                used.Count == 0 ? Ui.HoldSoft : Ui.Surface, 8, new Thickness(7, 1, 7, 1));
            count.ToolTip = used.Count == 0 ? "No step, check or other element uses it" : string.Join("\n", used.GroupBy(file => file).Select(file => file.Count() == 1 ? file.Key : $"{file.Key} ({file.Count()})"));
            count.Margin = new Thickness(8, 0, 6, 0);
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            var words = new StackPanel { Margin = new Thickness(4, 4, 0, 4) };
            words.Children.Add(new TextBlock { Text = name, FontFamily = Ui.Mono, FontSize = 12.5, Foreground = Ui.Ink });
            words.Children.Add(new TextBlock { Text = JsonLine.Write(target), FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Faint, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(words);
            row.MouseEnter += (_, _) => row.Background = Ui.Surface;
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            _named.Children.Add(row);
        }
        if (shown == 0)
            _named.Children.Add(Ui.Words(_unused.IsChecked == true ? "Every name is used." : "elements.json names no controls yet. The builder's Name it adds them.", 12.5, Ui.Muted));
    }

    private void PointOut(string name)
    {
        var elements = _suite.Elements;
        var globals = Globals();
        _app.Do((_, inspector) => inspector.Locate(Targets.Resolve(name, globals, elements)), box =>
        {
            if (box is { } found)
            {
                _outline.Show(found, name);
                Say($"{name} is outlined in the app.");
            }
            else
            {
                _outline.Hide();
                Say($"{name} isn't on the screen the app is showing, or the app isn't open.");
            }
        }, Say);
    }

    private void Rename(string name)
    {
        if (Ui.Prompt(this, "Rename", $"New name for {name}. Every step, check, element and setting that uses it is changed.", name, "Rename", Icons.Name, tall: false) is not { } to
            || (to = to.Trim()) == name || to.Length == 0)
            return;
        try
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(to, @"^[A-Za-z_]\w*$"))
                throw new ArgumentException("A name is letters and digits, such as addOrder.");
            var files = ElementUses.Rename(_suite, name, to);
            _suite = Suite.Load(_suite.Folder);
            _changed();
            Say($"Renamed {name} to {to} in {files.Count} {(files.Count == 1 ? "file" : "files")}.");
            ShowNamed();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or SuiteException)
        {
            MessageBox.Show(this, ex is SuiteException suite ? string.Join("\n", suite.Problems) : ex.Message, "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ===== Controls without ids =====

    private void Survey()
    {
        _gaps.Children.Clear();
        _gaps.Children.Add(Ui.Spinner(16, Ui.Accent));
        var (exe, window) = (Exe(), _suite.App.Window);
        _app.Do((driver, inspector) => driver.Attach(exe, window) ? IdReport.Gaps(inspector.Survey()) : null, ShowGaps, why =>
        {
            _gaps.Children.Clear();
            Say(why);
        });
    }

    private void ShowGaps(IReadOnlyList<IdGap>? gaps)
    {
        _gaps.Children.Clear();
        if (gaps is null)
        {
            _gaps.Children.Add(Ui.Words($"{_suite.App.Window} isn't open. Open it, go to a screen, and look again.", 12.5, Ui.Muted));
            return;
        }
        var unfindable = gaps.Count(gap => !gap.Named);
        _gaps.Children.Add(Ui.Words(gaps.Count == 0 ? "Every control a test would use on this screen has an id."
            : $"{gaps.Count} {(gaps.Count == 1 ? "control has" : "controls have")} no AutomationId: {unfindable} with nothing to find {(unfindable == 1 ? "it" : "them")} by, {gaps.Count - unfindable} only by {(gaps.Count - unfindable == 1 ? "its" : "their")} words. Add the XAML shown, as Microsoft recommends for UI Automation.",
            12, Ui.Muted));
        foreach (var gap in gaps)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = gap.Named ? Ui.Hold : Ui.Fail, Margin = new Thickness(2, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            dot.ToolTip = gap.Named ? "Only found by its words, which change with the language and the wording" : "Nothing to find it by but where it is";
            row.Children.Add(dot);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var copy = Ui.IconButton(Icons.Copy, "Copy the XAML", () => Clipboard.SetText(gap.Xaml), 13);
            copy.Padding = new Thickness(6, 3, 6, 3);
            var show = Ui.IconButton(Icons.Look, "Point it out in the app", () => _outline.Show(gap.Control.Bounds, gap.Control.Type, gap.Named ? TargetQuality.Wording : TargetQuality.Ambiguous), 13);
            show.Padding = new Thickness(6, 3, 6, 3);
            buttons.Children.Add(show);
            buttons.Children.Add(copy);
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            var words = new StackPanel();
            var where = gap.Control.Ancestors is [var window, ..] && window.Name.Length > 0 ? $" in {window.Name}" : "";
            words.Children.Add(Ui.Words(gap.Named ? $"{gap.Control.Type} \"{gap.Control.Name}\"{where}" : $"{gap.Control.Type} with no words{where}", 12.5, Ui.Ink));
            words.Children.Add(new TextBox { Text = gap.Xaml, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Muted });
            row.Children.Add(words);
            _gaps.Children.Add(row);
        }
    }
}
