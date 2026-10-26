using System.Text.Json.Nodes;
using AutoTestRunner.Building;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>
/// The builder's engine, against the feature tour's elements: how it names the controls the person used, the steps it
/// makes of what they did, and the files it writes, which must load as a suite with no mistakes.
/// </summary>
public sealed class BuildingTests : IDisposable
{
    private static readonly string Tour = Path.Combine(AppContext.BaseDirectory, "samples", "order-desk-feature-tour");

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "AutoTestRunner.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
            Directory.Delete(_temp, recursive: true);
    }

    private static readonly ControlFacts OrderRow = new("Button", "1001 Ada Lovelace", Id: "Row", HelpText: "Order 1001");

    private static VariableScope Globals(Suite suite) => Runner.Variables(suite, Path.GetTempPath(), new Dictionary<string, string>());

    // Every target finds one control on screen, except those named here, which find as many as given.
    private static Func<JsonNode, int> Count(params (string Target, int Count)[] counts) =>
        json => counts.FirstOrDefault(pair => pair.Target == JsonLine.Write(json)) is { Target: not null } found ? found.Count : 1;

    private static TargetChoice Name(Suite suite, ControlFacts control, Func<JsonNode, int>? count = null) =>
        TargetSuggester.Suggest(control, suite.Elements, Globals(suite), count ?? Count());

    [Fact]
    public void The_suites_beside_the_runner_are_found_and_nothing_inside_them_or_in_results()
    {
        foreach (var folder in new[] { "netdeploy", "order-desk", @"order-desk/cases", "results/run/old", @"tests/ui-tests/deep", ".git/suite" })
        {
            Directory.CreateDirectory(Path.Combine(_temp, folder));
            File.WriteAllText(Path.Combine(_temp, folder, "suite.json"), "{}");
        }
        Directory.CreateDirectory(Path.Combine(_temp, "autotestrunner"));

        var places = SuiteFinder.Places(Path.Combine(_temp, "autotestrunner"), Path.Combine(_temp, "autotestrunner"));
        var found = SuiteFinder.Find(places).Select(folder => Path.GetRelativePath(_temp, folder).Replace('\\', '/')).ToList();

        Assert.Equal(["netdeploy", "order-desk", "tests/ui-tests/deep"], found);
    }

    [Fact]
    public void A_suite_with_mistakes_is_listed_with_them()
    {
        var found = FoundSuite.Load(Path.Combine(AppContext.BaseDirectory, "samples", "script-mistakes"));
        Assert.Null(found.Suite);
        Assert.Equal("script-mistakes", found.Name);
        Assert.NotEmpty(found.Problems);
    }

    [Fact]
    public void A_control_in_elements_json_is_found_by_its_name()
    {
        var suite = Suite.Load(Tour);
        Assert.Equal("addOrder", Name(suite, new ControlFacts("Button", "Add order", Id: "AddButton")).Shown);
        Assert.Equal("customer", Name(suite, new ControlFacts("Edit", "Customer", Id: "CustomerBox")).Shown);

        var choice = Name(suite, new ControlFacts("CheckBox", "Gift wrap", Id: "GiftWrapBox"));
        Assert.Equal(TargetQuality.Sturdy, choice.Quality);
        Assert.Equal("\"giftWrap\"", JsonLine.Write(choice.Json));
    }

    [Fact]
    public void An_element_with_parameters_is_filled_in_from_the_control_and_the_row_around_it()
    {
        var suite = Suite.Load(Tour);
        Assert.Equal("""{ "element": "orderRow", "with": { "number": "1001" } }""", JsonLine.Write(Name(suite, OrderRow).Json));

        var delete = new ControlFacts("Button", "Delete", HelpText: "Delete this order") { Ancestors = [new("Pane"), OrderRow] };
        var choice = Name(suite, delete);
        Assert.Equal("""{ "element": "orderRowButton", "with": { "tip": "Delete this order", "number": "1001" } }""", JsonLine.Write(choice.Json));
    }

    [Fact]
    public void An_element_that_is_only_parameters_doesnt_match_everything_of_its_type()
    {
        var suite = Suite.Load(Tour);
        var matches = ElementMatcher.Matches(new ControlFacts("Text", "Ready."), suite.Elements, Globals(suite));
        Assert.DoesNotContain(matches, match => match.Name == "catalogueProduct");
    }

    [Fact]
    public void Without_an_element_it_takes_the_id_then_the_words_then_the_row_it_is_in()
    {
        var suite = Suite.Load(Tour);
        var clear = Name(suite, new ControlFacts("Button", "Clear", Id: "ClearButton"));
        Assert.Equal(("""{ "id": "ClearButton" }""", TargetQuality.Sturdy), (JsonLine.Write(clear.Json), clear.Quality));

        var words = Name(suite, new ControlFacts("Button", "Clear", Id: "ClearButton"), Count(("""{ "id": "ClearButton" }""", 3)));
        Assert.Equal(("""{ "button": "Clear" }""", TargetQuality.Wording), (JsonLine.Write(words.Json), words.Quality));

        // Every row has a Ship button: the one in order 1001's row.
        var ship = new ControlFacts("Button", "Ship") { Ancestors = [new("Pane"), OrderRow] };
        var inRow = Name(suite, ship, Count(("""{ "button": "Ship" }""", 4)));
        Assert.Equal("""{ "button": "Ship", "within": { "element": "orderRow", "with": { "number": "1001" } } }""", JsonLine.Write(inRow.Json));

        var many = Name(suite, new ControlFacts("Text", "£12.00"), Count(("""{ "text": "£12.00" }""", 5)));
        Assert.Equal(TargetQuality.Ambiguous, many.Quality);
        Assert.StartsWith("5 controls match this", many.Why);
    }

    [Fact]
    public void A_control_in_another_window_is_found_inside_it()
    {
        var suite = Suite.Load(Tour);
        var skip = new ControlFacts("Button", "Skip this order") { Ancestors = [new("Pane"), new("Window", "Import order")] };
        var choice = Name(suite, skip, Count(("""{ "button": "Skip this order" }""", 0)));
        Assert.Equal("""{ "button": "Skip this order", "within": { "window": "Import order" } }""", JsonLine.Write(choice.Json));
    }

    [Fact]
    public void A_recording_reads_as_the_steps_a_person_would_write()
    {
        var suite = Suite.Load(Tour);
        var draft = new CaseDraft { Name = "Take an order for Ada" };
        var recorder = new Recorder(suite, draft, control => Name(suite, control));

        var customer = new ControlFacts("Edit", "Customer", Id: "CustomerBox") { Value = "" };
        recorder.Entered(customer);
        recorder.Left("Ada Lovelace");
        var coffee = new ControlFacts("ListItem", "Coffee beans") { Ancestors = [new("ComboBox", "Product", Id: "ProductBox")] };
        recorder.Clicked(new ControlFacts("Text", "Coffee beans") { Ancestors = [coffee with { Ancestors = [] }, .. coffee.Ancestors] });
        recorder.Clicked(new ControlFacts("CheckBox", "Gift wrap", Id: "GiftWrapBox") { Checked = true });
        recorder.Clicked(new ControlFacts("CheckBox", "Express delivery", Id: "ExpressBox") { Checked = false });
        recorder.Clicked(new ControlFacts("Text", "Add order") { Ancestors = [new("Button", "Add order", Id: "AddButton")] });
        recorder.StatusSaid("Added order 1001 for Ada Lovelace, £26.00.");
        recorder.StatusSaid("Ready.");
        recorder.Clicked(new ControlFacts("TabItem", "Catalogue"));
        recorder.Clicked(new ControlFacts("Button", "Yes", Id: "6") { Dialog = new DialogFacts("Delete order", "Delete 1 order? It can't be undone.") });

        string[] expected =
        [
            """{ "type": "customer", "text": "Ada Lovelace", "say": "Type Ada Lovelace in Customer" }""",
            """{ "choose": "product", "option": "Coffee beans", "say": "Pick Coffee beans from Product" }""",
            """{ "tick": "giftWrap", "say": "Tick Gift wrap" }""",
            """{ "tick": "express", "checked": false, "say": "Clear Express delivery" }""",
            """{ "click": "addOrder", "say": "Click Add order" }""",
            """{ "call": "waitForStatusBar", "with": { "startsWith": "Added order 1001 for Ada Lovelace" } }""",
            """{ "tab": "Catalogue", "say": "Open the Catalogue tab" }""",
            """{ "answerDialog": "Delete order", "button": "Yes", "expect": "Delete 1 order?", "say": "Click Yes" }""",
        ];
        Assert.Equal(expected, draft.Steps.Select(step => JsonLine.Write(step.Json)));
        Assert.Equal(ActivityKind.Choose, draft.Steps[1].Kind);
    }

    [Fact]
    public void Leaving_a_box_unchanged_or_clicking_into_one_adds_nothing()
    {
        var suite = Suite.Load(Tour);
        var draft = new CaseDraft();
        var recorder = new Recorder(suite, draft, control => Name(suite, control));
        recorder.Clicked(new ControlFacts("Edit", "Quantity", Id: "QuantityBox"));
        recorder.Entered(new ControlFacts("Edit", "Quantity", Id: "QuantityBox") { Value = "1" });
        recorder.Left("1");
        recorder.Clicked(new ControlFacts("Pane"));
        Assert.Empty(draft.Steps);
    }

    [Fact]
    public void A_new_case_is_written_in_the_suites_style_and_loads_with_no_mistakes()
    {
        var suite = CopyOfTour();
        var draft = new CaseDraft { Name = "Take an order for Ada", CheckpointTitle = "Ada's order was added" };
        draft.Steps.Add(StepMaker.StartApp(suite));
        var total = new ControlFacts("Text", "Total: £26.00", Id: "TotalText");
        draft.Steps.Add(StepMaker.Keep(Name(suite, total), total, "total"));
        draft.Checks.Add(new JsonObject { ["equals"] = "${total}", ["to"] = "Total: £26.00" });
        draft.Checks.Add(new JsonObject { ["shows"] = Name(suite, OrderRow).Json.DeepClone() });
        draft.Look.Add("The order is in the list on the right.");

        var path = CaseWriter.Save(suite, draft);

        Assert.Equal("09-take-an-order-for-ada.json", Path.GetFileName(path));
        var text = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Equal("""
            {
              "$schema": "../../../schema/case.schema.json",
              "name": "Take an order for Ada",
              "steps": [
                { "startApp": {}, "say": "Start Order Desk" },
                { "find": "formTotal", "as": "total", "say": "Keep what it says as total: ${total}" },
                { "checkpoint": "Ada's order was added",
                  "checks": [
                    { "equals": "${total}", "to": "Total: £26.00" },
                    { "shows": { "element": "orderRow", "with": { "number": "1001" } } }
                  ],
                  "look": ["The order is in the list on the right."] }
              ]
            }

            """, text);
        Assert.Equal(9, Suite.Load(suite.Folder).Cases.Count);
    }

    [Fact]
    public void Steps_added_to_a_case_go_before_its_checkpoint_and_leave_the_rest_as_it_was()
    {
        var suite = CopyOfTour();
        var existing = suite.Cases[0];
        var before = File.ReadAllText(existing.File);
        var draft = CaseDraft.Load(existing);
        var express = new ControlFacts("CheckBox", "Express delivery", Id: "ExpressBox") { Checked = true };
        draft.Steps.Add(StepMaker.Tick(Name(suite, express), express, true));
        draft.Checks.Add(new JsonObject { ["shows"] = "addOrder" });
        draft.Look.Add("Express delivery is ticked.");

        CaseWriter.Save(suite, draft);

        var after = File.ReadAllText(existing.File);
        var reloaded = Suite.Load(suite.Folder).Cases[0];
        Assert.Equal(existing.Steps.Count + 1, reloaded.Steps.Count);
        Assert.Equal("Tick Express delivery", reloaded.Steps[^2]!["say"]!.GetValue<string>());
        var checkpoint = reloaded.Steps[^1]!.AsObject();
        Assert.Equal("""{ "shows": "addOrder" }""", JsonLine.Write(checkpoint["checks"]!.AsArray()[^1]));
        Assert.Equal("Express delivery is ticked.", checkpoint["look"]!.AsArray()[^1]!.GetValue<string>());
        // Only the new lines were added: the case's own text is all still there, in order.
        var kept = before.Split('\n').Where(line => line.Trim().Length > 0).ToList();
        var lines = after.Split('\n').ToList();
        var at = 0;
        foreach (var line in kept)
            at = lines.FindIndex(at, candidate => candidate.TrimEnd(',', '\r', ' ') == line.TrimEnd(',', '\r', ' ')) + 1;
        Assert.True(at > 0, "The case's own lines were changed.");
    }

    [Fact]
    public void A_case_opened_to_change_it_has_its_steps_but_not_its_checkpoint_none_of_them_played()
    {
        var suite = Suite.Load(Tour);
        var draft = CaseDraft.Load(suite.Cases[0]);

        Assert.Equal(suite.Cases[0].Steps.Count - 1, draft.Steps.Count);
        Assert.All(draft.Steps, step => Assert.Equal(DraftStepState.NotPlayed, step.State));
        Assert.Equal(["take-an-order"], suite.Cases[1].Needs);
        Assert.Equal("customer", draft.Steps[2].Target!.Shown);
        Assert.Equal(TargetQuality.Sturdy, draft.Steps[2].Target!.Quality);
        Assert.Equal("orderRow 1001", draft.Steps[^1].Target!.Shown);
    }

    [Fact]
    public void Changing_a_case_from_a_step_keeps_the_steps_before_it_as_written_and_its_checkpoint()
    {
        var suite = CopyOfTour();
        var existing = suite.Cases[0];
        var before = File.ReadAllText(existing.File);
        var draft = CaseDraft.Load(existing);
        // Edit from step 5: the steps after it go, and new ones are recorded.
        lock (draft.Steps)
            draft.Steps.RemoveRange(5, draft.Steps.Count - 5);
        var three = new ControlFacts("Edit", "Quantity", Id: "QuantityBox");
        draft.Steps.Add(StepMaker.Type(Name(suite, three), three, "3"));
        draft.Steps[1].Text = "The bar at the bottom says what the app is doing: ${ready}";

        CaseWriter.Save(suite, draft);

        var text = File.ReadAllText(existing.File);
        var reloaded = Suite.Load(suite.Folder).Cases[0];
        Assert.Equal(7, reloaded.Steps.Count);
        Assert.Equal("""{ "find": "statusText", "as": "ready", "what": "the bar at the bottom", "say": "The bar at the bottom says what the app is doing: ${ready}" }""", JsonLine.Write(reloaded.Steps[1]));
        Assert.Equal("""{ "type": "quantity", "text": "3", "say": "Type 3 in Quantity" }""", JsonLine.Write(reloaded.Steps[5]));
        Assert.Equal("Ada's order was added and saved", reloaded.Steps[^1]!["checkpoint"]!.GetValue<string>());
        // The first step is as it was written, and so is everything from the checkpoint on.
        var first = before.Split('\n').First(line => line.Contains("startApp", StringComparison.Ordinal));
        Assert.Contains(first, text, StringComparison.Ordinal);
        Assert.EndsWith(before[before.IndexOf("{ \"checkpoint\"", StringComparison.Ordinal)..], text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_case_with_every_step_taken_out_keeps_its_checkpoint()
    {
        var suite = CopyOfTour();
        var draft = CaseDraft.Load(suite.Cases[0]);
        draft.Steps.Clear();

        CaseWriter.Save(suite, draft);

        var reloaded = Suite.Load(suite.Folder).Cases[0];
        Assert.Single(reloaded.Steps);
        Assert.NotNull(reloaded.Steps[0]!["checkpoint"]);
    }

    [Fact]
    public void The_cases_that_carry_on_from_a_changed_case_can_be_marked_and_the_marks_cleared()
    {
        var suite = CopyOfTour();
        var first = suite.Cases[0];
        var after = suite.CarryingOn(first);
        Assert.Contains(suite.Cases[1], after);
        Assert.DoesNotContain(first, after);
        Assert.All(after, item => Assert.Contains(first, suite.Leading(item)));

        var draft = CaseDraft.Load(first);
        draft.Review[suite.Cases[1].File] = "Case 1 changed from step 5.";
        File.WriteAllText(first.File, CaseWriter.Flag(File.ReadAllText(first.File), "Left from before."));
        CaseWriter.Save(suite, draft);

        var reloaded = Suite.Load(suite.Folder);
        Assert.Null(reloaded.Cases[0].Review);
        Assert.Equal("Case 1 changed from step 5.", reloaded.Cases[1].Review);
        CaseWriter.ClearReview(reloaded.Cases[1]);
        Assert.Null(Suite.Load(suite.Folder).Cases[1].Review);
        Assert.Equal(File.ReadAllText(Path.Combine(Tour, "cases", Path.GetFileName(suite.Cases[1].File))), File.ReadAllText(suite.Cases[1].File));
    }

    [Fact]
    public void A_watched_control_gets_a_wait_through_its_waitFor_macro_and_a_check_when_asked()
    {
        var suite = CopyOfTour();
        var macro = """{ "params": { "startsWith": {} }, "steps": [ { "waitText": "formProblem", "startsWith": "${startsWith}", "as": "problem" } ], "returns": "problem" }""";
        File.WriteAllText(Path.Combine(suite.Folder, "macros", "waitForFormProblem.json"), macro);
        suite = Suite.Load(suite.Folder);
        Assert.Contains(new WatchHook("formProblem", "waitForFormProblem"), suite.Watched());
        Assert.Contains(new WatchHook("statusText", "waitForStatusBar"), suite.Watched());

        var draft = new CaseDraft { Name = "Watch the form" };
        draft.NewWatch.Add(new WatchHook("formTotal", Check: true));
        var recorder = new Recorder(suite, draft, control => Name(suite, control));
        recorder.Clicked(new ControlFacts("CheckBox", "Gift wrap", Id: "GiftWrapBox") { Checked = true });
        var problem = recorder.Watched.Single(hook => hook.Element == "formProblem");
        var total = recorder.Watched.Single(hook => hook.Element == "formTotal");
        recorder.Changed(problem, "Pick a product.");
        recorder.Changed(problem, "Pick a product, please.");
        recorder.Changed(total, "Total: £14.00");

        string[] expected =
        [
            """{ "tick": "giftWrap", "say": "Tick Gift wrap" }""",
            """{ "call": "waitForFormProblem", "with": { "startsWith": "Pick a product." } }""",
            """{ "waitText": "formTotal", "startsWith": "Total", "as": "formTotal" }""",
        ];
        Assert.Equal(expected, draft.Steps.Select(step => JsonLine.Write(step.Json)));
        Assert.Equal("""{ "equals": "${formTotal}", "to": "Total: £14.00", "label": "formTotal said \"Total: £14.00\"" }""", JsonLine.Write(Assert.Single(draft.Checks)));
    }

    [Fact]
    public void A_click_with_a_hook_is_followed_by_its_steps_instead_of_a_wait_for_the_status()
    {
        var suite = CopyOfTour();
        var draft = new CaseDraft { Name = "Hooks" };
        draft.NewAfterClick.Add(new ClickHook("addOrder", "waitForStatusBar", new JsonObject { ["startsWith"] = "Added order" }, 0.5));
        draft.NewWatch.Add(new WatchHook("formTotal"));
        CaseWriter.AddHooks(suite, draft.NewWatch, draft.NewAfterClick);
        suite = Suite.Load(suite.Folder);
        Assert.Equal([new WatchHook("formTotal")], suite.Watch);
        Assert.Equal("addOrder", Assert.Single(suite.AfterClick).Element);

        var recorder = new Recorder(suite, new CaseDraft(), control => Name(suite, control));
        var steps = recorder.Clicked(new ControlFacts("Button", "Add order", Id: "AddButton"));
        recorder.StatusSaid("Added order 1001 for Ada Lovelace, £26.00.");

        Assert.Equal(
            ["""{ "click": "addOrder", "say": "Click Add order" }""", """{ "wait": 0.5 }""", """{ "call": "waitForStatusBar", "with": { "startsWith": "Added order" } }"""],
            steps.Select(step => JsonLine.Write(step.Json)));

        // A second hook goes on the end of the list that is there now.
        CaseWriter.AddHooks(suite, [new WatchHook("formProblem", Check: true)], []);
        Assert.Equal(2, Suite.Load(suite.Folder).Watch.Count);
    }

    [Fact]
    public void A_script_step_names_a_ps1_file_in_the_suite()
    {
        var suite = CopyOfTour();
        var step = StepMaker.Script(@"tools\Reset-OrderDesk.ps1");
        Assert.Equal("""{ "script": "tools/Reset-OrderDesk.ps1", "say": "Run Reset-OrderDesk.ps1" }""", JsonLine.Write(step.Json));
        Assert.Equal("tools/Reset-OrderDesk.ps1", step.Script);

        var draft = new CaseDraft { Name = "Scripts" };
        draft.Steps.Add(step);
        draft.Steps.Add(StepMaker.Script("tools/Missing.ps1"));
        CaseWriter.Save(suite, draft);
        var problem = Assert.Single(Assert.Throws<SuiteException>(() => Suite.Load(suite.Folder)).Problems);
        Assert.Contains("there is no script tools/Missing.ps1", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_control_goes_into_elements_json_and_its_steps_use_the_name()
    {
        var suite = CopyOfTour();
        var draft = new CaseDraft { Name = "Clear the form" };
        var clear = new ControlFacts("Button", "Clear", Id: "ClearButton");
        var choice = Name(suite, clear);
        draft.Steps.Add(StepMaker.Click(choice, clear));

        var named = draft.NameControl(choice, "clearForm", suite.Elements);
        CaseWriter.Save(suite, draft);

        Assert.Equal("clearForm", named.Shown);
        Assert.Equal("""{ "click": "clearForm", "say": "Click Clear" }""", JsonLine.Write(draft.Steps[0].Json));
        var reloaded = Suite.Load(suite.Folder);
        Assert.Equal("""{ "id": "ClearButton" }""", JsonLine.Write(reloaded.Elements["clearForm"]));
        Assert.Equal("clearForm", reloaded.Elements.Keys.Last());
    }

    [Theory]
    [InlineData("Added order 1001 for Ada Lovelace, £26.00.", "Added order 1001 for Ada Lovelace")]
    [InlineData("Deploy finished. Log: C:\\logs\\1.log", "Deploy finished.")]
    [InlineData("Exported 3 orders: C:\\out\\orders.csv", "Exported 3 orders")]
    [InlineData("Connecting to the warehouse and sending every order now", "Connecting to the warehouse and sending")]
    public void A_wait_for_the_status_text_takes_its_first_words(string status, string expected) =>
        Assert.Equal(expected, StepMaker.StartOf(status));

    private Suite CopyOfTour()
    {
        // Two folders down, as in samples/, so the $schema paths stay right.
        var folder = Path.Combine(_temp, "samples", "tour");
        foreach (var file in Directory.GetFiles(Tour, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(folder, Path.GetRelativePath(Tour, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
        return Suite.Load(folder);
    }
}
