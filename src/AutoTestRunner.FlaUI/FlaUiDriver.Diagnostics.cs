using System.Globalization;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace AutoTestRunner.FlaUI;

public sealed partial class FlaUiDriver
{
    public void Screenshot(string path) => global::FlaUI.Core.Capturing.Capture.Screen().ToFile(path);

    public string DumpTree()
    {
        var text = new StringBuilder();
        var walker = _automation.TreeWalkerFactory.GetControlViewWalker();
        // Each control's facts come with it as the walk goes, rather than a call for each: a dump of a big window would
        // otherwise make tens of thousands of calls into the app, and the stuck card would wait for them.
        var properties = _automation.PropertyLibrary.Element;
        using var cached = Facts(properties.ControlType, properties.Name, properties.AutomationId, properties.HelpText, properties.ClassName,
            properties.BoundingRectangle).Activate();
        foreach (var hwnd in AppWindows())
        {
            text.Append(CultureInfo.InvariantCulture, $"===== {Native.Title(hwnd)} ({Native.ClassName(hwnd)})\r\n");
            var count = 0;
            void Walk(AutomationElement element, int depth)
            {
                if (++count > 5000 || depth > 60)
                    return;
                string line;
                try
                {
                    var box = element.BoundingRectangle;
                    var properties = element.Properties;
                    line = $"{properties.ControlType.ValueOrDefault} \"{properties.Name.ValueOrDefault}\""
                        + (properties.AutomationId.ValueOrDefault is { Length: > 0 } id ? $" id={id}" : "")
                        + (properties.HelpText.ValueOrDefault is { Length: > 0 } help ? $" help=\"{help}\"" : "")
                        + (properties.ClassName.ValueOrDefault is { Length: > 0 } className ? $" class={className}" : "")
                        + $" ({box.Left},{box.Top},{box.Right},{box.Bottom})";
                }
                catch (Exception ex) when (Gone(ex))
                {
                    line = $"(couldn't be read: {ex.Message})";
                }
                text.Append(' ', depth * 2).Append(line).Append("\r\n");
                for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                    Walk(child, depth + 1);
            }
            try
            {
                Walk(_automation.FromHandle(hwnd), 0);
            }
            catch (Exception ex) when (Gone(ex))
            {
                text.Append(CultureInfo.InvariantCulture, $"(the window couldn't be read: {ex.Message})\r\n");
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// Whether the keyboard is in a text box that can be typed in, in any app, so the panel's keys stay out of it. It is
    /// asked on a timer's thread, where an error would end autotest: a busy app is taken as not typing.
    /// </summary>
    public bool IsTyping()
    {
        try
        {
            var focused = _automation.FocusedElement();
            var type = focused?.Properties.ControlType.ValueOrDefault;
            return (type == ControlType.Edit || type == ControlType.Document)
                && !(focused!.Patterns.Value.IsSupported && focused.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault);
        }
        catch (Exception ex) when (Gone(ex))
        {
            return false;
        }
    }
}
