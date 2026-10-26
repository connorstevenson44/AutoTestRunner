using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using AutoTestRunner.Building;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// The builder panel's toolbars. The first adds steps: Record, which <see cref="RecordKey"/> also turns on and off
/// from the app; the steps used most, a button each; and the rest in a menu each, one level deep. Under it, in amber
/// as the outline round a control, the pick tools: the tool is chosen first and the control clicked in the app next,
/// so a step about a control takes two clicks. A tool that can't be used on a control says why on its outline, and a
/// click there does nothing. Ctrl+click while recording uses the last tool again.
/// </summary>
internal sealed partial class BuilderPanel
{
    /// <summary>
    /// The key that turns recording on and off, pressed in the app or the panel. F9 means nothing to Windows or to
    /// WPF's own controls, unlike F1 (help), F2 (rename), F4 (open a drop-down), F5 (refresh), F6 (next pane), F10
    /// (the menu bar) and F12 (the debugger), so the app under test is the least likely to need it.
    /// </summary>
    public const string RecordKey = "F9";

    // The steps of the In the app and On this computer menus: those the buttons and the pick tools don't add.
    private static readonly (string Kind, string Title, string Tip)[] AppSteps =
    [
        ("expand", "Open tree items", "Open each item on a path in a tree, and select the last"),
        ("drag", "Drag and drop", "Drag a control and drop it on another"),
        ("tab", "Open a tab", "Open a tab by its header"),
        ("useWindow", "Use another window", "Drive another of the app's windows from now on, as after a sign-in"),
    ];

    private static readonly (string Kind, string Title, string Tip)[] ComputerSteps =
    [
        ("copy", "Copy a file or folder", "Copy a file, or a folder and everything in it"),
        ("delete", "Delete a file or folder", "Delete it; nothing being there is fine"),
        ("writeFile", "Write a file", "Write a text to a file, or add it to the end"),
        ("unzip", "Unzip", "Unzip a .zip file into a folder"),
        ("setJsonValue", "Change a JSON setting", "Set a setting in a JSON file, such as Orders:Region"),
        ("setAppSetting", "Change an app setting", "Set an appSettings value in a .config file"),
        ("startService", "Start a service", "Start a Windows service and wait until it has"),
        ("stopService", "Stop a service", "Stop a Windows service and wait until it has"),
        ("run", "Run a program", "Run a program with arguments"),
    ];

    // Every button of both toolbars, to turn off while the builder is busy; and each pick tool's button, with the tools
    // it offers, to show which is waiting for a control.
    private readonly List<ButtonBase> _tools = [];
    private readonly List<(ToggleButton Button, PickTool[] Tools)> _pickButtons = [];

    // The pick tool waiting for a control to be clicked, and the one used last, which Ctrl+click uses again.
    private PickTool? _armed, _lastTool;

    // The tool a click in the app is for now: the one waiting, or while recording the last one, for Ctrl+click.
    private PickTool? ToolNow => _armed ?? (Mode == BuildMode.Recording ? _lastTool : null);

    // ===== The toolbars =====

