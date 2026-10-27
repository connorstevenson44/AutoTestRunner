namespace AutoTestRunner.Driving;

/// <summary>
/// What the person does with the mouse and the keyboard in a manual run, so the run can tell when they have done the
/// step it is waiting for. The panel's hook notes each click and key on a thread of its own, and the run takes what was
/// noted since it last looked. Nothing is noted unless the run is manual, so nothing piles up.
/// </summary>
public sealed class PersonInput(Func<bool> noting)
{
    // Far more than a person does between two looks, which are a fifth of a second apart.
    private const int Most = 200;

    private readonly object _lock = new();
    private readonly List<PersonEvent> _events = [];

    // Both halves of where the mouse is in one long, so a move is noted without a lock: moves come by the hundred.
    private long _mouse;

    /// <summary>Whether a manual run is noting what the person does; the hook can skip working out what it was otherwise.</summary>
    public bool Noting => noting();

    /// <summary>Where the mouse is on the screen, as the person last moved it.</summary>
    public (int X, int Y) Mouse
    {
        get
        {
            var mouse = Interlocked.Read(ref _mouse);
            return ((int)(mouse >> 32), (int)mouse);
        }
    }

    public void Moved(int x, int y)
    {
        if (noting())
            Interlocked.Exchange(ref _mouse, ((long)x << 32) | (uint)y);
    }

    public void Note(PersonEvent happened)
    {
        if (!noting())
            return;
        lock (_lock)
        {
            if (_events.Count == Most)
                _events.RemoveAt(0);
            _events.Add(happened);
        }
    }

    /// <summary>What the person did since the last time, oldest first.</summary>
    public IReadOnlyList<PersonEvent> Take()
    {
        lock (_lock)
        {
            var taken = _events.ToList();
            _events.Clear();
            return taken;
        }
    }
}
