namespace Broiler.Cli.Tests;

/// <summary>
/// Covers the digest that turns a capture's JavaScript failures into a work list: the grouping that
/// makes a log of thousands readable, and the extraction of the platform features the page asked for
/// and did not get.
/// </summary>
public sealed class JsErrorDigestTests
{
    [Fact(Timeout = 600000)]
    public void The_Same_Defect_From_Different_Scripts_Is_One_Group()
    {
        var groups = JsErrorDigest.Group(
        [
            "Script inline-0 failed: foo is not defined",
            "Script inline-7 failed: foo is not defined",
            "Script deferred-2 failed: foo is not defined",
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Count);
        // The example keeps a real message, not the blanked signature, so a reader sees a real case.
        Assert.Equal("Script inline-0 failed: foo is not defined", group.Example);
    }

    [Fact(Timeout = 600000)]
    public void Groups_Are_Ranked_By_How_Often_They_Happened()
    {
        var groups = JsErrorDigest.Group(
        [
            "rare failure",
            "common failure",
            "common failure",
            "common failure",
        ]);

        Assert.Equal(["common failure", "rare failure"], groups.Select(g => g.Example));
    }

    [Fact(Timeout = 600000)]
    public void Equally_Frequent_Groups_Keep_First_Seen_Order()
    {
        // A digest that reshuffles between runs of the same page cannot be diffed, so the ordering
        // among ties has to be stable rather than merely deterministic-within-a-run.
        var groups = JsErrorDigest.Group(["alpha failure", "beta failure", "gamma failure"]);

        Assert.Equal(["alpha failure", "beta failure", "gamma failure"], groups.Select(g => g.Example));
    }

    [Fact(Timeout = 600000)]
    public void A_Group_Names_Where_It_Was_First_Seen()
    {
        var groups = JsErrorDigest.Group(
            ["boom", "boom"],
            ["ScriptEngine.ExecuteInteractive", "console.error"]);

        Assert.Equal("ScriptEngine.ExecuteInteractive", Assert.Single(groups).FirstContext);
    }

    [Fact(Timeout = 600000)]
    public void Distinct_Defects_Are_Not_Merged()
    {
        var groups = JsErrorDigest.Group(["foo is not defined", "bar is not defined"]);

        Assert.Equal(2, groups.Count);
    }

    [Theory]
    // ReferenceError, as JSContext raises it.
    [InlineData("Script inline-0 failed: requestIdleCallback is not defined", "requestIdleCallback", "global")]
    // The wording JSUndefined/JSNull raise, which dominates a real Broiler log.
    [InlineData("Cannot get property isInputPending of undefined", "isInputPending", "property")]
    [InlineData("Cannot set property adsbygoogle of null", "adsbygoogle", "property")]
    // The wording JSValue.GetValue raises, shared with other engines.
    [InlineData("TypeError: Cannot read properties of undefined (reading 'startup')", "startup", "property")]
    [InlineData("Cannot read properties of null (reading 'observe')", "observe", "property")]
    // Callables.
    [InlineData("window.scheduler.postTask is not a function", "window.scheduler.postTask", "callable")]
    [InlineData("ResizeObserver is not a constructor", "ResizeObserver", "callable")]
    public void A_Failure_Names_The_Feature_It_Blames(string message, string expectedName, string expectedKind)
    {
        var api = Assert.Single(JsErrorDigest.MissingApis([message]));

        Assert.Equal(expectedName, api.Name);
        Assert.Equal(expectedKind, api.Kind);
    }

    [Fact(Timeout = 600000)]
    public void Features_Are_Ranked_By_How_Often_They_Were_Blamed()
    {
        var apis = JsErrorDigest.MissingApis(
        [
            "onceBlamed is not defined",
            "twiceBlamed is not defined",
            "twiceBlamed is not defined",
        ]);

        Assert.Equal(["twiceBlamed", "onceBlamed"], apis.Select(a => a.Name));
        Assert.Equal(2, apis[0].Count);
    }

    [Theory]
    [InlineData("undefined is not a function")]
    [InlineData("null is not a function")]
    [InlineData("Cannot get property 0 of undefined")]
    public void A_Message_That_Could_Not_Name_Its_Subject_Contributes_Nothing(string message)
    {
        // These are real and common — an engine that cannot describe the callee says "undefined is
        // not a function" — but "implement undefined" is not work anyone can do, and such rows would
        // outrank the ones that are.
        Assert.Empty(JsErrorDigest.MissingApis([message]));
    }

    [Fact(Timeout = 600000)]
    public void A_Message_Blaming_Nothing_Yields_Nothing()
    {
        Assert.Empty(JsErrorDigest.MissingApis(["Async work did not settle within 100 drain iterations"]));
    }
}
