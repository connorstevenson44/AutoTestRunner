using System.Text.Json.Nodes;

namespace AutoTestRunner.Building;

/// <summary>
/// Starts a new suite from the launcher: suite.json naming the app and its window, elements.json with the status
/// bar, the waitForStatusBar macro that waits for it, and an empty cases folder for the builder to record into.
/// </summary>
public static class SuiteMaker
{
    private const string Schema = "https://raw.githubusercontent.com/connorstevenson44/AutoTestRunner/main/schema/";

    /// <summary>
    /// Writes the suite's files in a folder that has no suite.json yet, and returns the folder. status is how to find
    /// the status bar, or null when the app has none.
    /// </summary>
    public static string Create(string folder, string name, string exe, string window, JsonNode? status)
    {
        if (File.Exists(Path.Combine(folder, "suite.json")))
            throw new InvalidOperationException($"{folder} already has a suite.json.");
        var newline = Environment.NewLine;
        Directory.CreateDirectory(Path.Combine(folder, "cases"));
        var app = new JsonObject { ["exe"] = Relative(folder, exe), ["window"] = window };
        if (status is not null)
            app["status"] = "statusText";
        File.WriteAllText(Path.Combine(folder, "suite.json"), string.Join(newline,
            "{",
            $"  \"$schema\": {JsonLine.Quote(Schema + "suite.schema.json")},",
            $"  \"name\": {JsonLine.Quote(name)},",
            $"  \"app\": {JsonLine.Write(app)}",
            "}", ""));
        File.WriteAllText(Path.Combine(folder, "elements.json"), string.Join(newline,
            ["{", $"  \"$schema\": {JsonLine.Quote(Schema + "elements.schema.json")}" + (status is null ? "" : ","),
                .. status is null ? Array.Empty<string>() : [$"  \"statusText\": {JsonLine.Write(status)}"], "}", ""]));
        if (status is not null)
        {
            Directory.CreateDirectory(Path.Combine(folder, "macros"));
            File.WriteAllText(Path.Combine(folder, "macros", "waitForStatusBar.json"), string.Join(newline,
                "{",
                $"  \"$schema\": {JsonLine.Quote(Schema + "macro.schema.json")},",
                "  \"description\": \"Waits for the status bar to say something new that starts with startsWith.\",",
                "  \"params\": {",
                "    \"startsWith\": { \"description\": \"What it should start with.\" },",
                "    \"within\": { \"description\": \"Seconds to wait.\", \"default\": \"30\" }",
                "  },",
                "  \"steps\": [",
                "    { \"waitText\": \"statusText\", \"startsWith\": \"${startsWith}\", \"new\": true, \"within\": \"${within}\", \"as\": \"status\" }",
                "  ],",
                "  \"returns\": \"status\"",
                "}", ""));
        }
        return folder;
    }

    // The exe relative to the suite folder when it is inside it, so the suite can be copied with its app; else as given.
    private static string Relative(string folder, string exe)
    {
        var relative = Path.GetRelativePath(folder, exe);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? exe : relative.Replace('\\', '/');
    }
}
