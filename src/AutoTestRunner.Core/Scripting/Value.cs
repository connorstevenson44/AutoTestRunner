namespace AutoTestRunner.Scripting;

/// <summary>
/// A variable's value: its text, which <c>${name}</c> gives, and named parts, which <c>${name.part}</c> gives. A
/// control kept with <c>as</c> has its name as its text, and parts such as value and helpText.
/// </summary>
public sealed record Value(string Text, IReadOnlyDictionary<string, string> Parts)
{
    public static Value Of(string text) => new(text, new Dictionary<string, string>());

    public static Value Of(string text, IReadOnlyDictionary<string, string> parts) => new(text, parts);

    public string Part(string name) =>
        Parts.TryGetValue(name, out var part)
            ? part
            : throw new ScriptException($"It has no part called {name}. It has: {(Parts.Count == 0 ? "none" : string.Join(", ", Parts.Keys))}.");

    public override string ToString() => Text;
}
