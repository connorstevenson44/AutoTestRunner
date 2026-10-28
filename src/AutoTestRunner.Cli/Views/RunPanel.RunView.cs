using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

internal sealed partial class RunPanel
{
    public void CaseStarted(int index, int count, Case item) => OnUi(() =>
    {
        EndWait();
        _caseIndex = index - 1;
        _caseFailed = false;
        _caseCount.Text = $"{index} / {count}";
        _caseName.Text = item.Name;
        _caseName.ToolTip = item.Covers;
        if (_caseIndex < _segments.Length)
            _segments[_caseIndex].Background = Ui.Accent;
        _feed.CaseStarted(item.Number, item.Name);
    });

    public void CaseFinished(CaseResult result) => OnUi(() =>
    {
        EndWait();
        _ended.Add(result);
        if (_caseIndex < _segments.Length)
        {
            _segments[_caseIndex].Background = Ui.Of(result.Status);
            _segments[_caseIndex].ToolTip = $"{result.Number}. {result.Name}: {result.Status.ToString().ToLowerInvariant()}";
        }
        _feed.CaseFinished(result);
    });

    public void ShowStep(Activity step) => OnUi(() =>
    {
        EndWait();
        HideCountdown();
        _step = step;
        _nowIcon.Text = Icons.Of(step.Kind);
        StepLine.Fill(_nowText, step, 14.5);
        _nowText.ToolTip = step.Say;
        _nowCommandText.Text = step.Command is null ? "" : "> " + step.Command.Replace("\r", "");
        _nowCommand.Visibility = step.Command is null ? Visibility.Collapsed : Visibility.Visible;
        _feed.Step(step);
    });

    public void ShowWait(Activity what) => OnUi(() => StartWait(what));

    public void Outline(ScreenRect? box) => OnUi(() =>
    {
        if (box is { } place)
            _outline.Show(place, null);
        else
            _outline.Hide();
    });

    // The step's line, with a secret's real words, as the person has to type them; how to do it waits under it.
    public void YourTurn(Activity? what) => OnUi(() =>
    {
        _yourTurn = what;
        if (what is null)
        {
            EndWait();
            ShowState();
            // A secret's words go once it is typed.
            if (_step is not null)
                StepLine.Fill(_nowText, _step, 14.5);
            return;
        }
        HideCountdown();
        StepLine.Fill(_nowText, what with { Detail = null }, 14.5);
        StartWait(Activity.Words(what.Detail ?? "Do it in the app."));
        ShowState();
    });

    public void Waited(Activity what, TimeSpan took) => OnUi(() =>
    {
        EndWait();
        _feed.Waited(what, took);
    });

    public void CommandStarted(string line, string? script) => OnUi(() =>
    {
        StartWait(Activity.Of(ActivityKind.Command, Part.Verb("Running")));
        _feed.CommandStarted(line, script);
    });

    // From the threads reading what the program prints, which the feed takes from any thread.
    public void CommandOutput(string line, bool error) => _feed.CommandOutput(line, error);

    public void CommandFinished(int exitCode, TimeSpan took) => OnUi(() =>
    {
        EndWait();
        _feed.CommandFinished(exitCode, took);
    });

    public void StepFailed(StepFailure failure) => OnUi(() =>
    {
        _caseFailed = true;
        _feed.StepFailed(failure);
    });

    public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) => OnUi(() =>
    {
        EndWait();
        if (_feed.Checkpoint(title, checks, look, tools, OpenTool))
        {
            _caseFailed = true;
            System.Media.SystemSounds.Exclamation.Play();
        }
        else if (_control.StopAtCheckpoints || _control.StepByStep)
            System.Media.SystemSounds.Asterisk.Play();
    });

    public void ShowCarryingOn(TimeSpan after) => OnUi(() =>
    {
        SetState(Icons.Play, "Carrying on", Ui.Pass, Ui.PassSoft, "Space holds it here");
        _countdown.Visibility = Visibility.Visible;
        ((ScaleTransform)_countdown.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, after));
    });

    public StuckAnswer AskStuck(string message, string screenshot)
    {
        while (true)
        {
            // A step whose control can be picked again offers Pick it, which writes the new way to find it.
            var stuck = _runner?.StuckAt;
            var fix = stuck is null ? null : TargetFixer.Where(_suite, stuck);
            (string, string)[] answers = fix is null
                ? [(Icons.Retry, "Try again"), (Icons.Skipped, "Skip the case"), (Icons.Stop, "Stop")]
                : [(Icons.Retry, "Try again"), (Icons.Pick, "Pick it"), (Icons.Skipped, "Skip the case"), (Icons.Stop, "Stop")];
            var how = fix is null ? "Put the app right by hand if you can, then try again." : "Put the app right by hand if you can, then try again; or Pick it, to point at the control the step should use.";
            var answer = OnUi(() => Ask(Ui.Fail, Icons.Warning, "Stuck", message + "\n\n" + how, answers, screenshot)).Result;
            if (fix is not null)
            {
                if (answer == 1)
                {
                    if (PickAndFix(stuck!, fix))
                        return StuckAnswer.TryAgain;
                    continue;
                }
                if (answer > 1)
                    answer--;
            }
            return answer switch
            {
                0 => StuckAnswer.TryAgain,
                1 => StuckAnswer.SkipCase,
                _ => StuckAnswer.Stop,
            };
        }
    }

    public bool AskScriptError(string message) =>
        OnUi(() => Ask(Ui.Fail, Icons.Failed, "Script error", message, [(Icons.Next, "Next case"), (Icons.Stop, "Stop")])).Result == 0;

    public void KeepClear(ScreenRect rect) => OnUi(() =>
    {
        if (Bounds is not { } panel || !panel.Overlaps(rect))
            return;
        var area = SystemParameters.WorkArea;
        Left = Left > area.Left + 10 ? area.Left : area.Right - ActualWidth;
    });

    public void Finished(string summary, string resultsFolder, bool allPassed) => OnUi(() =>
    {
        EndWait();
        HideCountdown();
        _control.CarryOn();
        _over = true;
        _main.IsEnabled = _next.IsEnabled = false;
        SetState(allPassed ? Icons.Passed : Icons.Failed, "Finished", allPassed ? Ui.Pass : Ui.Fail, allPassed ? Ui.PassSoft : Ui.FailSoft);
        _nowIcon.Text = allPassed ? Icons.Passed : Icons.Failed;
        StepLine.Fill(_nowText, Activity.Words(allPassed ? "Everything passed" : "Finished, with problems"), 14.5);
        _nowCommand.Visibility = Visibility.Collapsed;
        _question.Content = FinishedCard(resultsFolder, allPassed);
        System.Media.SystemSounds.Asterisk.Play();
    });

    /// <summary>Says what stopped the run before it could start, or went wrong in the runner itself.</summary>
    public void Tell(string message) => OnUi(() =>
    {
        _ = Ask(Ui.Fail, Icons.Warning, "Can't go on", message, [(Icons.Cross, "Close")]).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            // Closing the panel answers the question too, by which time it is closed already.
            if (IsLoaded)
                Close();
        }));
    });
}
