using System.Text;
using System.Text.RegularExpressions;

namespace AutoTestRunner.Scripting;

/// <summary>
/// The variables a step can use. A macro call gets a scope of its own over the case's, so its parameters don't leak,
/// while what the case kept stays readable. Expressions are deliberately tiny: <c>${name}</c>, <c>${name.part}</c>,
/// <c>${env:NAME}</c> and <c>${helper(name)}</c>. Anything more is a C# step, not a script.
/// </summary>
public sealed partial class VariableScope(VariableScope? parent = null, IReadOnlyDictionary<string, Helper>? helpers = null)
{
    private readonly VariableScope? _parent = parent;
    private readonly Dictionary<string, Value> _values = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, Helper> _helpers = helpers ?? parent?._helpers ?? new Dictionary<string, Helper>();

    public VariableScope Child() => new(this, _helpers);

    public void Set(string name, Value value) => _values[name] = value;

    public void Set(string name, string text) => _values[name] = Value.Of(text);

    /// <summary>Sets a variable in the scope that already has it, so a macro can update what the case kept.</summary>
    public void SetOuter(string name, Value value)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
            if (scope._values.ContainsKey(name))
            {
                scope._values[name] = value;
                return;
            }
        _values[name] = value;
    }

    public bool TryGet(string name, out Value value)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
            if (scope._values.TryGetValue(name, out value!))
                return true;
        value = null!;
        return false;
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
            if (!_helpers.TryGetValue(name, out var helper))
                throw new ScriptException($"There is no helper called {name}. The suite's helpers are: {(_helpers.Count == 0 ? "none" : string.Join(", ", _helpers.Keys))}.");
            var argument = call.Groups["arg"].Value.Trim();
            var input = argument.StartsWith('\'') && argument.EndsWith('\'') && argument.Length >= 2 ? argument[1..^1] : Evaluate(argument);
            var match = helper.Pattern.Match(input);
            return match.Success ? (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value) : "";
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

    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<arg>[^()]*)\)$")]
    private static partial Regex HelperCall();
}
