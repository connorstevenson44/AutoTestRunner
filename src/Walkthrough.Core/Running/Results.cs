using System.Text;
using System.Xml.Linq;

namespace Walkthrough.Running;

public enum CaseStatus { Passed, Failed, Skipped, Stopped }

/// <summary>One case's result: what failed and what the person noted.</summary>
public sealed class CaseResult(int number, string name)
{
    public int Number { get; } = number;
    public string Name { get; } = name;
    public CaseStatus Status { get; set; } = CaseStatus.Passed;
    public List<string> Failures { get; } = [];
    public List<string> Notes { get; } = [];
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// A run's results folder: run.log as it goes, then summary.txt and junit.xml, with the screenshots and UI Automation
/// dumps of each stuck point. The layout is the AutoHotkey runner's, so CI uploads the same folder.
/// </summary>
public sealed class Results
{
    private readonly Lock _lock = new();
    private readonly TextWriter? _console;
    private int _captures;

    public Results(string folder, TextWriter? console)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
        LogPath = Path.Combine(folder, "run.log");
        _console = console;
    }

    public string Folder { get; }
    public string LogPath { get; }
    public List<CaseResult> Cases { get; } = [];

    /// <summary>A folder named for now, inside parent, as "2026-10-25 141502".</summary>
    public static string NewFolder(string parent) => Path.Combine(Path.GetFullPath(parent), DateTime.Now.ToString("yyyy-MM-dd HHmmss"));

    public void Log(string kind, string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {kind,-6}  {text}";
        lock (_lock)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // The log is a convenience; a locked file doesn't stop the run.
            }
            _console?.WriteLine(line);
        }
    }

    /// <summary>A path for the next capture, numbered in order: "03-stuck".</summary>
    public string NextCapture(string name) => Path.Combine(Folder, $"{Interlocked.Increment(ref _captures):00}-{name}");

    /// <summary>Writes summary.txt and junit.xml, and returns the summary.</summary>
    public string Finish(string suiteName)
    {
        var text = new StringBuilder($"{suiteName}, {DateTime.Now:yyyy-MM-dd HH:mm}\r\n\r\n");
        foreach (var result in Cases)
        {
            text.Append($"{result.Status.ToString().ToUpperInvariant(),-8}{result.Number}. {result.Name}\r\n");
            foreach (var failure in result.Failures)
                text.Append($"          ✗ {failure}\r\n");
            foreach (var note in result.Notes)
                text.Append($"          note: {note}\r\n");
        }
        int Count(CaseStatus status) => Cases.Count(result => result.Status == status);
        text.Append($"\r\n{Cases.Count} cases: {Count(CaseStatus.Passed)} passed, {Count(CaseStatus.Failed)} failed, "
            + $"{Count(CaseStatus.Skipped)} skipped, {Count(CaseStatus.Stopped)} stopped\r\n");
        var summary = text.ToString();
        File.WriteAllText(Path.Combine(Folder, "summary.txt"), summary, Encoding.UTF8);
        JUnit(suiteName).Save(Path.Combine(Folder, "junit.xml"));
        return summary;
    }

    private XDocument JUnit(string suiteName)
    {
        var suite = new XElement("testsuite",
            new XAttribute("name", suiteName),
            new XAttribute("tests", Cases.Count),
            new XAttribute("failures", Cases.Count(result => result.Status is CaseStatus.Failed or CaseStatus.Stopped)),
            new XAttribute("skipped", Cases.Count(result => result.Status == CaseStatus.Skipped)),
            new XAttribute("time", Cases.Sum(result => result.Duration.TotalSeconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        foreach (var result in Cases)
        {
            var testCase = new XElement("testcase",
                new XAttribute("classname", suiteName),
                new XAttribute("name", $"{result.Number}. {result.Name}"),
                new XAttribute("time", result.Duration.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            if (result.Status == CaseStatus.Skipped)
                testCase.Add(new XElement("skipped", string.Join("\n", result.Failures)));
            else if (result.Status != CaseStatus.Passed)
                testCase.Add(new XElement("failure",
                    new XAttribute("message", result.Failures.FirstOrDefault() ?? result.Status.ToString()),
                    string.Join("\n", result.Failures)));
            if (result.Notes.Count > 0)
                testCase.Add(new XElement("system-out", string.Join("\n", result.Notes.Select(note => "note: " + note))));
            suite.Add(testCase);
        }
        return new XDocument(new XElement("testsuites", suite));
    }
}
