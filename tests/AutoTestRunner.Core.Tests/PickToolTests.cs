using System.Text.Json.Nodes;
using AutoTestRunner.Building;
using AutoTestRunner.Running;

namespace AutoTestRunner.Core.Tests;

/// <summary>The builder's pick tools: which controls each can be used on, and the steps they make.</summary>
public sealed class PickToolTests
{
    private static readonly TargetChoice ById = new(new JsonObject { ["id"] = "StatusText" }, "id StatusText", TargetQuality.Sturdy, "");
    private static readonly TargetChoice ByName = new(JsonValue.Create("statusText"), "statusText", TargetQuality.Sturdy, "");

    [Fact]
    public void ToolsForTextRefuseAControlThatShowsNone()
    {
        var blank = new ControlFacts("Image");
        foreach (var tool in new[] { PickTools.WaitText, PickTools.Keep, PickTools.CheckText })
            Assert.Equal("it shows no text.", tool.Refusal(blank, ById));
        Assert.Null(PickTools.WaitText.Refusal(new ControlFacts("Text", "Ready"), ById));
        // A box's text is what it holds.
        Assert.Null(PickTools.Keep.Refusal(new ControlFacts("Edit") { Value = "Ada" }, ById));
    }

    [Fact]
    public void ToolsForAnyControlTakeAnyControl()
    {
        var blank = new ControlFacts("Image");
        foreach (var tool in new[] { PickTools.Click, PickTools.Hover, PickTools.WaitFor, PickTools.WaitGone, PickTools.WaitEnabled, PickTools.CheckShows, PickTools.SetValue })
            Assert.Null(tool.Refusal(blank, ById));
    }

    [Fact]
    public void NameItRefusesAControlNamedAlready()
    {
        var status = new ControlFacts("Text", "Ready");
        Assert.Null(PickTools.Name.Refusal(status, ById));
        Assert.Equal("it already has a name in elements.json.", PickTools.Name.Refusal(status, ByName));
        var element = new TargetChoice(new JsonObject { ["element"] = "orderRow", ["with"] = new JsonObject { ["number"] = "1001" } }, "orderRow 1001", TargetQuality.Sturdy, "");
        Assert.NotNull(PickTools.Name.Refusal(status, element));
    }

    [Fact]
    public void ChecksOfTicksAndRowsTakeOnlyTheControlsTheyRead()
    {
        var ticked = Check("ticked");
        Assert.Null(ticked.Refusal(new ControlFacts("CheckBox", "Gift wrap"), ById));
        Assert.Equal("it isn't a tick box.", ticked.Refusal(new ControlFacts("Edit"), ById));
        var rows = Check("rows");
        Assert.Null(rows.Refusal(new ControlFacts("DataGrid"), ById));
        Assert.Equal("it isn't a list, a grid or a tree.", rows.Refusal(new ControlFacts("Button", "Add"), ById));
        Assert.Null(Check("enabled").Refusal(new ControlFacts("Button", "Add"), ById));
    }

    [Fact]
    public void EachControlCheckWithAFormHasATool()
    {
        Assert.Equal(["says", "enabled", "disabled", "ticked", "notTicked", "selected", "focused", "rows"], PickTools.Checks.Select(tool => tool.Check));
        Assert.Equal("Check it can be used…", Check("enabled").Title);
        Assert.Equal("check its number of rows", Check("rows").Doing);
        Assert.All(PickTools.Checks, tool => Assert.Equal(PickAction.Check, tool.Action));
    }

    [Fact]
    public void WaitUntilItCanBeUsedIsAStepThatPoints()
    {
        var step = StepMaker.WaitEnabled(ById, new ControlFacts("Button", "Deploy"));
        Assert.Equal("""{"waitEnabled":{"id":"StatusText"},"say":"Deploy can be used"}""", step.Json.ToJsonString());
        Assert.Equal(ActivityKind.Point, step.Kind);
    }

    private static PickTool Check(string key) => PickTools.Checks.Single(tool => tool.Check == key);
}
