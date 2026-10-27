using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// A step's or check's fields, one under another, each with its name over it: a box for words, with a button that
/// puts in a kept value; a box for a control, with Pick; a box for a path, with Browse; a box for keys, with Record; a
/// tick for yes or no; and a drop-down for one of a few words. What is typed is written back as the JSON the scripts take.
/// </summary>
internal sealed class FormView : StackPanel
{
    // Yes or no fields that are yes when left out, so leaving them ticked writes nothing.
    private static readonly Dictionary<string, bool> YesWhenLeftOut = new(StringComparer.Ordinal) { ["checked"] = true, ["running"] = true, ["exists"] = true };

    private readonly List<(FormField Field, Func<string> Read, bool Was)> _fields = [];
    private readonly string _key;
    private readonly string _folder;
    private readonly Func<IReadOnlyList<(string Name, string Text)>> _values;
    private readonly Action<Action<TargetChoice>>? _pick;

    /// <param name="fields">The fields, as Forms gives them.</param>
    /// <param name="json">The step or check as it is now, which the fields start from.</param>
    /// <param name="key">The step's or check's own key, which is written even when its field is empty.</param>
    /// <param name="folder">The suite folder, which paths browsed to inside it are written from.</param>
    /// <param name="values">The kept values and suite variables, with what each holds now, to put in a box.</param>
    /// <param name="pick">Picks a control in the app, then calls back with how to find it; null where nothing can be picked.</param>
    public FormView(IReadOnlyList<FormField> fields, JsonObject json, string key, string folder, Func<IReadOnlyList<(string Name, string Text)>> values, Action<Action<TargetChoice>>? pick)
    {
        (_key, _folder, _values, _pick) = (key, folder, values, pick);
        foreach (var field in fields)
            Add(field, json);
    }

    /// <summary>Writes what the fields hold into the JSON. Throws a FormatException, saying why, for a field that doesn't read.</summary>
    public void WriteTo(JsonObject json)
    {
        foreach (var (field, read, was) in _fields)
        {
            var text = read();
            // A tick left as it was when the property wasn't there stays out.
            if (field.Type == FieldType.YesNo && !was && text == (YesWhenLeftOut.GetValueOrDefault(field.Key) ? "true" : "false"))
                text = "";
            Forms.Write(json, field, text, isKey: field.Key == _key);
        }
    }

