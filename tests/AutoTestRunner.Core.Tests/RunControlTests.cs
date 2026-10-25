using AutoTestRunner.Driving;

namespace AutoTestRunner.Core.Tests;

public class RunControlTests
{
    [Fact]
    public async Task A_pause_holds_the_run_until_it_carries_on()
    {
        var control = new RunControl(unattended: false);
        control.PauseOrCarryOn();
        var run = Task.Run(control.Gate, TestContext.Current.CancellationToken);

        var first = await Task.WhenAny(run, Task.Delay(200, TestContext.Current.CancellationToken));
        Assert.NotSame(run, first);
        control.PauseOrCarryOn();
        await run.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Enter_does_one_step_and_turns_on_step_by_step()
    {
        var control = new RunControl(unattended: false);
        control.Hold(HoldReason.Step);
        control.NextStep();
        Assert.Equal(HoldReason.None, control.Waiting);
        Assert.True(control.StepByStep);
        control.Hold(HoldReason.Step);
        control.PauseOrCarryOn();
        Assert.False(control.StepByStep);
    }

    [Fact]
    public async Task A_stop_ends_a_held_run()
    {
        var control = new RunControl(unattended: false);
        control.Hold(HoldReason.Checkpoint);
        var run = Task.Run(control.Gate, TestContext.Current.CancellationToken);
        control.RequestStop();
        await Assert.ThrowsAsync<StopRunException>(() => run.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Keys_do_nothing_while_stuck()
    {
        var control = new RunControl(unattended: false);
        control.Hold(HoldReason.Stuck);
        control.PauseOrCarryOn();
        control.NextStep();
        Assert.Equal(HoldReason.Stuck, control.Waiting);
    }
}
