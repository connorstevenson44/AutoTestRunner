using System.IO;
using AutoTestRunner.Building;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// Pick it, on the stuck card: the person clicks the control the step should have found, the click is kept from the
/// app, and the new way to find it is written to elements.json or the step's file once they say so. The run then
/// tries the step again.
/// </summary>
internal sealed partial class RunPanel
{
    // Runs on the run's thread, which is waiting for the stuck card's answer. True once the new control is saved.
    private bool PickAndFix(StuckStep stuck, TargetFix fix)
    {
        if (_runner is not { } runner || _driver is not { } driver)
            return false;
        var inspector = new FlaUiInspector(driver);
        while (true)
        {
            var app = inspector.Processes();
            var clicked = new TaskCompletionSource<(int X, int Y, bool Exact)?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var back = OnUi(() => Ask(Ui.Accent, Icons.Pick, "Pick the control",
                $"Click the control in {_suite.App.Window} that the step should use. The click doesn't reach the app. Hold Shift to pick exactly what is under the mouse. Esc goes back.",
                [(Icons.Back, "Back")]));
            var hook = OnUi(() => new RecordingHook(
                (x, y, _) =>
                {
                    if (!app.Contains(Native.ProcessOf(Native.WindowAt(x, y))))
                        return false;
                    clicked.TrySetResult((x, y, RecordingHook.ShiftHeld));
                    return true;
                },
                keys =>
                {
                    if (keys == "Esc")
                        clicked.TrySetResult(null);
                    return false;
                },
                () => false));
            Task.WaitAny(clicked.Task, back);
            OnUi(hook.Dispose);
            if (back.IsCompleted || clicked.Task.Result is not { } at)
                return false;
            OnUi(() => Answer(0));

            if (inspector.At(at.X, at.Y) is not { } hit)
                continue;
            var (facts, choice) = inspector.Pick(hit.Facts, _suite.Elements, runner.Globals, at.Exact);
            var answer = OnUi(() => Ask(Ui.Accent, Icons.Pick, "Use this control?",
                $"{facts.Type} \"{facts.Label}\", found by {choice.Shown}. {choice.Why}\n\nIt is saved in {fix.Shown}, and the step is tried again.",
                [(Icons.Save, "Save and try again"), (Icons.Pick, "Pick again"), (Icons.Back, "Back")])).Result;
            if (answer == 1)
                continue;
            if (answer != 0)
                return false;
            try
            {
                TargetFixer.Save(_suite, stuck, fix, choice.Json);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                OnUi(() => Ask(Ui.Fail, Icons.Failed, "Couldn't save it", ex.Message, [(Icons.Back, "Back")])).Wait();
                return false;
            }
        }
    }
}
