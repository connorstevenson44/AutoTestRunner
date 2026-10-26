namespace AutoTestRunner.Running;

/// <summary>
/// A check ready to run: what it looks at, how it compares and what it wants, how long it may take to pass, and the
/// test, which says what it saw. Label is the script's own name for it, or one made from the rest.
/// </summary>
public sealed record Check(string Label, Subject Subject, string How, string Expected, TimeSpan Within, Func<Seen> Test)
{
    /// <summary>Runs the test once: an error, such as a file it can't read, is what it saw.</summary>
    public Seen Try()
    {
        try
        {
            return Test();
        }
        catch (Exception ex) when (ex is not Driving.StopRunException)
        {
            return new Seen(false, "error: " + ex.Message);
        }
    }

    public CheckResult Result(Seen seen) => new(Label, Subject, How, Expected, seen.Actual, seen.Passed) { Full = seen.Full };
}
