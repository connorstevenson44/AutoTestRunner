namespace AutoTestRunner.Scripting;

/// <summary>A suite found near the runner: loaded, or the problems that stopped it loading.</summary>
public sealed record FoundSuite(string Folder, string Name, Suite? Suite, IReadOnlyList<string> Problems)
{
    /// <summary>Loads the suite in a folder, keeping its problems rather than throwing them.</summary>
    public static FoundSuite Load(string folder)
    {
        try
        {
            var suite = Suite.Load(folder);
            return new FoundSuite(suite.Folder, suite.Name, suite, []);
        }
        catch (SuiteException ex)
        {
            return new FoundSuite(Path.GetFullPath(folder), Path.GetFileName(Path.GetFullPath(folder).TrimEnd('\\', '/')), null, ex.Problems);
        }
    }
}
