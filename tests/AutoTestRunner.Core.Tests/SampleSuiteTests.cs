using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>
/// The sample suites are the demos people run first, so they must load cleanly, and the suite of script mistakes must
/// keep showing what validate catches.
/// </summary>
public class SampleSuiteTests
{
    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);

    [Theory]
    [InlineData("demo")]
    [InlineData("order-desk-feature-tour")]
    [InlineData("order-desk-failure-examples")]
    public void A_sample_suite_has_no_mistakes(string name)
    {
        var suite = Suite.Load(Sample(name));
        Assert.NotEmpty(suite.Cases);
    }

    [Fact]
    public void The_failure_examples_fail_by_what_they_do_not_by_mistakes_in_their_scripts()
    {
        var suite = Suite.Load(Sample("order-desk-failure-examples"));
        Assert.Equal(8, suite.Cases.Count);
        Assert.All(suite.Cases, item => Assert.Equal("startWithNoOrders", item.Steps[0]!["call"]!.GetValue<string>()));
    }

    [Fact]
    public void Validate_finds_every_mistake_in_the_script_mistakes_sample()
    {
        var problems = Assert.Throws<SuiteException>(() => Suite.Load(Sample("script-mistakes"))).Problems;
        string[] expected =
        [
            "03-not-json.json:",
            "suite.json: app.status names statusBar, which isn't in elements.json.",
            "01-steps-with-mistakes.json, step 2: has \"clik\", which isn't a step this runner knows.",
            "01-steps-with-mistakes.json, step 3: has click and type; a step does one thing.",
            "01-steps-with-mistakes.json, step 4: there is no element called saveButon in elements.json.",
            "01-steps-with-mistakes.json, step 5: type doesn't take \"value\".",
            "01-steps-with-mistakes.json, step 6: a selector has no \"label\".",
            "01-steps-with-mistakes.json, step 7: there is no macro called waitForStatus.",
            "01-steps-with-mistakes.json, step 8: waitForStatusBar has no parameter startWith.",
            "02-a-case-with-mistakes.json: needs open-the-app, and there is no case with that id.",
            "02-a-case-with-mistakes.json, step 1: a check is one of:",
            "02-a-case-with-mistakes.json, step 1: a tool is one of:",
        ];
        foreach (var start in expected)
            Assert.Contains(problems, problem => problem.StartsWith(start, StringComparison.Ordinal));
    }
}
