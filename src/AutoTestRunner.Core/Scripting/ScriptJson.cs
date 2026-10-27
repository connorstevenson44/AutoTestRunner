using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>Reads JSON as the suites are written by hand: with comments, and a comma after the last item.</summary>
public static class ScriptJson
{
    public static readonly JsonDocumentOptions Document = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    public static readonly JsonReaderOptions Reader = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static JsonNode? Parse(string text) => JsonNode.Parse(text, documentOptions: Document);

    public static JsonNode? Parse(ReadOnlySpan<byte> utf8) => JsonNode.Parse(utf8, documentOptions: Document);

    public static JsonNode? ReadFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>The line ending the text is written with, so an edit keeps to it.</summary>
    public static string NewLineOf(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
