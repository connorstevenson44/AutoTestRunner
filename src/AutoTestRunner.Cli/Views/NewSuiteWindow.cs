using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AutoTestRunner.Building;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Starts a new suite in three steps: pick the app's exe, start it so its window's title is read, and point at its
/// status bar, which the cases then wait on. It writes suite.json, elements.json and the waitForStatusBar macro.
/// </summary>
internal static partial class NewSuiteWindow
{
    /// <summary>The new suite's folder, once it was made; null when the window was closed.</summary>
    public static string? Ask(Window owner, string near)
    {
        string? made = null;
        JsonNode? status = null;
        var started = false;
        var window = Ui.Dialog("New suite", owner, 540);
        using var app = new AppSession(window.Dispatcher);
        var stack = new StackPanel { Margin = new Thickness(22, 16, 22, 18) };
        stack.Children.Add(Ui.IconText(Icons.NewFolder, "New suite", Ui.Ink, 15));

        TextBox Box(string heading, string tip, double top = 12)
        {
            stack.Children.Add(Ui.Heading(heading, top));
            var box = new TextBox { FontSize = 13, Padding = new Thickness(5, 4, 5, 4), ToolTip = tip };
            System.Windows.Automation.AutomationProperties.SetName(box, heading);
            stack.Children.Add(box);
            return box;
        }
        DockPanel Row(TextBox box, string glyph, string tip, Action click)
        {
            stack.Children.Remove(box);
            var row = new DockPanel();
            var button = Ui.IconButton(glyph, tip, click, 14);
            button.Padding = new Thickness(7, 4, 7, 4);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(box);
            stack.Children.Add(row);
            return row;
        }

        var name = Box("Name", "What the suite tests, such as Order Desk");
        var exe = Box("1 · The app", "The app's .exe");
        Row(exe, Icons.Document, "Browse for the app's .exe", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
            if (dialog.ShowDialog(window) == true)
            {
                exe.Text = dialog.FileName;
                if (name.Text.Trim().Length == 0)
                    name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        });

        var title = Box("2 · Its window", "The title of the app's main window, which the runner waits for. Start the app to read it.");
        var said = Ui.Words("", 11.5, Ui.Muted);
        Row(title, Icons.Play, "Start the app and read its window's title", () =>
        {
            // Read here: the app's thread can't touch the box, which belongs to this one.
            var path = exe.Text.Trim();
            if (!File.Exists(path))
            {
                said.Text = "Pick the app's .exe first.";
                return;
            }
            said.Text = "Starting it…";
            app.Do((driver, _) => driver.Launch(path), read =>
            {
                started = true;
                title.Text = read;
                said.Text = "Read from the window. Now point at its status bar.";
            }, why => said.Text = why);
        });
        stack.Children.Add(said);

        stack.Children.Add(Ui.Heading("3 · Its status bar", 12));
        var statusShown = Ui.Words("Not picked: the cases can still wait for other controls.", 12, Ui.Muted);
        var point = Ui.LabelledButton(Icons.Pick, "Point at it", () =>
        {
            // Only a click in the app started here is taken, so with no app there would be nothing to click.
            if (!started)
            {
                statusShown.Text = "Start the app first, with the button beside its window's title.";
                return;
            }
            statusShown.Text = "Click the status bar in the app. Esc goes back.";
            app.Pick(new Dictionary<string, JsonNode>(), new VariableScope(), (facts, choice) =>
            {
                if (facts is null || choice is null)
                {
                    statusShown.Text = status is null ? "Not picked." : statusShown.Text;
                    return;
                }
                status = choice.Json;
                statusShown.Text = $"{facts.Type} \"{facts.Label}\", found by {choice.Shown}. {choice.Why}";
                statusShown.Foreground = OutlineWindow.Quality(choice.Quality);
            }, why => statusShown.Text = why);
        });
        point.ToolTip = "The status bar is what the app says when it has done something, such as Saved. The cases wait for it.";
        point.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(point);
        stack.Children.Add(statusShown);

        var folder = Box("Folder", "Where the suite's files go");
        Row(folder, Icons.Folder, "Browse for a folder", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = near };
            if (dialog.ShowDialog(window) == true)
                folder.Text = dialog.FolderName;
        });
        name.TextChanged += (_, _) => folder.Text = Path.Combine(near, Slug(name.Text));

        var problem = Ui.Words("", 12, Ui.Fail);
        problem.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(problem);
        var create = Ui.LabelledButton(Icons.NewFolder, "Make the suite", () =>
        {
            var why = name.Text.Trim().Length == 0 ? "Give the suite a name."
                : !File.Exists(exe.Text.Trim()) ? "Pick the app's .exe."
                : title.Text.Trim().Length == 0 ? "Say the title of the app's window, or start the app to read it."
                : folder.Text.Trim().Length == 0 ? "Say where the suite goes."
                : File.Exists(Path.Combine(folder.Text.Trim(), "suite.json")) ? "That folder has a suite already."
                : null;
            if (why is not null)
            {
                problem.Text = why;
                return;
            }
            try
            {
                made = SuiteMaker.Create(folder.Text.Trim(), name.Text.Trim(), exe.Text.Trim(), title.Text.Trim(), status);
                window.Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                problem.Text = ex.Message;
            }
        }, "MainButton");
        create.IsDefault = true;
        create.HorizontalAlignment = HorizontalAlignment.Right;
        create.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(create);
        window.Content = stack;
        window.Loaded += (_, _) => name.Focus();
        // The app started to read it closes with the window.
        window.Closed += (_, _) => app.Do((driver, _) =>
        {
            driver.Close();
            return true;
        }, _ => { }, _ => { });
        window.ShowDialog();
        return made;
    }

    private static string Slug(string name) => string.Join('-', Word().Matches(name.ToLowerInvariant()).Select(match => match.Value)) is { Length: > 0 } slug ? slug : "new-suite";

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex Word();
}
