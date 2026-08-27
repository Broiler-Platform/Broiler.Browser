using System.Threading;
using Broiler.Layout.Diagnostics;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The contract <c>LayoutPassProfile</c> reads. Small surface, but two of these properties are
/// load-bearing for a measurement and one of them is the kind that fails silently.
/// </summary>
public sealed class LayoutPassCounterTests
{
    /// <summary>
    /// Off by default is the production state, and it is what makes the call site in
    /// <c>HtmlContainerInt.PerformLayout</c> free. A counter that defaulted on would put a static
    /// write on the hottest path in the engine and nothing would report it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Records_nothing_while_disabled()
    {
        LayoutPassCounter.Enabled = false;
        LayoutPassCounter.Reset();

        LayoutPassCounter.RecordCall();
        LayoutPassCounter.Record();
        LayoutPassCounter.Record();

        Assert.Equal(0, LayoutPassCounter.Calls);
        Assert.Equal(0, LayoutPassCounter.Passes);
    }

    [Fact(Timeout = 600000)]
    public void Counts_calls_and_passes_separately_while_enabled()
    {
        try
        {
            LayoutPassCounter.Reset();
            LayoutPassCounter.Enabled = true;

            // One PerformLayout that takes the shrink-to-fit branch: one call, two passes.
            LayoutPassCounter.RecordCall();
            LayoutPassCounter.Record();
            LayoutPassCounter.Record();

            Assert.Equal(1, LayoutPassCounter.Calls);
            Assert.Equal(2, LayoutPassCounter.Passes);
        }
        finally
        {
            LayoutPassCounter.Enabled = false;
        }
    }

    [Fact(Timeout = 600000)]
    public void Reset_clears_both_counters()
    {
        try
        {
            LayoutPassCounter.Enabled = true;
            LayoutPassCounter.RecordCall();
            LayoutPassCounter.Record();
            LayoutPassCounter.Reset();

            Assert.Equal(0, LayoutPassCounter.Calls);
            Assert.Equal(0, LayoutPassCounter.Passes);
        }
        finally
        {
            LayoutPassCounter.Enabled = false;
        }
    }

    /// <summary>
    /// The counters are <c>[ThreadStatic]</c>, so a thread reads its own renders and nobody else's.
    /// This is the property that fails silently: shared counters would still produce plausible
    /// numbers under the parallel style and raster passes items #12 and #5 introduced, just wrong
    /// ones, and no assertion on a single-threaded profile could catch it.
    /// </summary>
    /// <remarks>
    /// Deliberately synchronous, with a bare <see cref="Thread.Join()"/> rather than an
    /// <see langword="await"/>. An <see langword="await"/> here resumes the test on a thread-pool
    /// thread rather than the one that recorded, so the final assertions would read a *third*
    /// thread's counters and fail — which is the property under test doing its job, reported as a
    /// test failure. The counters are thread-affine, so the assertions have to stay on their thread.
    /// </remarks>
    [Fact(Timeout = 600000)]
    public void Counts_are_per_thread()
    {
        try
        {
            LayoutPassCounter.Enabled = true;
            LayoutPassCounter.Reset();
            LayoutPassCounter.RecordCall();
            LayoutPassCounter.Record();

            int otherCalls = -1, otherPasses = -1;

            var thread = new Thread(() =>
            {
                LayoutPassCounter.Reset();
                LayoutPassCounter.Record();
                LayoutPassCounter.Record();
                LayoutPassCounter.Record();
                otherCalls = LayoutPassCounter.Calls;
                otherPasses = LayoutPassCounter.Passes;
            });
            thread.Start();
            thread.Join();

            // The other thread saw only its own three passes and none of this thread's call.
            Assert.Equal(0, otherCalls);
            Assert.Equal(3, otherPasses);

            // And this thread is untouched by the other thread's Reset and its three passes.
            Assert.Equal(1, LayoutPassCounter.Calls);
            Assert.Equal(1, LayoutPassCounter.Passes);
        }
        finally
        {
            LayoutPassCounter.Enabled = false;
        }
    }
}
