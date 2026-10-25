namespace AutoTestRunner.Driving;

/// <summary>Why the run is holding, if it is.</summary>
public enum HoldReason { None, Pause, Step, Checkpoint, Stuck }

/// <summary>The three speeds a watched run can go at.</summary>
public enum Speed { Slow, Modest, Fast }

/// <summary>Thrown to give up on the rest of a case, after the person chose to skip it.</summary>
public sealed class SkipCaseException(string message) : Exception(message);

/// <summary>Thrown to end the whole run: the person stopped it, or an unattended run got stuck.</summary>
public sealed class StopRunException(string message) : Exception(message);

/// <summary>
/// The pace of a run and whether it is holding: paused, waiting for the next step, at a checkpoint or stuck. The run
/// itself goes on a worker thread and calls <see cref="Gate"/> between steps and during waits; the panel's keys and
/// buttons call the other methods from the UI thread.
/// </summary>
public sealed class RunControl
{
    private readonly object _lock = new();

    // Pace, mouse speed and key delay for each speed, as the AutoHotkey runner had them.
    private static readonly Dictionary<Speed, (int Pace, int Mouse, int Key)> Speeds = new()
    {
        [Speed.Slow] = (1800, 25, 110),
        [Speed.Modest] = (1100, 15, 60),
        [Speed.Fast] = (450, 5, 20),
    };

    public RunControl(bool unattended, Speed speed = Speed.Modest)
    {
        Unattended = unattended;
        if (unattended)
            (Pace, MouseSpeed, KeyDelay, TimeoutScale) = (150, 0, 10, 3);
        else
            SetSpeed(speed);
    }

    public bool Unattended { get; }

    /// <summary>Milliseconds spent on each action, so it can be followed.</summary>
    public int Pace { get; private set; }

    /// <summary>0 moves the mouse instantly, 100 is the slowest.</summary>
    public int MouseSpeed { get; private set; }

    /// <summary>Milliseconds between typed keys.</summary>
    public int KeyDelay { get; private set; }

    /// <summary>Waits are longer on a slow CI machine.</summary>
    public double TimeoutScale { get; set; } = 1;

    public Speed Speed { get; private set; } = Speed.Modest;

    /// <summary>Waits at every checkpoint until the person carries on.</summary>
    public bool StopAtCheckpoints { get; set; }

    /// <summary>Waits before every step until the person presses Enter.</summary>
    public bool StepByStep { get; set; }

    public HoldReason Waiting { get; private set; }

    public bool StopRequested { get; private set; }

    /// <summary>Raised whenever Waiting, StepByStep or StopRequested change, on whichever thread changed them.</summary>
    public event Action? Changed;

    public void SetSpeed(Speed speed)
    {
        Speed = speed;
        (Pace, MouseSpeed, KeyDelay) = Speeds[speed];
    }

    public TimeSpan Scale(TimeSpan timeout) => timeout * TimeoutScale;

    /// <summary>Holds the run while it is waiting, and ends it once a stop was asked for.</summary>
    public void Gate()
    {
        lock (_lock)
        {
            while (Waiting != HoldReason.None && !StopRequested)
                Monitor.Wait(_lock);
        }
        if (StopRequested)
            throw new StopRunException("You stopped the test.");
    }

    /// <summary>Sleeps, a little at a time, so a pause or stop takes effect during long waits.</summary>
    public void Wait(int milliseconds)
    {
        var end = Environment.TickCount64 + milliseconds;
        while (true)
        {
            Gate();
            var left = end - Environment.TickCount64;
            if (left <= 0)
                return;
            lock (_lock)
                Monitor.Wait(_lock, TimeSpan.FromMilliseconds(Math.Min(50, left)));
        }
    }

    /// <summary>Holds the run, for a reason the runner sets: before a step, at a checkpoint or while stuck.</summary>
    public void Hold(HoldReason why) => Update(() => Waiting = why);

    /// <summary>Space: pauses a run that is going. On a run that is waiting, carries on by itself.</summary>
    public void PauseOrCarryOn()
    {
        if (Waiting == HoldReason.Stuck || StopRequested)
            return;
        if (Waiting == HoldReason.None)
            Update(() => Waiting = HoldReason.Pause);
        else
            Update(() => (StepByStep, Waiting) = (false, HoldReason.None));
    }

    /// <summary>Enter: does the next step, then waits for Enter again.</summary>
    public void NextStep()
    {
        if (Waiting == HoldReason.Stuck || StopRequested)
            return;
        Update(() =>
        {
            StepByStep = true;
            Waiting = HoldReason.None;
        });
    }

    public void CarryOn() => Update(() => Waiting = HoldReason.None);

    public void RequestStop() => Update(() => StopRequested = true);

    private void Update(Action change)
    {
        lock (_lock)
        {
            change();
            Monitor.PulseAll(_lock);
        }
        Changed?.Invoke();
    }
}
