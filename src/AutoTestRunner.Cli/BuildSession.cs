using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using AutoTestRunner.Building;
using AutoTestRunner.Cli.Views;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;

namespace AutoTestRunner.Cli;

/// <summary>
/// A case built by using the app: the builder panel beside it, the hook that sees the person's clicks and keys, and the
/// builder's thread that reads and drives the app. The hook and the panel are on the UI thread and only note what
/// happened, as Windows drops a hook that keeps it waiting; the builder's thread makes steps of it, in order.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The hook goes when the panel closes. The queue is the collector's, as both threads use it until they end.")]
internal sealed class BuildSession
{
    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(120);

    private readonly BuildRequest _request;
    private readonly Options _options;
    private readonly CaseDraft _draft;
    private readonly BuilderPanel _panel;
    private readonly RunControl _control = new(unattended: false, Speed.Fast);
    private readonly BlockingCollection<BuildInput> _inputs = [];
    private readonly Action _ended;
    private RecordingHook? _hook;
    private volatile IReadOnlySet<int> _app = new HashSet<int>();
    private volatile bool _playing;

    private BuildSession(BuildRequest request, Options options, Action ended)
    {
        (_request, _options, _ended) = (request, options, ended);
        _draft = request.Edit is { } editing ? CaseDraft.Load(editing) : new CaseDraft { Name = request.Name };
        if (request.After is { } after)
            _draft.Needs.Add(after.Id);
        _panel = new BuilderPanel(request, _draft);
        _panel.Play += (step, add) => Post(new BuildInput(BuildInputKind.Play, Step: step, Add: add));
        _panel.Locate += step => Post(new BuildInput(BuildInputKind.Locate, Step: step));
        _panel.AddStep += step => Post(new BuildInput(BuildInputKind.Add, Step: step));
        _panel.RunTo += step => Post(new BuildInput(BuildInputKind.RunTo, Step: step));
        _panel.RollBack += step => Post(new BuildInput(BuildInputKind.RollBack, Step: step));
        _panel.EditFrom += (step, rewind) => Post(new BuildInput(BuildInputKind.EditFrom, Step: step, Rewind: rewind));
        _panel.Try += (check, answer) => Post(new BuildInput(BuildInputKind.Try, Check: check, Answer: answer));
        _panel.MadeMacro += macro => Post(new BuildInput(BuildInputKind.Use, Macro: macro));
        _panel.Loaded += (_, _) =>
        {
            // A low-level hook runs on the thread that set it, which must pump messages: this one does.
            _hook = new RecordingHook(OnDown, OnKeys, () => _playing, OnRight);
            var worker = new Thread(Work) { IsBackground = true, Name = "Builder" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        };
        _panel.Closed += (_, _) =>
        {
            _hook?.Dispose();
            _inputs.CompleteAdding();
            _control.RequestStop();
        };
    }

    /// <summary>
    /// Opens the builder panel, gets the app to where the case starts, and turns what the person does in it into
    /// steps. ended is called once the panel has closed and the app with it.
    /// </summary>
    public static BuilderPanel Open(BuildRequest request, Options options, Action ended)
    {
        var session = new BuildSession(request, options, ended);
        session._panel.Show();
        return session._panel;
    }

    /// <summary>Whether the builder is playing steps or passing on input itself, when the hook notices nothing.</summary>
    public bool Playing
    {
        get => _playing;
        set => _playing = value;
    }

    /// <summary>The next thing the person did, or null after a moment of nothing. False once the panel has closed.</summary>
    public bool Next(out BuildInput? input)
    {
        input = null;
        if (_inputs.IsCompleted)
            return false;
        _inputs.TryTake(out input, Idle);
        return true;
    }

    /// <summary>The app's processes now, whose windows the hook watches.</summary>
    public void Watch(IReadOnlySet<int> processes) => _app = processes;

    /// <summary>Whether a window is the app's.</summary>
    public bool IsApp(nint window) => _app.Contains(Desktop.ProcessOf(window));

    // ===== The builder's thread =====

    private void Work()
    {
        var suite = _request.Suite;
        var results = new Results(Results.NewFolder(SuitePane.ResultsFolder(suite, _options)), Console.Out);
        var view = new BuilderView(_panel);
        try
        {
            using var driver = new FlaUiDriver(_control, () => _panel.Bounds, _ => { }, note => results.Log("NOTE", note));
            var runner = new Runner(suite, driver, new WindowsMachine(), view, _control, results, _options.Variables);
            results.Log("START", $"Building in {suite.Name}: {runner.Exe}");
            try
            {
                new BuildWorker(this, _request, _draft, _panel, view, driver, runner).Run();
            }
            finally
            {
                driver.Close();
            }
        }
        catch (Exception ex)
        {
            results.Log("ERROR", ex.ToString());
            _panel.Tell("Something went wrong in the builder itself. It's in run.log:\n\n" + ex.Message);
        }
        finally
        {
            _ended();
        }
    }

    // ===== The hook, on the UI thread: it only notes what happened =====

    // A click in the app is noted while recording; while picking, or with Ctrl held, it picks and doesn't reach the
    // app. A message box or file picker closes as it is clicked, so that click waits until the builder has read it.
    private bool OnDown(int x, int y, bool ctrl)
    {
        var mode = _panel.Mode;
        var window = Desktop.WindowAt(x, y);
        if (mode == BuildMode.Paused || _inputs.IsAddingCompleted || !IsApp(window))
            return false;
        var pick = mode == BuildMode.Picking || ctrl;
        var passOn = !pick && Desktop.IsDialog(window);
        _inputs.Add(new BuildInput(pick ? BuildInputKind.Pick : BuildInputKind.Click, x, y, PassOn: passOn));
        return pick || passOn;
    }

    // A right-click in the app while recording, which reaches the app: the menu it opens is recorded with it.
    private void OnRight(int x, int y)
    {
        if (_panel.Mode == BuildMode.Recording && !_inputs.IsAddingCompleted && IsApp(Desktop.WindowAt(x, y)))
            _inputs.Add(new BuildInput(BuildInputKind.RightClick, x, y));
    }

    // A key pressed in the app while recording. In a message box, Enter and Esc wait as its clicks do.
    private bool OnKeys(string keys)
    {
        var window = Desktop.Foreground();
        if (_panel.Mode != BuildMode.Recording || _inputs.IsAddingCompleted || !IsApp(window))
            return false;
        var passOn = keys is "Enter" or "Esc" && Desktop.IsDialog(window);
        _inputs.Add(new BuildInput(BuildInputKind.Key, Keys: keys, PassOn: passOn));
        return passOn;
    }

    private void Post(BuildInput input)
    {
        if (!_inputs.IsAddingCompleted)
            _inputs.Add(input);
    }
}
