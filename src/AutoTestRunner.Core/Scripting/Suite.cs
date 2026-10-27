using System.Globalization;
using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>
/// A suite: one app's tests, in a folder. suite.json says how to start the app, its variables and how to reset the
/// machine; elements.json names its controls; macros/ holds reusable steps and cases/ the cases, run in file order.
/// </summary>
public sealed partial class Suite
{
    public required string Folder { get; init; }
    public required string Name { get; init; }
    public required AppSettings App { get; init; }
    public required ResetSettings Reset { get; init; }
    public required JsonObject Variables { get; init; }
    public required IReadOnlyList<string> Requires { get; init; }
    public required IReadOnlyDictionary<string, Helper> Helpers { get; init; }
    public required IReadOnlyDictionary<string, JsonNode> Elements { get; init; }
    public required IReadOnlyDictionary<string, Macro> Macros { get; init; }
    public required IReadOnlyList<Case> Cases { get; init; }
    public (int Width, int Height)? Screen { get; init; }

    /// <summary>The databases steps and checks read and change, by their name in suite.json.</summary>
    public IReadOnlyDictionary<string, DatabaseSettings> Databases { get; init; } = new Dictionary<string, DatabaseSettings>();

    /// <summary>The message queues steps and checks send to and read, by their name in suite.json.</summary>
    public IReadOnlyDictionary<string, QueueSettings> Queues { get; init; } = new Dictionary<string, QueueSettings>();

    /// <summary>The software the suite needs on the computer, by its name in suite.json, which the launcher offers to install.</summary>
    public IReadOnlyDictionary<string, SoftwareSettings> Software { get; init; } = new Dictionary<string, SoftwareSettings>();

    /// <summary>The controls the builder watches while recording, from builder.watch in suite.json.</summary>
    public IReadOnlyList<WatchHook> Watch { get; init; } = [];

    /// <summary>The steps the builder adds after a control is clicked, from builder.afterClick in suite.json.</summary>
    public IReadOnlyList<ClickHook> AfterClick { get; init; } = [];

    /// <summary>
    /// Finds an element another way from now on, as when a run stuck on it has it picked again; elements.json is
    /// written separately.
    /// </summary>
    public void Redefine(string element, JsonNode target)
    {
        if (Elements is not IDictionary<string, JsonNode> elements || !elements.ContainsKey(element))
            throw new ArgumentException($"There is no element called {element} to change.", nameof(element));
        elements[element] = target.DeepClone();
    }

    /// <summary>The case with this id, such as "open-package", or this number.</summary>
    public Case? Find(string idOrNumber) =>
        int.TryParse(idOrNumber, out var number)
            ? Cases.FirstOrDefault(item => item.Number == number)
            : Cases.FirstOrDefault(item => item.Id == idOrNumber);

    /// <summary>Picks cases by number or id, such as "1-5,8" or "open-package". Empty picks them all.</summary>
    public IReadOnlyList<Case> Select(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return Cases;
        var picked = new List<Case>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', 2);
            if (range.Length == 2 && int.TryParse(range[0], out var from) && int.TryParse(range[1], out var to))
            {
                for (var number = from; number <= to; number++)
                    picked.Add(Find(number.ToString(CultureInfo.InvariantCulture)) ?? throw new SuiteException([$"There is no case {number}."]));
            }
            else
                picked.Add(Find(part) ?? throw new SuiteException([$"There is no case {part}."]));
        }
        return picked;
    }

    /// <summary>The cases with any of these tags, in order; all of them when there are none.</summary>
    public static IReadOnlyList<Case> Tagged(IReadOnlyList<Case> cases, IReadOnlyCollection<string> tags) =>
        tags.Count == 0 ? cases : [.. cases.Where(item => item.Tags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))];

    /// <summary>Every tag the cases have, in the order they first appear.</summary>
    public IReadOnlyList<string> Tags => [.. Cases.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>A case and the cases it carries on from, in the order they run.</summary>
    public IReadOnlyList<Case> Leading(Case last)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new Stack<Case>([last]);
        while (waiting.TryPop(out var item))
        {
            if (!ids.Add(item.Id))
                continue;
            foreach (var need in item.Needs.Append(item.AfterReset).OfType<string>())
                if (Find(need) is { } needed)
                    waiting.Push(needed);
        }
        return [.. Cases.Where(item => ids.Contains(item.Id))];
    }

    /// <summary>The cases that carry on from a case, directly or through others, in order: a change to it changes where they start.</summary>
    public IReadOnlyList<Case> CarryingOn(Case first)
    {
        var after = new HashSet<string>(StringComparer.Ordinal) { first.Id };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var item in Cases)
                if (!after.Contains(item.Id) && (item.Needs.Any(after.Contains) || item.AfterReset is { } setup && after.Contains(setup)))
                    grew = after.Add(item.Id) || grew;
        }
        return [.. Cases.Where(item => item != first && after.Contains(item.Id))];
    }

    /// <summary>
    /// The controls the builder watches: those in builder.watch, those with a macro named waitFor and their name, such
    /// as waitForSavedText for savedText, and the app's status text, waited for with waitForStatusBar when there is one.
    /// </summary>
    public IReadOnlyList<WatchHook> Watched()
    {
        var watched = Watch.ToList();
        bool Has(string element) => watched.Any(hook => hook.Element == element);
        foreach (var element in Elements.Keys)
            if (!Has(element) && Macros.ContainsKey(WaitMacro(element)))
                watched.Add(new WatchHook(element, WaitMacro(element)));
        if (App.Status is { } status && !Has(status))
            watched.Add(new WatchHook(status, Macros.ContainsKey("waitForStatusBar") ? "waitForStatusBar" : null));
        return watched;
    }

    /// <summary>The macro that waits for an element by its name alone, such as waitForSavedText for savedText.</summary>
    public static string WaitMacro(string element) => "waitFor" + char.ToUpperInvariant(element[0]) + element[1..];
}
