namespace AutoTestRunner.Driving;

/// <summary>
/// A click or a key of the person's in a manual run: where on the screen a button went, or the keys pressed, as
/// <see cref="KeyNames"/> writes them. At is when, in <see cref="Environment.TickCount64"/> milliseconds.
/// </summary>
public sealed record PersonEvent(PersonEventKind Kind, int X, int Y, string? Keys, long At)
{
    public static PersonEvent Button(PersonEventKind kind, int x, int y) => new(kind, x, y, null, Environment.TickCount64);

    public static PersonEvent Pressed(string keys) => new(PersonEventKind.Keys, 0, 0, keys, Environment.TickCount64);
}