    private StackPanel Toolbars()
    {
        var steps = new WrapPanel();
        steps.Children.Add(_record);
        steps.Children.Add(new Border { Width = 1, Background = Ui.Line, Margin = new Thickness(4, 6, 6, 6) });
        steps.Children.Add(StepTool(Icons.Keyboard, "Press keys", "Press a key or a shortcut, such as Ctrl+S.", () => NewStep("key", "Press keys", Icons.Keyboard)));
        steps.Children.Add(StepTool(Icons.Menu, "Pick from a menu", "Pick each item of a menu in turn, such as File then Export.", () => NewStep("menu", "Pick from a menu", Icons.Menu)));
        steps.Children.Add(StepTool(Icons.Clock, "Wait", "Wait a number of seconds. Where the app shows something to wait for, an amber Wait tool is more reliable.", WaitAWhile));
        steps.Children.Add(StepTool(Icons.Message, "Show a message", "A step that only shows the tester a message, such as what to notice.", SaySomething));
        steps.Children.Add(MenuTool(Icons.Console, "Script", "Run a PowerShell or C# script, or write a new one.", menu =>
        {
            menu.Items.Add(Item(Icons.Console, "Run a script…", "Choose a .ps1 or .cs file: it runs now, and is added as a step", PickScript));
            menu.Items.Add(Item(Icons.Document, "New PowerShell script…", "Create a .ps1 file in the tools folder and open it to write. Its step plays with Run to here.", () => NewScript(csharp: false)));
            menu.Items.Add(Item(Icons.Document, "New C# script…", "Create a .cs file in the tools folder and open it to write. It runs with dotnet run, which needs the .NET 10 SDK.", () => NewScript(csharp: true)));
        }));
        steps.Children.Add(MenuTool(Icons.Macro, "Use a macro", "Add a step that runs one of the suite's macros, with its parameters.", ListMacros));
        steps.Children.Add(MenuTool(Icons.App, "In the app", "Open tree items, drag and drop, open a tab, or use another window.", menu => ListSteps(menu, AppSteps, Icons.App)));
        steps.Children.Add(MenuTool(Icons.Computer, "On this computer", "Copy, delete or write files, change settings, start and stop services, or run a program.",
            menu => ListSteps(menu, ComputerSteps, Icons.Computer)));

        var picks = new WrapPanel();
        picks.Children.Add(PickButton(Icons.Pointer, "Click", PickTools.Click));
        picks.Children.Add(PickButton(Icons.Mouse, "Point at", PickTools.Hover));
        picks.Children.Add(PickButton(Icons.Wait, "Wait for it", PickTools.WaitFor, PickTools.WaitGone, PickTools.WaitText, PickTools.WaitEnabled));
        picks.Children.Add(PickButton(Icons.Copy, "Keep its text", PickTools.Keep));
        picks.Children.Add(PickButton(Icons.CheckList, "Check it", [PickTools.CheckShows, PickTools.CheckText, null, .. PickTools.Checks]));
        picks.Children.Add(PickButton(Icons.Name, "Name it", PickTools.Name));
        picks.Children.Add(PickButton(Icons.More, "More", PickTools.SetValue, PickTools.Watch, PickTools.AfterClick));
        var heading = Ui.Heading("Pick a control to…");
        heading.Foreground = Ui.AmberInk;
        heading.Margin = new Thickness(4, 0, 0, 2);
        var pickBar = new StackPanel();
        pickBar.Children.Add(heading);
        pickBar.Children.Add(picks);

        var bars = new StackPanel { Margin = new Thickness(0, 10, 4, 8) };
        bars.Children.Add(new Border { Background = Ui.Surface, CornerRadius = new CornerRadius(6), Padding = new Thickness(2), Child = steps });
        bars.Children.Add(new Border
        {
            Background = Ui.AmberSoft, BorderBrush = Ui.Amber, BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 5, 2, 2), Margin = new Thickness(0, 6, 0, 0), Child = pickBar,
        });
        return bars;
    }

    // Record, with its key under it, as the run panel shows its keys.
    private ToggleButton RecordButton()
    {
        var stack = new StackPanel { MinWidth = 34 };
        var icon = Ui.Glyph(Icons.Record, 17, Ui.Fail);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock { Text = RecordKey, FontSize = 10, Foreground = Ui.Muted, Margin = new Thickness(0, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        var record = new ToggleButton
        {
            Content = stack, Style = (Style)Ui.Theme["ToggleIcon"], Padding = new Thickness(8, 4, 8, 3), IsEnabled = false,
            ToolTip = Tip("Record", $"Each click, choice and entry in the app becomes a step. {RecordKey} turns it on and off from the app too.", RecordKey),
        };
        AutomationProperties.SetName(record, "Record");
        record.Click += (_, _) =>
        {
            _recordOn = record.IsChecked == true;
            PutDown();
            ShowMode();
        };
        _tools.Add(record);
        return record;
    }

    private Button StepTool(string glyph, string title, string tip, Action click)
    {
        var button = new Button { Content = Ui.Glyph(glyph, 17), ToolTip = Tip(title, tip), Style = (Style)Ui.Theme["IconButton"], Padding = new Thickness(9, 8, 9, 8), IsEnabled = false };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) => click();
        _tools.Add(button);
        return button;
    }

    // A tool that opens a menu under it, filled as it opens, as the suite's macros can change while building.
    private Button MenuTool(string glyph, string title, string tip, Action<ContextMenu> fill)
    {
        var button = new Button { Content = WithChevron(glyph), ToolTip = Tip(title, tip), Style = (Style)Ui.Theme["IconButton"], Padding = new Thickness(9, 8, 7, 8), IsEnabled = false };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            fill(menu);
            menu.IsOpen = true;
        };
        _tools.Add(button);
        return button;
    }

    // A pick tool's button: one tool, used on the next control clicked, or a menu of them. Null in a menu is a line.
    private ToggleButton PickButton(string glyph, string title, params PickTool?[] tools)
    {
        var one = tools is [{ } only] ? only : null;
        var button = new ToggleButton
        {
            Content = one is null ? WithChevron(glyph) : Ui.Glyph(glyph, 17), Style = (Style)Ui.Theme["PickTool"],
            Padding = new Thickness(9, 8, one is null ? 7 : 9, 8), IsEnabled = false,
            ToolTip = one is not null ? Tip(one.Title, one.Tip + " Choose it, then click the control in the app.")
                : Tip(title, string.Join("\n", tools.OfType<PickTool>().Select(tool => tool.Title.TrimEnd('…')))),
        };
        AutomationProperties.SetName(button, one?.Title ?? title);
        _pickButtons.Add((button, [.. tools.OfType<PickTool>()]));
        _tools.Add(button);
        button.Click += (_, _) =>
        {
            if (one is not null)
            {
                Arm(one);
                return;
            }
            // The button shows on only while one of its tools waits, not because it was clicked.
            ShowArmed();
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            foreach (var tool in tools)
            {
                if (tool is null)
                    menu.Items.Add(new Separator());
                else
                    menu.Items.Add(Item(glyph, tool.Title, tool.Tip, () => Arm(tool), Ui.AmberInk));
            }
            menu.IsOpen = true;
        };
        return button;
    }

    // A glyph with a small chevron after it, for a button that opens a menu.
    private static StackPanel WithChevron(string glyph)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Ui.Glyph(glyph, 17));
        var chevron = Ui.Glyph(Icons.Expand, 7);
        chevron.Margin = new Thickness(3, 2, 0, 0);
        chevron.Opacity = 0.7;
        content.Children.Add(chevron);
        return content;
    }

    private static MenuItem Item(string glyph, string title, string tip, Action click, System.Windows.Media.Brush? color = null)
    {
        var item = new MenuItem { Header = title, Icon = Ui.Glyph(glyph, 14, color ?? Ui.Accent), ToolTip = tip };
        item.Click += (_, _) => click();
        return item;
    }

    // A tool's tip: what it is called, with its key, and what it does.
    private static TextBlock Tip(string title, string text, string? key = null)
    {
        var tip = new TextBlock { MaxWidth = 300, TextWrapping = TextWrapping.Wrap };
        tip.Inlines.Add(new Run(title) { FontWeight = FontWeights.SemiBold });
        if (key is not null)
            tip.Inlines.Add(new Run("   " + key) { Foreground = Ui.Faint });
        tip.Inlines.Add(new LineBreak());
        tip.Inlines.Add(new Run(text));
        return tip;
    }

    private void EnableTools(bool on)
    {
        foreach (var tool in _tools)
            tool.IsEnabled = on;
    }

    // ===== Steps from the toolbar =====

    private void ListSteps(ContextMenu menu, (string Kind, string Title, string Tip)[] kinds, string glyph)
    {
        foreach (var (kind, title, tip) in kinds)
            menu.Items.Add(Item(glyph, title + "…", tip, () => NewStep(kind, title, glyph)));
    }

    private void PickScript()
    {
        var tools = Path.Combine(_suite.Folder, "tools");
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Run a script", Filter = "Scripts (*.ps1, *.cs)|*.ps1;*.cs|PowerShell (*.ps1)|*.ps1|C# (*.cs)|*.cs", InitialDirectory = Directory.Exists(tools) ? tools : _suite.Folder,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        Play?.Invoke(StepMaker.Script(Path.GetRelativePath(_suite.Folder, dialog.FileName)), true);
    }

    // A new script is PowerShell, which every Windows machine has, or C#, for setup that is easier written as code.
    private void NewScript(bool csharp)
    {
        if (Ui.Prompt(this, csharp ? "New C# script" : "New PowerShell script", "Name of the script, such as Check the orders file. It is saved in the tools folder and opened for editing.", "", "Create", Icons.Document, tall: false) is not { } name
            || name.Trim().Length == 0)
            return;
        var relative = ScriptEditor.Create(_suite, name, _draft.Name, csharp);
        OpenScript(relative);
        var step = StepMaker.Script(relative);
        step.Text = name.Trim();
        // It is played with Run to here, once it is written.
        step.State = DraftStepState.NotPlayed;
        AddStep?.Invoke(step);
    }

    private void OpenScript(string relative)
    {
        if (!ScriptEditor.Open(Path.Combine(_suite.Folder, relative)))
            Note($"Couldn't open Notepad. The script is {Path.Combine(_suite.Folder, relative)}.");
    }

    private void SaySomething()
    {
        if (Ui.Prompt(this, "Show a message", "Message to show the tester at this step:", "", "Add", Icons.Message) is { } text && text.Trim().Length > 0)
            AddStep?.Invoke(StepMaker.Say(text.Trim()));
    }

    private void WaitAWhile()
    {
        if (Ui.Prompt(this, "Wait", "Seconds to wait. Where the app shows something to wait for, use an amber Wait tool on it instead: that is more reliable.", "1", "Add", Icons.Clock, tall: false) is { } text
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            Play?.Invoke(StepMaker.Wait(seconds), true);
    }

    // The suite's macros, and those made here: one chosen asks for its parameters, and a step that runs it is played.
    private void ListMacros(ContextMenu menu)
    {
        var macros = _suite.Macros.Values.Select(macro => (macro.Name, Parameters: (IReadOnlyDictionary<string, string?>)macro.Parameters))
            .Concat(_draft.NewMacros.Select(pair => (Name: pair.Key, Parameters: (IReadOnlyDictionary<string, string?>)MacroMaker.Read(pair.Key, "", pair.Value).Parameters)))
            .OrderBy(macro => macro.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var (name, parameters) in macros)
            menu.Items.Add(Item(Icons.Macro, name, $"Run {name}, with its parameters from a form", () => UseMacro(name, parameters)));
        if (macros.Count == 0)
            menu.Items.Add(new MenuItem { Header = "The suite has no macros yet. Pick steps with Shift+click to make one.", IsEnabled = false });
    }

    private void UseMacro(string name, IReadOnlyDictionary<string, string?> parameters)
    {
        var fields = parameters.Select(pair => new FormField(pair.Key, pair.Value is { } fallback ? $"{pair.Key} (left empty: {fallback})" : pair.Key, FieldType.Text))
            .Append(new FormField("as", "Keep what it hands back as", FieldType.Text)).ToList();
        if (FormWindow.Ask(this, $"Use {name}", Icons.Macro, fields, "", new JsonObject(), _suite.Folder, ValuesNow, null, add: "Do it") is not { } given)
            return;
        var call = new JsonObject { ["call"] = name };
        var with = new JsonObject();
        foreach (var (key, value) in given.Where(pair => pair.Key != "as"))
            with[key] = value?.DeepClone();
        if (with.Count > 0)
            call["with"] = with;
        if (given["as"] is { } keep)
            call["as"] = keep.DeepClone();
        Play?.Invoke(StepMaker.FromJson(call, DraftStepState.Played), true);
    }

    // ===== Pick tools =====

    // Picks a tool up to use on the next control clicked, or puts it down when it was already waiting.
    private void Arm(PickTool tool)
    {
        if (_armed == tool)
            PutDown();
        else
        {
            _armed = tool;
            ShowArmed();
        }
        ShowMode();
    }

    private void PutDown()
    {
        _armed = null;
        ShowArmed();
    }

    // Each pick button is on while one of its tools waits for a control.
    private void ShowArmed()
    {
        foreach (var (button, tools) in _pickButtons)
            button.IsChecked = _armed is not null && tools.Contains(_armed);
    }

    /// <summary>Picks a tool up, as its button does: for the preview's pictures.</summary>
    internal void PickUp(PickTool tool)
    {
        _armed = tool;
        ShowArmed();
        ShowMode();
    }

    // Uses a pick tool on the control clicked: most play a step, and add it when it works; the checks go in the
    // checkpoint; the rest ask a little first.
    private void Use(PickTool tool, ControlFacts facts, TargetChoice choice)
    {
        var text = PickTool.TextOf(facts);
        switch (tool.Action)
        {
            case PickAction.Click:
                Play?.Invoke(StepMaker.Click(choice, facts), true);
                break;
            case PickAction.Hover:
                Play?.Invoke(StepMaker.Hover(choice, facts), true);
                break;
            case PickAction.WaitFor:
                Play?.Invoke(StepMaker.Find(choice, facts), true);
                break;
            case PickAction.WaitGone:
                Play?.Invoke(StepMaker.WaitGone(choice, facts), true);
                break;
            case PickAction.WaitText:
                Play?.Invoke(StepMaker.WaitText(choice, facts, text), true);
                break;
            case PickAction.WaitEnabled:
                Play?.Invoke(StepMaker.WaitEnabled(choice, facts), true);
                break;
            case PickAction.Keep:
                if (Ui.Prompt(this, "Keep its text", $"What to call what it says (\"{text}\"), as ${{name}} in later steps and checks:", Variable(choice, facts), "Keep", Icons.Note, tall: false) is { } kept
                    && Identifier().IsMatch(kept.Trim()))
                    Play?.Invoke(StepMaker.Keep(choice, facts, kept.Trim()), true);
                break;
            case PickAction.CheckShows:
                AddCheck(new JsonObject { ["shows"] = choice.Json.DeepClone() }, $"{choice.Shown} shows");
                break;
            case PickAction.CheckText:
            {
                var name = Variable(choice, facts);
                Play?.Invoke(StepMaker.Keep(choice, facts, name), true);
                AddCheck(new JsonObject { ["equals"] = $"${{{name}}}", ["to"] = text }, $"{name} is \"{text}\"");
                break;
            }
            case PickAction.Check when Forms.CheckKinds.FirstOrDefault(kind => kind.Key == tool.Check) is { } kind:
                NewCheck(kind, choice.Json);
                break;
            case PickAction.SetValue:
                NewStep("setValue", "Set a value", Icons.Slider, new JsonObject { ["setValue"] = choice.Json.DeepClone(), ["to"] = facts.Value ?? "" });
                break;
            case PickAction.Name:
                NameIt(choice, facts);
                break;
            case PickAction.Watch:
                if (Element(choice, facts) is { } watched)
                    Watch(watched);
                break;
            case PickAction.AfterClick:
                if (Element(choice, facts) is { } clicked && HookWindow.Ask(this, _suite, clicked) is { } hook)
                {
                    lock (_draft.NewAfterClick)
                        _draft.NewAfterClick.Add(hook);
                    _changes++;
                    Note($"After each click on {clicked}, {hook.Call} is added as a step. Saved to suite.json with the case.");
                }
                break;
        }
    }

    // A hook names its control in elements.json: a control that has no name there yet is named first.
    private string? Element(TargetChoice choice, ControlFacts facts)
    {
        if (choice.Json is JsonValue value && value.TryGetValue(out string? name))
            return name;
        if (choice.Json is JsonObject { Count: 1 } selector && selector["element"] is JsonValue element)
            return element.GetValue<string>();
        return NameIt(choice, facts)?.Shown;
    }

    private void Watch(string element)
    {
        var answer = MessageBox.Show(this,
            $"When a step changes the text of {element}, a wait for the new text is added.\n\nAlso check its text at the checkpoint?",
            "Watch it", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
            return;
        var call = _suite.Macros.ContainsKey(Suite.WaitMacro(element)) ? Suite.WaitMacro(element) : null;
        lock (_draft.NewWatch)
        {
            _draft.NewWatch.RemoveAll(hook => hook.Element == element);
            _draft.NewWatch.Add(new WatchHook(element, call, answer == MessageBoxResult.Yes));
        }
        _changes++;
        Note($"Watching {element}{(call is null ? "" : $" with {call}")}. Saved to suite.json with the case.");
    }

    private TargetChoice? NameIt(TargetChoice choice, ControlFacts facts)
    {
        if (Ui.Prompt(this, "Name it", "Name for this control in elements.json. Steps will refer to it by this name.", Camel(facts.Label), "Name it", Icons.Name, tall: false) is not { } name)
            return null;
        name = name.Trim();
        try
        {
            if (!Identifier().IsMatch(name))
                throw new ArgumentException("A name is letters and digits, such as addOrder.");
            var named = _draft.NameControl(choice, name, _suite.Elements);
            foreach (var (step, row) in _rows)
                if (step.Target == named)
                    Refresh(step, row);
            foreach (var check in _checks.Children.OfType<FrameworkElement>())
                if (check.Tag is JsonObject json && json["shows"] is JsonValue)
                    ((TextBlock)((DockPanel)check).Children[^1]).Text = $"{name} shows";
            _changes++;
            return named;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Name it", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
    }

    // A name for a kept value: the element's, else a word or two of what it says, not taken by another step.
    private string Variable(TargetChoice choice, ControlFacts facts)
    {
        var name = choice.Json is JsonValue element ? element.GetValue<string>() : Camel(Regex.Replace(facts.Label, @"[^A-Za-z ]", " ").Trim() is { Length: > 0 } words ? words : "kept");
        var taken = _draft.Steps.Select(step => step.Json["as"]?.GetValue<string>()).OfType<string>().ToHashSet();
        var unique = name;
        for (var copy = 2; taken.Contains(unique); copy++)
            unique = $"{name}{copy}";
        return unique;
    }

    private static string Camel(string text)
    {
        var words = Regex.Matches(text, "[A-Za-z0-9]+").Select(match => match.Value).Take(3).ToList();
        if (words.Count == 0)
            return "control";
        var name = string.Concat(words.Select((word, index) => index == 0 ? word.ToLowerInvariant() : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
        return char.IsDigit(name[0]) ? "control" + name : name;
    }
}
