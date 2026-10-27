namespace AutoTestRunner.Core.Tests;

/// <summary>CLAUDE.md is read at the start of every session, so it stays short enough to cost little to read.</summary>
public class CodebaseNotesTests
{
    private const int MaxLines = 20;

    [Fact]
    public void Claude_md_stays_under_the_line_cap()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "CLAUDE.md"));
        Assert.True(lines.Length <= MaxLines, $"CLAUDE.md has {lines.Length} lines; keep it to {MaxLines} or fewer.");
    }
}
