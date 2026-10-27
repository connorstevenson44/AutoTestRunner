namespace AutoTestRunner.Scripting;

/// <summary>
/// A database the suite reads and changes, from databases in suite.json: its kind, which picks the driver, and its
/// connection string, which can have ${variables}.
/// </summary>
public sealed record DatabaseSettings(string Name, string Kind, string Connection)
{
    /// <summary>The kinds of database, by the word suite.json uses, and what a person calls each.</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sqlServer"] = "SQL Server",
        ["postgres"] = "PostgreSQL",
        ["oracle"] = "Oracle",
        ["mySql"] = "MySQL",
        ["sqlite"] = "SQLite",
    };
}
