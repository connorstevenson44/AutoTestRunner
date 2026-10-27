using System.Globalization;
using System.Text;

namespace AutoTestRunner.Running;

/// <summary>
/// What SQL gave back: its columns and rows, each value as text (null for a database null), or how many rows a
/// statement changed when it reads none.
/// </summary>
public sealed record QueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, int Changed)
{
    /// <summary>How a database null reads in a kept value and in a check.</summary>
    public const string Null = "<null>";

    /// <summary>The first column of the first row, the value a step keeps; Checks.Absent when there are no rows.</summary>
    public string First => Rows is [[var value, ..], ..] ? value ?? Null : Checks.Absent;

    /// <summary>The rows read, or else the rows changed.</summary>
    public int Count => Columns.Count > 0 ? Rows.Count : Changed;

    /// <summary>The rows as a person reads them, a line each with tabs between values, the columns first; up to most rows.</summary>
    public string Table(int most = 50)
    {
        if (Columns.Count == 0)
            return $"{Changed} row{(Changed == 1 ? "" : "s")} changed";
        var text = new StringBuilder(string.Join('\t', Columns));
        foreach (var row in Rows.Take(most))
            text.Append('\n').Append(string.Join('\t', row.Select(value => value ?? Null)));
        if (Rows.Count > most)
            text.Append(CultureInfo.InvariantCulture, $"\n… and {Rows.Count - most} more");
        return text.ToString();
    }
}
