using System.Text;
using System.Text.RegularExpressions;

namespace AutoTestRunner.Scripting;

/// <summary>
/// The variables a step can use. A macro call gets a scope of its own over the case's, so its parameters don't leak,
/// while what the case kept stays readable. Expressions are deliberately tiny: <c>${name}</c>, <c>${name.part}</c>,
/// <c>${env:NAME}</c>, <c>${helper(name)}</c> and <c>${readFile(path)}</c>, which calls can wrap, as in
/// <c>${connection(readFile(appConfig))}</c>. Anything more is a C# step, not a script.
/// </summary>
public sealed partial class VariableScope(VariableScope? parent = null, IReadOnlyDictionary<string, Helper>? helpers = null)
{
    /// <summary>The call that gives a file's text, built in rather than one of the suite's helpers.</summary>
    public const string ReadFile = "readFile";

    private readonly VariableScope? _parent = parent;
    private readonly Dictionary<string, Value> _values = new(StringComparer.Ordinal);
    // Variables worked out when they are first used, by SetLater.
    private readonly Dictionary<string, Func<Value>> _later = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, Helper> _helpers = helpers ?? parent?._helpers ?? new Dictionary<string, Helper>();

    public VariableScope Child() => new(this, _helpers);

    public void Set(string name, Value value)
    {
        _later.Remove(name);
        _values[name] = value;
    }

    public void Set(string name, string text) => Set(name, Value.Of(text));

    /// <summary>
    /// Sets a variable that is worked out when it is first used, such as one read from a file the app writes once it
    /// is built, so a suite still opens before it can be. Until it can be, using it says why it can't.
    /// </summary>
    public void SetLater(string name, Func<Value> make)
    {
        _values.Remove(name);
        _later[name] = make;
    }

    /// <summary>Sets a variable in the scope that already has it, so a macro can update what the case kept.</summary>
    public void SetOuter(string name, Value value)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
            if (scope._values.ContainsKey(name) || scope._later.ContainsKey(name))
            {
                scope.Set(name, value);
                return;
            }
        _values[name] = value;
    }

    public bool TryGet(string name, out Value value)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._values.TryGetValue(name, out value!))
                return true;
            if (scope._later.TryGetValue(name, out var make))
            {
                try
                {
                    value = make();
                }
                catch (ScriptException ex)
                {
                    throw new ScriptException($"{name}: {ex.Message}");
                }
                scope.Set(name, value);
                return true;
            }
        }
        value = null!;
        return false;
    }

    /// <summary>
    /// Every variable it can see, each with its text, the nearest scope's where two have the same name. One that can't
    /// be worked out yet has why as its text.
    /// </summary>
    public IReadOnlyList<(string Name, string Text)> All()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            foreach (var name in scope._later.Keys.ToList())
            {
                if (seen.ContainsKey(name))
                    continue;
                try
                {
                    seen[name] = scope.Get(name).Text;
                }
                catch (ScriptException ex)
                {
                    seen[name] = ex.Message;
                }
            }
            foreach (var (name, value) in scope._values)
                seen.TryAdd(name, value.Text);
        }
        return [.. seen.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => (pair.Key, pair.Value))];
    }

    public Value Get(string name) =>
        TryGet(name, out var value) ? value : throw new ScriptException($"There is no variable called {name}.");

    /// <summary>Replaces each ${...} in text with what it names. "$${" writes a literal "${".</summary>
    public string Expand(string text)
    {
        if (!text.Contains('$'))
            return text;
        var result = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf("${", index, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(text, index, text.Length - index);
                break;
            }
            if (start > 0 && text[start - 1] == '$')
            {
                result.Append(text, index, start - 1 - index).Append("${");
                index = start + 2;
                continue;
            }
            var end = text.IndexOf('}', start);
            if (end < 0)
                throw new ScriptException($"\"{text}\" has a ${{ with no }} after it.");
            result.Append(text, index, start - index).Append(Evaluate(text[(start + 2)..end].Trim()));
            index = end + 1;
        }
        return result.ToString();
    }

    /// <summary>The value an expression names, such as "files" or "files.name", keeping its parts.</summary>
    public Value Resolve(string expression)
    {
        expression = expression.Trim();
        if (expression.StartsWith("${", StringComparison.Ordinal) && expression.EndsWith('}') && expression.IndexOf("${", 2, StringComparison.Ordinal) < 0)
            expression = expression[2..^1].Trim();
        var dot = expression.IndexOf('.');
        if (dot < 0 && !expression.Contains('(') && !expression.Contains(':'))
            return Get(expression);
        return Value.Of(Evaluate(expression));
    }

    private string Evaluate(string expression)
    {
        if (expression.StartsWith("env:", StringComparison.Ordinal))
            return Environment.GetEnvironmentVariable(expression[4..]) ?? "";

        var call = HelperCall().Match(expression);
        if (call.Success)
        {
            var name = call.Groups["name"].Value;
            var reads = string.Equals(name, ReadFile, StringComparison.OrdinalIgnoreCase);
            Helper? helper = null;
            if (!reads && !_helpers.TryGetValue(name, out helper))
                throw new ScriptException($"There is no helper called {name}. The suite's helpers are: {(_helpers.Count == 0 ? "none" : string.Join(", ", _helpers.Keys))}.");
            var argument = call.Groups["arg"].Value.Trim();
            var input = argument.StartsWith('\'') && argument.EndsWith('\'') && argument.Length >= 2 ? argument[1..^1] : Evaluate(argument);
            return helper is null ? Read(input) : helper.Apply(input);
        }

        var dot = expression.IndexOf('.');
        if (dot < 0)
            return Get(expression).Text;
        var value = Get(expression[..dot]);
        try
        {
            return value.Part(expression[(dot + 1)..]);
        }
        catch (ScriptException ex)
        {
            throw new ScriptException($"{expression[..dot]}: {ex.Message}");
        }
    }

    // A file's text, relative to the suite folder, for a helper to read a setting out of, such as an app's .config file.
    private string Read(string path)
    {
        var full = Path.IsPathRooted(path) || !TryGet("suiteDir", out var suite) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(suite.Text, path));
        try
        {
            return File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ScriptException($"{ReadFile} couldn't read {full}: {ex.Message}");
        }
    }

    // The argument can be another call, as in configValue(readFile(appConfig)).
    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<arg>.*)\)$", RegexOptions.Singleline)]
    private static partial Regex HelperCall();
}
