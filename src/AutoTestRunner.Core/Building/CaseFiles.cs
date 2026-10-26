using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Changes to a suite's case files from the launcher: a copy of a case, a new name, a new place in the order, and a
/// case taken out. A case's id is the part of its file name after the number, so moving a case renumbers files and
/// keeps every needs as it was.
/// </summary>
public static partial class CaseFiles
{
    /// <summary>A copy of a case, after the last case, named as the case with "(copy)" after it. Returns the new file.</summary>
    public static string Duplicate(Suite suite, Case item)
    {
        var name = $"{item.Name} (copy)";
        var path = Path.Combine(Path.GetDirectoryName(item.File)!, CaseWriter.FileName(suite, name));
        File.WriteAllText(path, Named(CaseWriter.Unflag(File.ReadAllText(item.File)), name));
        return path;
    }

    /// <summary>Gives a case a new name, in its file; its id, and so what needs it, stays.</summary>
    public static void Rename(Case item, string name)
    {
        if (name.Trim().Length == 0)
            throw new ArgumentException("A case needs a name.", nameof(name));
        File.WriteAllText(item.File, Named(File.ReadAllText(item.File), name.Trim()));
    }

    /// <summary>
    /// Why a case can't move one place up (by -1) or down (by 1), or null when it can: a case runs after the cases it
    /// needs, so it can't go above one of them, nor below a case that needs it.
    /// </summary>
    public static string? CantMove(Suite suite, Case item, int by)
    {
        var index = Index(suite, item);
        var to = index + by;
        if (to < 0 || to >= suite.Cases.Count)
            return by < 0 ? "It's the first case." : "It's the last case.";
        var other = suite.Cases[to];
        return by < 0 && item.Needs.Contains(other.Id) ? $"It carries on from {other.Number}. {other.Name}, so it runs after it."
            : by > 0 && other.Needs.Contains(item.Id) ? $"{other.Number}. {other.Name} carries on from it, so it runs before it."
            : null;
    }

    /// <summary>Moves a case one place up or down, swapping its file's number with the case there.</summary>
    public static void Move(Suite suite, Case item, int by)
    {
        if (CantMove(suite, item, by) is { } why)
            throw new InvalidOperationException(why);
        var other = suite.Cases[Index(suite, item) + by];
        var (file, otherFile) = (item.File, other.File);
        if (Number(file) is null || Number(otherFile) is null)
            throw new InvalidOperationException("Only cases whose file names start with their number can be moved.");
        // Two cases can share a number, ordered by id: then every case is given a number of its own first.
        if (Number(file) == Number(otherFile))
        {
            var renumbered = Renumber(suite);
            (file, otherFile) = (renumbered.GetValueOrDefault(file, file), renumbered.GetValueOrDefault(otherFile, otherFile));
        }
        var (mine, theirs) = (Number(file)!, Number(otherFile)!);
        File.Move(file, Renamed(file, theirs));
        File.Move(otherFile, Renamed(otherFile, mine));
    }

    /// <summary>The cases that carry on from a case, which taking it out leaves to start somewhere else.</summary>
    public static IReadOnlyList<Case> Dependents(Suite suite, Case item) => [.. suite.Cases.Where(other => other.Needs.Contains(item.Id))];

    /// <summary>
    /// Deletes a case. The cases that carried on from it carry on from what it carried on from instead, and are
    /// marked as needing a look, as they may not start where they should.
    /// </summary>
    public static void Delete(Suite suite, Case item)
    {
        foreach (var other in Dependents(suite, item))
        {
            var needs = other.Needs.Where(id => id != item.Id).Concat(item.Needs).Distinct(StringComparer.Ordinal).ToList();
            var text = NeedsList().Replace(File.ReadAllText(other.File), match =>
                $"{match.Groups[1].Value}{JsonLine.Write(new JsonArray([.. needs.Select(id => (JsonNode?)JsonValue.Create(id))]))}", 1);
            File.WriteAllText(other.File, CaseWriter.Flag(text, $"Case {item.Number}, {item.Name}, which it carried on from, was deleted: check this case still starts where it should."));
        }
        File.Delete(item.File);
    }

    // Numbers every case file in order from 1, as wide as the widest number, so each has its own; returns where each moved.
    private static Dictionary<string, string> Renumber(Suite suite)
    {
        var width = Math.Max(2, suite.Cases.Count.ToString(CultureInfo.InvariantCulture).Length);
        var moves = suite.Cases.Select((each, index) => (From: each.File, To: Renamed(each.File, (index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'))))
            .Where(move => move.From != move.To).ToList();
        // Through names of their own first, so that no file is moved onto one not moved yet.
        foreach (var (from, _) in moves)
            File.Move(from, from + ".moving");
        foreach (var (from, to) in moves)
            File.Move(from + ".moving", to);
        return moves.ToDictionary(move => move.From, move => move.To);
    }

    private static int Index(Suite suite, Case item) =>
        suite.Cases.ToList().FindIndex(each => each.Id == item.Id) is >= 0 and var index ? index : throw new ArgumentException($"{item.Name} isn't in {suite.Name}.", nameof(item));

    private static string? Number(string file) => CaseWriter.Leading().Match(Path.GetFileName(file)) is { Success: true } match ? match.Value : null;

    private static string Renamed(string file, string number)
    {
        var name = Path.GetFileName(file);
        return Path.Combine(Path.GetDirectoryName(file)!, number + name[CaseWriter.Leading().Match(name).Length..]);
    }

    // A case's text with a new name in its name line.
    private static string Named(string text, string name)
    {
        var line = CaseWriter.NameLine().Match(text);
        if (!line.Success)
            throw new InvalidOperationException("The case file has no name line to change.");
        var comma = line.Value.TrimEnd().EndsWith(',') ? "," : "";
        return text[..line.Index] + $"{line.Groups[1].Value}\"name\": {JsonLine.Quote(name)}{comma}" + text[(line.Index + line.Length)..];
    }

    // A case's needs, with what comes before the list.
    [GeneratedRegex(@"(""needs""\s*:\s*)\[[^\]]*\]")]
    private static partial Regex NeedsList();
}
