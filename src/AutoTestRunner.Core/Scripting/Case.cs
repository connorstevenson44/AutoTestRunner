using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>A test case: the steps a person takes, ending at a checkpoint.</summary>
public sealed record Case(
    int Number, string Id, string Name, string File, IReadOnlyList<string> Needs, string? NeedsNote, string? Covers, JsonArray Steps)
{
    /// <summary>Why the case needs looking at again, such as a change to a case it carries on from; null when it doesn't.</summary>
    public string? Review { get; init; }

    /// <summary>Words to pick cases by, such as smoke.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Steps that put the app back to where this case leaves it, such as closing what is open and going to the home
    /// screen after signing in. They play after its checkpoint, and before each case that starts after its reset.
    /// </summary>
    public JsonArray Reset { get; init; } = [];

    /// <summary>
    /// The id of the case this one starts after the reset of, such as log-in: once that case has run, its reset steps
    /// take the app back there instead of it starting again. Null when it doesn't.
    /// </summary>
    public string? AfterReset { get; init; }
}
