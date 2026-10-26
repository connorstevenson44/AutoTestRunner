using System.Text.Json.Nodes;
using AutoTestRunner.Building;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>The builder's forms, macros and wrapping, and the launcher's tools for cases and named controls.</summary>
public sealed class BuilderToolsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AutoTestRunnerTests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    // A suite of these cases, each a file name and its text, with addButton and statusText in elements.json.
    private Suite Make(params (string File, string Text)[] cases)
    {
        var suite = Path.Combine(_folder, "suite");
        Directory.CreateDirectory(Path.Combine(suite, "cases"));
        File.WriteAllText(Path.Combine(suite, "suite.json"), """
            { "name": "Tools", "app": { "exe": "App.exe", "window": "App", "status": "statusText" } }
            """);
        File.WriteAllText(Path.Combine(suite, "elements.json"), """
            {
              "addButton": { "button": "Add" },
              "statusText": { "id": "StatusText" },
              "nameBox": { "id": "NameBox", "within": "addPanel" },
              "addPanel": { "id": "AddPanel" }
            }
            """);
        foreach (var (file, text) in cases)
            File.WriteAllText(Path.Combine(suite, "cases", file), text);
        return Suite.Load(suite);
    }

    [Fact]
    public void Every_step_and_check_has_a_form_whose_first_field_is_its_own_key()
    {
        foreach (var kind in ScriptVocabulary.Steps.Keys)
        {
            var fields = Forms.ForStep(kind);
            if (ScriptVocabulary.Steps[kind].Takes != Takes.Nothing)
                Assert.Equal(kind, fields[0].Key);
            Assert.All(fields, field => Assert.True(field.Key == kind || ScriptVocabulary.Steps[kind].Keys.Contains(field.Key) || ScriptVocabulary.CommonStepKeys.Contains(field.Key), $"{kind}: {field.Key}"));
        }
        foreach (var kind in ScriptVocabulary.Checks.Keys)
        {
            var fields = Forms.ForCheck(kind);
            if (kind != "nothingLeft")
                Assert.Equal(kind, fields[0].Key);
            Assert.All(fields, field => Assert.True(field.Key == kind || ScriptVocabulary.Checks[kind].Contains(field.Key) || ScriptVocabulary.CommonCheckKeys.Contains(field.Key), $"{kind}: {field.Key}"));
            Assert.Contains(Forms.CheckKinds, offered => offered.Key == kind);
        }
    }

    [Fact]
    public void A_form_reads_and_writes_each_kind_of_field()
    {
        var step = new JsonObject { ["click"] = "addButton" };
        var fields = Forms.ForStep("click").Concat(Forms.ForStep("run")).Concat(Forms.ForStep("wait")).DistinctBy(field => field.Key).ToDictionary(field => field.Key);

        Forms.Write(step, fields["click"], """{ "button": "Add", "within": "addPanel" }""", isKey: true);
        Forms.Write(step, fields["how"], "double");
        Forms.Write(step, fields["whileEnabled"], "true");
        Forms.Write(step, fields["args"], "--quiet\r\n\r\n--name=${name}");
        Forms.Write(step, fields["wait"], "2.5");
        Forms.Write(step, fields["within"], "${slow}");
        Forms.Write(step, fields["say"], "");

        Assert.Equal("""{ "click": { "button": "Add", "within": "addPanel" }, "how": "double", "whileEnabled": true, "args": ["--quiet", "--name=${name}"], "wait": 2.5, "within": "${slow}" }""", JsonLine.Write(step));
        Assert.Equal("--quiet\n--name=${name}", Forms.Read(step, fields["args"]));
        Assert.Equal("true", Forms.Read(step, fields["whileEnabled"]));
        Assert.Equal("2.5", Forms.Read(step, fields["wait"]));
        Forms.Write(step, fields["click"], "addButton", isKey: true);
        Assert.Equal("addButton", Forms.Read(step, fields["click"]));
        Assert.Throws<FormatException>(() => Forms.Write(step, fields["env"], "{ nope"));
    }

    [Fact]
    public void A_macro_is_made_of_steps_with_their_values_as_parameters()
    {
        JsonObject[] steps =
        [
            JsonNode.Parse("""{ "type": "nameBox", "text": "Ada" }""")!.AsObject(),
            JsonNode.Parse("""{ "choose": { "comboBox": "Product" }, "option": "Tea" }""")!.AsObject(),
            JsonNode.Parse("""{ "type": "nameBox", "text": "${password}", "secret": true }""")!.AsObject(),
            JsonNode.Parse("""{ "click": "addButton" }""")!.AsObject(),
        ];

        var values = MacroMaker.Values(steps);
        var (macro, call) = MacroMaker.Make("addOrder", "Add an order", steps, values);

        Assert.Equal(["nameBox", "product"], values.Select(value => value.Name));
        Assert.Equal("""{ "call": "addOrder", "with": { "nameBox": "Ada", "product": "Tea" }, "say": "Add an order" }""", JsonLine.Write(call));
        Assert.Equal("${nameBox}", macro["steps"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("${product}", macro["steps"]![1]!["option"]!.GetValue<string>());

        var suite = Make(("01-a.json", """{ "name": "A", "steps": [] }"""));
        MacroMaker.Save(suite, "addOrder", macro);
        File.WriteAllText(suite.Cases[0].File, $$"""{ "name": "A", "steps": [ {{JsonLine.Write(call)}} ] }""");
        var loaded = Suite.Load(suite.Folder);
        Assert.Equal(["nameBox", "product"], loaded.Macros["addOrder"].Parameters.Keys);
        Assert.Throws<ArgumentException>(() => MacroMaker.Save(loaded, "addOrder", macro));
    }

    [Fact]
    public void A_macro_made_while_building_plays_before_the_suite_is_loaded_again()
    {
        var suite = Make(("01-a.json", """{ "name": "A", "steps": [] }"""));
        var steps = new[] { JsonNode.Parse("""{ "type": "nameBox", "text": "Ada" }""")!.AsObject() };
        var (macro, call) = MacroMaker.Make("fillName", "Fill in the name", steps, MacroMaker.Values(steps));
        var driver = new FakeDriver();
        var box = driver.Add("Edit", "Name", id: "NameBox");
        driver.Add("Pane", "Add", id: "AddPanel");
        var runner = new Runner(suite, driver, new FakeMachine(), new FakeRunView(), new RunControl(unattended: true) { TimeoutScale = 0.01 }, new Results(Path.Combine(_folder, "results"), console: null));

        runner.Use(MacroMaker.Read("fillName", "fillName.json", macro));
        runner.Play(new JsonArray(call), "The builder");

        Assert.Equal("Ada", box.Value);
    }

    [Fact]
    public void Steps_can_go_in_a_repeat_or_an_if_and_the_suite_still_loads()
    {
        JsonObject[] steps = [JsonNode.Parse("""{ "type": "nameBox", "text": "${name}" }""")!.AsObject(), JsonNode.Parse("""{ "click": "addButton" }""")!.AsObject()];
        var repeat = StepWrapper.Repeat(steps, ["Ada", "Grace"], null, "name");
        var only = StepWrapper.OnlyIf(steps, JsonNode.Parse("""{ "shows": "addButton" }""")!.AsObject());

        Make(("01-a.json", $$"""{ "name": "A", "steps": [ {{JsonLine.Write(repeat)}}, {{JsonLine.Write(only)}} ] }"""));

        Assert.Equal("""{ "repeat": ["Ada", "Grace"], "item": "name", "steps": [ { "type": "nameBox", "text": "${name}" }, { "click": "addButton" } ] }""", JsonLine.Write(repeat));
        Assert.Equal(2, StepWrapper.Inside(only).Count);
    }

    [Fact]
    public void Cases_can_be_copied_renamed_moved_and_deleted_and_needs_still_hold()
    {
        var suite = Make(
            ("01-first.json", "{\n  \"name\": \"First\",\n  \"steps\": []\n}\n"),
            ("02-second.json", "{\n  \"name\": \"Second\",\n  \"needs\": [\"first\"],\n  \"steps\": []\n}\n"),
            ("03-third.json", "{\n  \"name\": \"Third\",\n  \"needs\": [\"second\"],\n  \"steps\": []\n}\n"));

        Assert.NotNull(CaseFiles.CantMove(suite, suite.Cases[1], -1));
        Assert.NotNull(CaseFiles.CantMove(suite, suite.Cases[1], 1));
        Assert.NotNull(CaseFiles.CantMove(suite, suite.Cases[0], -1));

        var copy = CaseFiles.Duplicate(suite, suite.Cases[0]);
        CaseFiles.Rename(Suite.Load(suite.Folder).Cases[0], "The first one");
        suite = Suite.Load(suite.Folder);
        Assert.Equal("04-first-copy.json", Path.GetFileName(copy));
        Assert.Equal(["The first one", "Second", "Third", "First (copy)"], suite.Cases.Select(item => item.Name));

        CaseFiles.Move(suite, suite.Cases[3], -1);
        suite = Suite.Load(suite.Folder);
        Assert.Equal(["The first one", "Second", "First (copy)", "Third"], suite.Cases.Select(item => item.Name));
        Assert.Equal("04-third.json", Path.GetFileName(suite.Cases[3].File));

        Assert.Equal(["Third"], CaseFiles.Dependents(suite, suite.Cases[1]).Select(item => item.Name));
        CaseFiles.Delete(suite, suite.Cases[1]);
        suite = Suite.Load(suite.Folder);
        var third = suite.Cases.Single(item => item.Name == "Third");
        Assert.Equal(["first"], third.Needs);
        Assert.Contains("was deleted", third.Review);
    }

    [Fact]
    public void Named_controls_show_where_they_are_used_and_can_be_renamed_everywhere()
    {
        var suite = Make(("01-a.json", """
            { "name": "A", "steps": [
              { "click": "addButton", "say": "addButton" },
              { "click": { "button": "OK", "within": "addPanel" } },
              { "if": { "shows": "addButton" }, "steps": [ { "drag": "nameBox", "to": ["addButton", { "id": "Bin" }] } ] },
              { "checkpoint": "Done", "checks": [ { "says": "statusText", "equals": "addButton" } ] } ] }
            """));

        var uses = ElementUses.Find(suite);

        Assert.Equal(3, uses["addButton"].Count);
        Assert.Equal(["elements.json", "cases/01-a.json"], uses["addPanel"]);
        Assert.Equal(["suite.json", "cases/01-a.json"], uses["statusText"]);

        ElementUses.Rename(suite, "addButton", "addOrder");
        ElementUses.Rename(Suite.Load(suite.Folder), "addPanel", "orderPanel");
        var text = File.ReadAllText(suite.Cases[0].File);
        var renamed = Suite.Load(suite.Folder);

        Assert.Contains("\"say\": \"addButton\"", text);
        Assert.Contains("\"equals\": \"addButton\"", text);
        Assert.Contains("""{ "drag": "nameBox", "to": ["addOrder", { "id": "Bin" }] }""", text);
        Assert.True(renamed.Elements.ContainsKey("addOrder"));
        Assert.Equal("orderPanel", renamed.Elements["nameBox"]["within"]!.GetValue<string>());
        Assert.Equal(3, ElementUses.Find(renamed)["addOrder"].Count);
        Assert.Empty(ElementUses.Find(renamed).GetValueOrDefault("addButton") ?? []);
    }

    [Fact]
    public void The_ids_report_lists_controls_without_an_id_with_the_XAML_to_add()
    {
        var gaps = IdReport.Gaps([
            new ControlFacts("Button", "Add order"),
            new ControlFacts("Button", "Save", Id: "SaveButton"),
            new ControlFacts("Text", "Ready"),
            new ControlFacts("Edit", "") { Bounds = new ScreenRect(1, 2, 3, 4) },
        ]);

        Assert.Equal(["Edit", "Button"], gaps.Select(gap => gap.Control.Type));
        Assert.False(gaps[0].Named);
        Assert.Equal("AutomationProperties.AutomationId=\"AddOrderButton\"", gaps[1].Xaml);
    }

    [Fact]
    public void A_new_suite_loads_with_its_status_bar_and_macro()
    {
        var folder = Path.Combine(_folder, "new");
        Directory.CreateDirectory(Path.Combine(folder, "bin"));

        SuiteMaker.Create(folder, "Order Desk", Path.Combine(folder, "bin", "OrderDesk.exe"), "Order Desk", JsonNode.Parse("""{ "id": "StatusText" }"""));
        var suite = Suite.Load(folder);

        Assert.Equal("bin/OrderDesk.exe", suite.App.Exe);
        Assert.Equal("statusText", suite.App.Status);
        Assert.True(suite.Macros.ContainsKey("waitForStatusBar"));
        Assert.Throws<InvalidOperationException>(() => SuiteMaker.Create(folder, "Again", "a.exe", "A", null));
    }

    [Fact]
    public void A_scope_lists_every_variable_it_can_see()
    {
        var outer = new VariableScope();
        outer.Set("exe", "App.exe");
        outer.Set("name", "outer");
        var inner = outer.Child();
        inner.Set("name", "inner");

        Assert.Equal([("exe", "App.exe"), ("name", "inner")], inner.All());
    }

    [Fact]
    public void A_stuck_step_gets_the_control_picked_for_it_in_elements_json_or_in_its_case()
    {
        var suite = Make(("01-a.json", "{\n  \"name\": \"A\",\n  \"steps\": [\n    { \"click\": \"addButton\" },\n    { \"click\": { \"button\": \"OK\" }, \"say\": \"Click OK\" }\n  ]\n}\n"));
        var item = suite.Cases[0];
        var onElement = new StuckStep(item.Steps[0]!.AsObject(), "click", item);
        var onStep = new StuckStep(item.Steps[1]!.AsObject(), "click", item);

        var toElement = TargetFixer.Where(suite, onElement)!;
        var toStep = TargetFixer.Where(suite, onStep)!;
        TargetFixer.Save(suite, onElement, toElement, JsonNode.Parse("""{ "id": "AddButton" }""")!);
        TargetFixer.Save(suite, onStep, toStep, JsonNode.Parse("""{ "id": "OkButton" }""")!);

        Assert.Equal("elements.json, as addButton", toElement.Shown);
        Assert.Equal("step 2 of 01-a.json", toStep.Shown);
        Assert.Equal("AddButton", suite.Elements["addButton"]["id"]!.GetValue<string>());
        Assert.Equal("OkButton", item.Steps[1]!["click"]!["id"]!.GetValue<string>());
        var reloaded = Suite.Load(suite.Folder);
        Assert.Equal("AddButton", reloaded.Elements["addButton"]["id"]!.GetValue<string>());
        Assert.Contains("""    { "click": { "id": "OkButton" }, "say": "Click OK" }""", File.ReadAllText(item.File));
        Assert.Null(TargetFixer.Where(suite, new StuckStep(new JsonObject { ["wait"] = 1 }, "wait", item)));
    }

    [Fact]
    public void The_runner_says_which_step_it_is_stuck_on_while_it_asks()
    {
        var suite = Make(("01-a.json", """{ "name": "A", "steps": [ { "click": "addButton" } ] }"""));
        StuckStep? seen = null;
        Runner? runner = null;
        var view = new FakeRunView { OnStuck = () => seen = runner!.StuckAt };
        runner = new Runner(suite, new FakeDriver(), new FakeMachine(), view, new RunControl(unattended: false) { TimeoutScale = 0.01 }, new Results(Path.Combine(_folder, "results"), console: null));

        runner.Run(suite.Cases);

        Assert.Equal("click", seen?.Kind);
        Assert.Equal("addButton", seen?.Target?.GetValue<string>());
        Assert.Null(runner.StuckAt);
    }

    [Fact]
    public void A_right_click_and_the_menu_items_after_it_make_one_menu_step_and_a_second_click_a_double_click()
    {
        var suite = Make(("01-a.json", """{ "name": "A", "steps": [] }"""));
        var draft = new CaseDraft { Name = "A" };
        var recorder = new Recorder(suite, draft, control => new TargetChoice(new JsonObject { ["name"] = control.Name }, control.Name, TargetQuality.Wording, ""));
        var row = new ControlFacts("ListItem", "ORD-1001");

        recorder.RightClicked(row);
        Assert.Empty(recorder.Clicked(new ControlFacts("MenuItem", "Export")));
        Assert.Empty(recorder.Clicked(new ControlFacts("MenuItem", "As CSV")));
        var steps = recorder.Clicked(new ControlFacts("Button", "Save"));
        recorder.Clicked(new ControlFacts("Button", "Again"));
        var doubled = recorder.Doubled(new ControlFacts("Button", "Again"));
        recorder.RightClicked(row);
        var right = recorder.MenuDone();

        Assert.Equal("""{ "menu": ["Export", "As CSV"], "on": { "name": "ORD-1001" }, "say": "Pick Export › As CSV from ORD-1001's menu" }""", JsonLine.Write(steps[0].Json));
        Assert.Equal("click", steps[1].Verb);
        Assert.Equal("double", doubled?.Json["how"]?.GetValue<string>());
        Assert.Equal("Double-click Again", doubled?.Text);
        Assert.Equal("right", right.Single().Json["how"]?.GetValue<string>());
        Assert.Null(recorder.Doubled(new ControlFacts("Button", "Save")));
        Assert.Equal("Quantity of row ORD-1001", TargetSuggester.Describe(JsonNode.Parse("""{ "cell": { "row": "ORD-1001", "column": "Quantity" } }""")!).Shown);
    }
}
