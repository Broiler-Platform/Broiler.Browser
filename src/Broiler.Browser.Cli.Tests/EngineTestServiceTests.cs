namespace Broiler.Cli.Tests;

/// <summary>
/// <c>--test-engines</c> asks the engine the browser composes, not one of its own.
/// </summary>
[Collection(CaptureCollection.Name)]
public sealed class EngineTestServiceTests
{
    [Fact(Timeout = 600000)]
    public void Every_Engine_Passes()
    {
        Assert.All(new EngineTestService().RunAll(), result => Assert.True(result.Passed, result.Error));
    }

    /// <summary>
    /// Under <c>Debug-VM</c>/<c>Release-VM</c> the browser runs script-only work on the Broiler.VM
    /// JavaScript profile, and that is the engine this reports.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void The_JavaScript_Result_Names_The_Engine_This_Build_Composes()
    {
#if BROILER_VM_JS
        const string Expected = "Broiler.VM";
#else
        const string Expected = "Broiler.JS";
#endif
        Assert.Equal(Expected, new EngineTestService().TestJavaScript().EngineName);
    }
}
