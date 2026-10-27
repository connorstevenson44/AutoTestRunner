using System.Globalization;

namespace AutoTestRunner.Driving;

/// <summary>
/// Keys as a key step writes them, such as Enter or Ctrl+S, in one spelling, so what the person pressed in a manual run
/// can be compared with what the step says: Control and Ctrl, Return and Enter, the order of Shift and Ctrl and the case
/// of a letter all come out the same.
/// </summary>
public static class KeyNames
{
    private static readonly string[] Modifiers = ["CTRL", "ALT", "SHIFT", "WIN"];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CONTROL"] = "CTRL", ["MENU"] = "ALT", ["RETURN"] = "ENTER", ["ESCAPE"] = "ESC", ["DEL"] = "DELETE", ["INS"] = "INSERT",
        ["PGUP"] = "PAGEUP", ["PRIOR"] = "PAGEUP", ["PGDN"] = "PAGEDOWN", ["NEXT"] = "PAGEDOWN", ["BACK"] = "BACKSPACE",
        ["WINDOWS"] = "WIN", ["LWIN"] = "WIN", ["RWIN"] = "WIN",
    };

    /// <summary>A key and the keys held with it in one spelling: Ctrl, Alt, Shift and Win in that order, then the key.</summary>
    public static string Canonical(string keys)
    {
        var parts = keys.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Aliases.TryGetValue(part, out var alias) ? alias : part.ToUpperInvariant())
            .Select(part => part.StartsWith("KEY_", StringComparison.Ordinal) ? part[4..] : part)
            .Distinct()
            .ToList();
        var held = Modifiers.Where(parts.Contains);
        var rest = parts.Where(part => !Modifiers.Contains(part));
        return string.Join("+", held.Concat(rest));
    }

    public static bool Same(string one, string other) => Canonical(one) == Canonical(other);

    /// <summary>A Windows virtual key, with the keys held with it, in the one spelling; null for a key with no name here.</summary>
    public static string? Of(int virtualKey, bool ctrl, bool alt, bool shift, bool win)
    {
        if (Name(virtualKey) is not { } name)
            return null;
        var held = new[] { (ctrl, "CTRL"), (alt, "ALT"), (shift, "SHIFT"), (win, "WIN") }.Where(modifier => modifier.Item1).Select(modifier => modifier.Item2);
        return Canonical(string.Join("+", held.Append(name)));
    }

    /// <summary>
    /// A Windows virtual key, with the keys held with it, as a key step is written by hand: Ctrl+S, Shift+PageDown, F5.
    /// Null for a key with no name here, or one that is only ever held with another, such as Ctrl on its own.
    /// </summary>
    public static string? Written(int virtualKey, bool ctrl, bool alt, bool shift, bool win)
    {
        if (Name(virtualKey) is not { } name || Modifiers.Contains(name))
            return null;
        var held = new[] { (ctrl, "Ctrl"), (alt, "Alt"), (shift, "Shift"), (win, "Win") }.Where(modifier => modifier.Item1).Select(modifier => modifier.Item2);
        var spelt = name switch
        {
            "PAGEUP" => "PageUp",
            "PAGEDOWN" => "PageDown",
            { Length: 1 } => name,
            _ => name[0] + name[1..].ToLowerInvariant(),
        };
        return string.Join("+", held.Append(spelt));
    }

    private static string? Name(int key) => key switch
    {
        0x08 => "BACKSPACE",
        0x09 => "TAB",
        0x0D => "ENTER",
        0x10 or 0xA0 or 0xA1 => "SHIFT",
        0x11 or 0xA2 or 0xA3 => "CTRL",
        0x12 or 0xA4 or 0xA5 => "ALT",
        0x13 => "PAUSE",
        0x1B => "ESC",
        0x20 => "SPACE",
        0x21 => "PAGEUP",
        0x22 => "PAGEDOWN",
        0x23 => "END",
        0x24 => "HOME",
        0x25 => "LEFT",
        0x26 => "UP",
        0x27 => "RIGHT",
        0x28 => "DOWN",
        0x2C => "SNAPSHOT",
        0x2D => "INSERT",
        0x2E => "DELETE",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        0x5B or 0x5C => "WIN",
        0x5D => "APPS",
        >= 0x60 and <= 0x69 => "NUMPAD" + (key - 0x60).ToString(CultureInfo.InvariantCulture),
        0x6A => "MULTIPLY",
        0x6B => "ADD",
        0x6D => "SUBTRACT",
        0x6E => "DECIMAL",
        0x6F => "DIVIDE",
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture),
        _ => null,
    };
}