    /// <summary>Puts the keyboard in the first box.</summary>
    public void FocusFirst()
    {
        if (FindBox(this) is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void Add(FormField field, JsonObject json)
    {
        var text = Forms.Read(json, field);
        var was = json.ContainsKey(field.Key);
        if (field.Type == FieldType.YesNo)
        {
            var tick = new CheckBox
            {
                Content = field.Label, Margin = new Thickness(0, 8, 0, 2), FontSize = 12.5,
                IsChecked = was ? text == "true" : YesWhenLeftOut.GetValueOrDefault(field.Key),
            };
            Children.Add(tick);
            _fields.Add((field, () => tick.IsChecked == true ? "true" : "false", was));
            return;
        }
        var label = Ui.Words(field.Label, 11.5, Ui.Muted);
        label.Margin = new Thickness(0, 8, 0, 2);
        Children.Add(label);
        if (field.Type == FieldType.Choice)
        {
            var choice = new ComboBox { IsEditable = true, Text = text, FontSize = 12.5, ToolTip = "Leave it empty to leave it out" };
            foreach (var option in field.Choices ?? [])
                choice.Items.Add(option);
            Children.Add(choice);
            _fields.Add((field, () => choice.Text, was));
            return;
        }
        var multiline = field.Type is FieldType.LongText or FieldType.Lines or FieldType.Json;
        var box = new TextBox
        {
            Text = text, FontSize = 12.5, Padding = new Thickness(4, 3, 4, 3), BorderBrush = Ui.Line, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = multiline, MinHeight = multiline ? 48 : 0, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        if (field.Type is FieldType.Target or FieldType.Json)
            box.FontFamily = Ui.Mono;
        System.Windows.Automation.AutomationProperties.SetName(box, field.Label);
        var row = new DockPanel();
        void Button(string glyph, string tip, Action click)
        {
            var button = Ui.IconButton(glyph, tip, click, 13);
            button.Padding = new Thickness(6, 3, 6, 3);
            button.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        if (field.Type == FieldType.Target && _pick is { } pick)
            Button(Icons.Pick, "Pick it in the app", () => pick(choice => box.Text = choice.Json is JsonValue name ? name.ToString() : JsonLine.Write(choice.Json)));
        if (field.Type == FieldType.File)
            Button(Icons.Document, "Browse for a file", () => Browse(box, folder: false));
        if (field.Type == FieldType.Folder)
            Button(Icons.Folder, "Browse for a folder", () => Browse(box, folder: true));
        if (field.Type is not (FieldType.Target or FieldType.Json or FieldType.Number))
            Button(Icons.Values, "Put in a kept value or a suite variable", () => Values(box));
        // Last, so it sits right beside the box.
        if (field.Type == FieldType.Keys)
        {
            var record = RecordKeys(box);
            DockPanel.SetDock(record, Dock.Right);
            row.Children.Add(record);
        }
        row.Children.Add(box);
        Children.Add(row);
        _fields.Add((field, () => box.Text, was));
    }

    // A menu of the kept values and suite variables, each with what it holds now; the one clicked goes in at the caret.
    private void Values(TextBox box)
    {
        var menu = new ContextMenu { PlacementTarget = box, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var values = _values();
        foreach (var (name, now) in values)
        {
            var header = new DockPanel { MinWidth = 260 };
            var shown = Ui.Words(now.Length > 40 ? now[..40] + "…" : now, 11.5, Ui.Faint, wrap: false);
            shown.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(shown, Dock.Right);
            header.Children.Add(shown);
            header.Children.Add(new TextBlock { Text = "${" + name + "}", FontFamily = Ui.Mono, FontSize = 12 });
            var item = new MenuItem { Header = header, ToolTip = now };
            item.Click += (_, _) =>
            {
                var at = box.CaretIndex;
                box.Text = box.Text.Insert(at, "${" + name + "}");
                box.CaretIndex = at + name.Length + 3;
                box.Focus();
            };
            menu.Items.Add(item);
        }
        if (values.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Nothing kept yet", IsEnabled = false });
        menu.IsOpen = true;
    }

    // A button that puts the next keys pressed in the box, such as Ctrl+S, then stops. The keys are kept from the
    // window, so Esc or Enter is recorded rather than closing it; Ctrl or Shift on its own waits for the key it is held with.
    private static Button RecordKeys(TextBox box)
    {
        const string Tip = "Record keys: click, then press them, such as Ctrl+S";
        var recording = false;
        Button? button = null;
        void Stop()
        {
            recording = false;
            ((TextBlock)button!.Content).ClearValue(TextBlock.ForegroundProperty);
            button.ToolTip = Tip;
        }
        button = Ui.IconButton(Icons.Record, Tip, () =>
        {
            if (recording)
            {
                Stop();
                return;
            }
            recording = true;
            ((TextBlock)button!.Content).Foreground = Ui.Fail;
            button.ToolTip = "Recording: press the keys, or click to stop";
            box.Focus();
            box.SelectAll();
        }, 13);
        button.Padding = new Thickness(6, 3, 6, 3);
        button.VerticalAlignment = VerticalAlignment.Top;
        box.PreviewKeyDown += (_, e) =>
        {
            if (!recording)
                return;
            e.Handled = true;
            var key = e.Key switch
            {
                Key.System => e.SystemKey,
                Key.ImeProcessed => e.ImeProcessedKey,
                Key.DeadCharProcessed => e.DeadCharProcessedKey,
                var other => other,
            };
            var held = Keyboard.Modifiers;
            if (KeyNames.Written(KeyInterop.VirtualKeyFromKey(key), held.HasFlag(ModifierKeys.Control), held.HasFlag(ModifierKeys.Alt),
                    held.HasFlag(ModifierKeys.Shift), held.HasFlag(ModifierKeys.Windows)) is not { } keys)
                return;
            box.Text = keys;
            box.CaretIndex = keys.Length;
            Stop();
        };
        // Clicking anywhere else stops it; clicking the button again stops it through the button.
        box.LostKeyboardFocus += (_, e) =>
        {
            if (recording && !ReferenceEquals(e.NewFocus, button))
                Stop();
        };
        return button;
    }

    // A path browsed to, written from ${suiteDir} when it is in the suite's folder, so the suite can move.
    private void Browse(TextBox box, bool folder)
    {
        string? path;
        if (folder)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _folder };
            path = dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { InitialDirectory = _folder, CheckFileExists = false };
            path = dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        }
        if (path is null)
            return;
        var relative = Path.GetRelativePath(_folder, path);
        box.Text = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : "${suiteDir}\\" + relative;
    }

    private static TextBox? FindBox(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is TextBox box)
                return box;
            if (child is Panel inner && FindBox(inner) is { } found)
                return found;
        }
        return null;
    }
}
