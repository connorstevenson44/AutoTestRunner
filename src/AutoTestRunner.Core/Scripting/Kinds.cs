using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>Finds which step, check or tool an object is.</summary>
public static class Kinds
{
    /// <summary>The one key of item that names a kind, or null when there is none, or more than one.</summary>
    public static string? Of(JsonObject item, IReadOnlyDictionary<string, string[]> kinds) =>
        Named(item, kinds.ContainsKey, kind => kinds[kind]) is [var kind] ? kind : null;

    /// <summary>
    /// The keys of item that could name its kind. Some kinds' properties are named like other kinds, such as jsonValue's
    /// equals, so a key only names the kind when every other kind-like key is one of its properties. One key when it
    /// names one, otherwise every kind-like key.
    /// </summary>
    public static IReadOnlyList<string> Named(JsonObject item, Func<string, bool> isKind, Func<string, string[]> propertiesOf)
    {
        var found = item.Select(pair => pair.Key).Where(isKind).ToList();
        var named = found.Where(kind => found.All(other => other == kind || propertiesOf(kind).Contains(other))).ToList();
        return named.Count == 1 ? named : found;
    }
}
