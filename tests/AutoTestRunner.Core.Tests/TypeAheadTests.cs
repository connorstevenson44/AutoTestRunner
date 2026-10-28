using AutoTestRunner.Building;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>The list that drops down as ${ is typed in a box: when it opens, what it offers, and what picking one writes.</summary>
public class TypeAheadTests
{
    private static readonly Suggestion Order = new("orderId", "1042"), ReadFile = new("readFile", "a file's text", IsCall: true);

    [Theory]
    [InlineData("${", 2, 2, "")]
    [InlineData("Order ${ord", 11, 8, "ord")]
    [InlineData("${a} and ${or", 13, 11, "or")]
    [InlineData("${connection(re", 15, 13, "re")]
    [InlineData("${orderId}", 5, 2, "ord")]
    public void It_opens_on_the_name_being_typed_in_an_open_expression(string text, int caret, int start, string typed) =>
        Assert.Equal((start, typed), TypeAhead.Typing(text, caret));

    [Theory]
    [InlineData("plain words", 5)]
    [InlineData("${orderId} done", 15)]
    [InlineData("$${literal", 10)]
    [InlineData("${order.par", 11)]
    [InlineData("$", 1)]
    public void It_stays_shut_outside_an_open_expression(string text, int caret) =>
        Assert.Null(TypeAhead.Typing(text, caret));

    [Fact]
    public void Names_that_start_with_what_is_typed_come_first() =>
        Assert.Equal(["readFile", "orderId"], TypeAhead.Matching([Order, ReadFile], "r").Select(suggestion => suggestion.Name));

    [Theory]
    [InlineData("Order ${ord", 11, 8, "Order ${orderId}", 16)]
    [InlineData("${ord} now", 5, 2, "${orderId} now", 10)]
    [InlineData("${ord and ${b}", 5, 2, "${orderId} and ${b}", 10)]
    [InlineData("${readFile(ord", 14, 11, "${readFile(orderId", 18)]
    public void Picking_a_value_closes_it_once(string text, int caret, int start, string expected, int expectedCaret) =>
        Assert.Equal((expected, expectedCaret), TypeAhead.Put(text, start, caret, Order));

    [Theory]
    [InlineData("${re", 4, 2, "${readFile()}", 11)]
    [InlineData("${connection(re", 15, 13, "${connection(readFile()", 22)]
    [InlineData("${re(path)}", 4, 2, "${readFile(path)}", 11)]
    [InlineData("${re}", 4, 2, "${readFile()}", 11)]
    public void Picking_a_call_leaves_the_caret_between_its_brackets(string text, int caret, int start, string expected, int expectedCaret) =>
        Assert.Equal((expected, expectedCaret), TypeAhead.Put(text, start, caret, ReadFile));

    [Fact]
    public void The_calls_are_read_file_then_the_suites_helpers()
    {
        var helpers = new Dictionary<string, Helper> { ["logFile"] = Helper.From("logFile", new System.Text.Json.Nodes.JsonObject { ["regex"] = "Log: (.+)$" }) };
        Assert.Equal([("readFile", true), ("logFile", true)], TypeAhead.Calls(helpers).Select(call => (call.Name, call.IsCall)));
        Assert.Equal("regex Log: (.+)$", TypeAhead.Calls(helpers)[1].Detail);
    }
}
