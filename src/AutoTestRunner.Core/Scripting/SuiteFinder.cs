namespace AutoTestRunner.Scripting;

/// <summary>
/// Finds the suites near the runner, so one launcher can offer them all. A ready-to-run kit puts the runner in a
/// folder of its own beside one folder per suite; a clone keeps suites a few folders down, such as samples/demo or
/// tests/ui-tests/netdeploy. A suite is a folder with a suite.json, and suites don't hold other suites.
/// </summary>
public static class SuiteFinder
{
    private const int MostFolders = 5000;

    // Build output, results and tools' folders never hold a suite, and can be big.
    private static readonly HashSet<string> Skipped = new(["bin", "obj", "results", "node_modules", "packages", "artifacts"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The suite folders in these places and up to depth folders below them, by path.</summary>
    public static IReadOnlyList<string> Find(IEnumerable<string> places, int depth = 3)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var looked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var budget = MostFolders;
        foreach (var place in places)
        {
            if (string.IsNullOrEmpty(place) || !Directory.Exists(place))
                continue;
            Look(Path.GetFullPath(place).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), depth, found, looked, ref budget);
        }
        return [.. found];
    }

    /// <summary>
    /// Where to look: the runner's own folder, the folder it is in, and the folder it was started from. A suite asked
    /// for by name adds the folder it is in, for the suites beside it.
    /// </summary>
    public static IReadOnlyList<string> Places(string runnerFolder, string currentFolder, string? suiteFolder = null)
    {
        var places = new List<string>();
        if (suiteFolder is not null && Path.GetDirectoryName(Path.GetFullPath(suiteFolder).TrimEnd('\\', '/')) is { } beside)
            places.Add(beside);
        places.Add(runnerFolder);
        if (Path.GetDirectoryName(Path.GetFullPath(runnerFolder).TrimEnd('\\', '/')) is { } parent)
            places.Add(parent);
        places.Add(currentFolder);
        return places;
    }

    public static bool IsSuite(string folder) => File.Exists(Path.Combine(folder, "suite.json"));

    private static void Look(string folder, int depth, SortedSet<string> found, HashSet<string> looked, ref int budget)
    {
        if (budget-- <= 0 || !looked.Add(folder))
            return;
        if (IsSuite(folder))
        {
            found.Add(folder);
            return;
        }
        if (depth == 0)
            return;
        string[] children;
        try
        {
            children = Directory.GetDirectories(folder);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }
        foreach (var child in children.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || Skipped.Contains(name))
                continue;
            Look(child, depth - 1, found, looked, ref budget);
        }
    }
}
