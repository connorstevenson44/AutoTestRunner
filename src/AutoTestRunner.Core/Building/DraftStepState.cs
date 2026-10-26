namespace AutoTestRunner.Building;

/// <summary>Where a step of a case being built is: done in the app, still to play, or played and it didn't work.</summary>
public enum DraftStepState
{
    /// <summary>Done in the app, as a recorded step is when it is made.</summary>
    Played,

    /// <summary>A step of the case opened, which the app hasn't been taken through yet.</summary>
    NotPlayed,

    /// <summary>Played, and it didn't work: Problem says why.</summary>
    Failed,
}
