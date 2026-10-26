namespace AutoTestRunner.Scripting;

/// <summary>A control to find: the selectors to try in order, and what to call it in a message.</summary>
public sealed record Target(IReadOnlyList<Selector> Selectors, string What)
{
    public static Target Of(Selector selector) => new([selector], selector.Describe());
}
