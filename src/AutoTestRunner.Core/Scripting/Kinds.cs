using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>Finds which check or tool an object is.</summary>
public static class Kinds
{
    /// <summary>
    /// The one key of item that names a kind. Some kinds' properties are named like other kinds, such as jsonValue's
    /// equals, so a key only names the kind when every other kind-like key is one of its properties. Null when there
    /// is none, or more than one.
    /// </summary>
    public static string? Of(JsonObject item, IReadOnlyDictionary<string, string[]> kinds)
    {
        var found = item.Select(pair => pair.Key).Where(kinds.ContainsKey).ToList();
        var named = found.Where(kind => found.All(other => other == kind || kinds[kind].Contains(other))).ToList();
        return named.Count == 1 ? named[0] : null;
    }
}
