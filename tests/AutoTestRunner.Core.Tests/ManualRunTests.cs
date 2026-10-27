using System.Text.Json.Nodes;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>
/// A manual run: the test is the person, who changes the fake app and notes clicks and keys as the panel's hook would.
/// The driver must never be asked to click or type.
/// </summary>
public sealed class ManualRunTests : IDisposable
{
    private static readonly string Demo = Path.Combine(AppContext.BaseDirectory, "samples", "demo");
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);
    private readonly string _results = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());
    private readonly FakeDriver _app = new();
    private readonly FakeRunView _view = new();
    private readonly RunControl _control = new(unattended: false, Speed.Manual);
    private readonly Runner _runner;

    public ManualRunTests()
    {
        _runner = new Runner(Suite.Load(Demo), _app, new FakeMachine(), _view, _control, new Results(_results, console: null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_results))
            Directory.Delete(_results, recursive: true);
    }

    // Plays steps on a thread of their own, and waits until the run asks the person to do the first of them.
    private async Task<Task> Play(string steps)
    {
        var turns = _view.Turns.Count;
        var run = Task.Run(() => _runner.Play((JsonArray)JsonNode.Parse(steps)!, "test"), TestContext.Current.CancellationToken);
        var end = DateTime.Now + Soon;
        while (_view.Turns.Count == turns && !run.IsCompleted && DateTime.Now < end)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.False(run.IsCompleted, "The step didn't wait for the person.");
        return run;
    }

    private static async Task StillWaiting(Task run)
    {
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.False(run.IsCompleted, "The step went on before the person had done it.");
    }

    private void Click(int x, int y) => _control.Person.Note(PersonEvent.Button(PersonEventKind.LeftDown, x, y));

    [Fact]
    public async Task Typing_waits_until_the_box_holds_the_text_however_it_got_there()
    {
        var box = _app.Add("Edit", "Name", id: "NameBox");

        var run = await Play("""[{ "type": "nameBox", "text": "main" }]""");
        box.Value = "mai";
        await StillWaiting(run);
        box.Value = "main";

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(_app.Done, done => done.StartsWith("type", StringComparison.Ordinal));
        Assert.Equal(["Type \"main\" in nameBox"], _view.Turns);
    }

    [Fact]
    public async Task A_click_counts_only_on_the_control()
    {
        var save = _app.Add("Button", "Save");
        save.Bounds = new ScreenRect(100, 100, 200, 130);

        var run = await Play("""[{ "click": { "button": "Save" } }]""");
        Click(10, 10);
        _control.Person.Note(PersonEvent.Pressed("ENTER"));
        await StillWaiting(run);
        Click(150, 115);

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("click Save", _app.Done);
        // Outlined once, and taken away once it was done: never drawn again while it stayed put.
        Assert.Equal([save.Bounds, null], _view.Outlines);
    }

    [Fact]
    public async Task The_outline_follows_a_control_that_moves()
    {
        var save = _app.Add("Button", "Save");
        save.Bounds = new ScreenRect(100, 100, 200, 130);

        var run = await Play("""[{ "click": { "button": "Save" } }]""");
        save.Bounds = new ScreenRect(100, 300, 200, 330);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Click(150, 315);

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
        Assert.Equal([new ScreenRect(100, 100, 200, 130), new ScreenRect(100, 300, 200, 330), null], _view.Outlines);
    }

    [Fact]
    public async Task A_double_click_needs_two_clicks_close_together()
    {
        _app.Add("Button", "Save").Bounds = new ScreenRect(100, 100, 200, 130);

        var run = await Play("""[{ "click": { "button": "Save" }, "how": "double" }]""");
        Click(150, 115);
        await StillWaiting(run);
        Click(150, 115);
        Click(150, 115);

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_click_on_a_greyed_out_control_does_nothing()
    {
        var save = _app.Add("Button", "Save");
        save.Bounds = new ScreenRect(100, 100, 200, 130);
        save.IsEnabled = false;

        var run = await Play("""[{ "click": { "button": "Save" } }]""");
        Click(150, 115);
        await StillWaiting(run);
        save.IsEnabled = true;
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Click(150, 115);

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_key_step_waits_for_those_keys_however_they_are_spelt()
    {
        var run = await Play("""[{ "key": "Control+s" }]""");
        _control.Person.Note(PersonEvent.Pressed(KeyNames.Of('S', ctrl: false, alt: false, shift: true, win: false)!));
        await StillWaiting(run);
        _control.Person.Note(PersonEvent.Pressed(KeyNames.Of('S', ctrl: true, alt: false, shift: false, win: false)!));

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(_app.Done, done => done.StartsWith("key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ticking_and_picking_wait_for_the_control_to_say_so()
    {
        var agree = _app.Add("CheckBox", "Agree");
        var colour = _app.Add("ComboBox", "Colour");
        agree.IsChecked = false;

        var run = await Play("""[{ "tick": { "checkBox": "Agree" } }, { "choose": { "comboBox": "Colour" }, "option": "Blue" }]""");
        agree.IsChecked = true;
        var end = DateTime.Now + Soon;
        while (_view.Turns.Count < 2 && DateTime.Now < end)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        colour.Selection = "Red";
        await StillWaiting(run);
        colour.Selection = "Blue";

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(_app.Done, done => done.StartsWith("tick", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_menu_is_done_once_its_last_item_is_clicked()
    {
        _app.Add("MenuItem", "File").Bounds = new ScreenRect(0, 0, 40, 20);

        var run = await Play("""[{ "menu": "File > Save" }]""");
        Click(20, 10);
        _app.Add("MenuItem", "Save").Bounds = new ScreenRect(0, 20, 80, 40);
        await StillWaiting(run);
        Click(30, 30);

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(_app.Done, done => done.StartsWith("menu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Space_on_a_button_that_has_the_keyboard_clicks_it()
    {
        var save = _app.Add("Button", "Save");
        save.Bounds = new ScreenRect(100, 100, 200, 130);

        var run = await Play("""[{ "click": { "button": "Save" } }]""");
        _control.Person.Note(PersonEvent.Pressed("SPACE"));
        await StillWaiting(run);
        save.HasFocus = true;
        await Task.Delay(300, TestContext.Current.CancellationToken);
        _control.Person.Note(PersonEvent.Pressed("SPACE"));

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Changing_the_speed_from_manual_has_the_runner_do_the_step()
    {
        _app.Add("Edit", "Name", id: "NameBox");

        var run = await Play("""[{ "type": "nameBox", "text": "main" }]""");
        _control.SetSpeed(Speed.Fast);

        await run.WaitAsync(Soon, TestContext.Current.CancellationToken);
        Assert.Contains("type main", _app.Done);
    }

    [Fact]
    public async Task What_the_run_does_by_itself_it_still_does()
    {
        _app.Add("Edit", "Name", id: "NameBox").Value = "main";

        await Task.Run(() => _runner.Play((JsonArray)JsonNode.Parse("""[{ "startApp": {} }, { "find": "nameBox", "as": "box" }]""")!, "test"), TestContext.Current.CancellationToken)
            .WaitAsync(Soon, TestContext.Current.CancellationToken);

        Assert.True(_app.Started);
        Assert.Empty(_view.Turns);
    }

    [Fact]
    public void An_unattended_run_is_never_manual()
    {
        var control = new RunControl(unattended: true, Speed.Manual);
        control.SetSpeed(Speed.Manual);

        Assert.False(control.Manual);
        control.Person.Note(PersonEvent.Pressed("ENTER"));
        Assert.Empty(control.Person.Take());
    }

    [Theory]
    [InlineData("Ctrl+S", "control+s")]
    [InlineData("Shift+Ctrl+Tab", "Ctrl+Shift+TAB")]
    [InlineData("Return", "Enter")]
    [InlineData("Esc", "ESCAPE")]
    [InlineData("PgDn", "PageDown")]
    public void Keys_spelt_differently_are_the_same(string one, string other) => Assert.True(KeyNames.Same(one, other));

    [Fact]
    public void Keys_from_the_hook_are_spelt_as_a_step_spells_them()
    {
        Assert.True(KeyNames.Same("Ctrl+Shift+F5", KeyNames.Of(0x74, ctrl: true, alt: false, shift: true, win: false)!));
        Assert.True(KeyNames.Same("Enter", KeyNames.Of(0x0D, ctrl: false, alt: false, shift: false, win: false)!));
        Assert.False(KeyNames.Same("Ctrl+S", KeyNames.Of('S', ctrl: false, alt: false, shift: false, win: false)!));
    }

    [Theory]
    [InlineData('S', true, false, false, "Ctrl+S")]
    [InlineData(0x22, false, false, true, "Shift+PageDown")]
    [InlineData(0x74, true, true, false, "Ctrl+Alt+F5")]
    [InlineData(0x1B, false, false, false, "Esc")]
    [InlineData(0x08, false, false, false, "Backspace")]
    [InlineData(0x61, false, false, false, "Numpad1")]
    public void Keys_recorded_in_a_form_are_written_as_a_person_writes_them(int key, bool ctrl, bool alt, bool shift, string written)
    {
        Assert.Equal(written, KeyNames.Written(key, ctrl, alt, shift, win: false));
        Assert.True(KeyNames.Same(written, KeyNames.Of(key, ctrl, alt, shift, win: false)!));
    }

    [Fact]
    public void Ctrl_or_shift_on_its_own_isnt_recorded_until_a_key_is_pressed_with_it()
    {
        Assert.Null(KeyNames.Written(0x11, ctrl: true, alt: false, shift: false, win: false));
        Assert.Null(KeyNames.Written(0xA0, ctrl: false, alt: false, shift: true, win: false));
    }
}
