using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// The app, driven from the launcher's tools: a new suite's exe, or a suite's app to point out its named controls and
/// look for controls without ids. UI Automation goes on a thread of its own, so the launcher stays responsive; what
/// it finds comes back on the UI thread. A control is picked by clicking it, with the click kept from the app.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The queue ends with Dispose, and the thread with it.")]
internal sealed class AppSession : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Dispatcher _ui;
    private FlaUiDriver? _driver;
    private FlaUiInspector? _inspector;

    public AppSession(Dispatcher ui)
    {
        _ui = ui;
        var thread = new Thread(() =>
        {
            using var driver = new FlaUiDriver(new RunControl(unattended: true, Speed.Fast), () => null, _ => { }, _ => { });
            (_driver, _inspector) = (driver, new FlaUiInspector(driver));
            foreach (var work in _work.GetConsumingEnumerable())
                work();
        })
        { IsBackground = true, Name = "App" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>Does something with the app on its thread; then, or failed with why, is called on the UI thread.</summary>
    public void Do<T>(Func<FlaUiDriver, FlaUiInspector, T> work, Action<T> then, Action<string> failed)
    {
        if (_work.IsAddingCompleted)
            return;
        _work.Add(() =>
        {
            try
            {
                var result = work(_driver!, _inspector!);
                _ui.BeginInvoke(() => then(result));
            }
            catch (Exception ex)
            {
                // Anything else would end the thread, and with it autotest: the window says what went wrong instead.
                if (ex is not (StuckException or ScriptException or COMException or InvalidOperationException or TimeoutException
                    or System.ComponentModel.Win32Exception or System.IO.IOException))
                    Crashes.Log(ex);
                _ui.BeginInvoke(() => failed(ex.Message));
            }
        });
    }

    /// <summary>
    /// Waits for a click in the app, kept from it, and calls back with the control clicked, as the person meant it, and
    /// how a step would find it; or with nulls when Esc was pressed. elements are the suite's, which a step can name.
    /// </summary>
    public void Pick(IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode> elements, VariableScope globals, Action<ControlFacts?, TargetChoice?> then, Action<string> failed)
    {
        Do((_, inspector) => inspector.Processes(), app =>
        {
            RecordingHook? hook = null;
            void Done((int X, int Y)? at)
            {
                // A second click, or Esc, can come before the hook has gone: only the first counts.
                if (hook is null)
                    return;
                hook.Dispose();
                hook = null;
                if (at is not { } point)
                {
                    then(null, null);
                    return;
                }
                var window = Desktop.WindowAt(point.X, point.Y);
                Do((driver, inspector) =>
                {
                    // The app's first window may have closed since it started, as a sign-in window does.
                    driver.Follow(window);
                    if (inspector.At(point.X, point.Y) is not { } hit)
                        return ((ControlFacts?)null, (TargetChoice?)null);
                    var facts = hit.Facts.Meant();
                    return (facts, TargetSuggester.Suggest(facts, elements, globals, node =>
                    {
                        try
                        {
                            return driver.Count(Targets.Resolve(node, globals, elements));
                        }
                        catch (Exception ex) when (ex is ScriptException or COMException or InvalidOperationException)
                        {
                            return 0;
                        }
                    }));
                }, found => then(found.Item1, found.Item2), failed);
            }
            hook = new RecordingHook(
                (x, y, _) =>
                {
                    if (!app.Contains(Desktop.ProcessOf(Desktop.WindowAt(x, y))))
                        return false;
                    _ui.BeginInvoke(() => Done((x, y)));
                    return true;
                },
                keys =>
                {
                    if (keys == "Esc")
                        _ui.BeginInvoke(() => Done(null));
                    return false;
                },
                () => false);
        }, failed);
    }

    public void Dispose() => _work.CompleteAdding();
}
