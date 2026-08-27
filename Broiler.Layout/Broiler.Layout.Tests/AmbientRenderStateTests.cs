using System;
using System.Threading.Tasks;
using Broiler.Layout;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Multithreading roadmap P0-c: the debug-mode assertion that fires when a thread-static ambient
/// value is read before being set on that thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>These run the assertion on a real second thread, not by clearing the record on this one.</b>
/// The whole failure mode is per-thread — a value the main thread set is not a value a worker thread
/// has — so a test that simulated it with <see cref="AmbientRenderState.ClearThreadRecord"/> would
/// pass against an implementation that stored the record in a plain <c>static</c> and would
/// therefore prove nothing about the thing that actually breaks.
/// </para>
/// <para>
/// <b>Nothing here touches state the rest of the assembly can see.</b>
/// <see cref="AmbientRenderState.EnforceOnThisThread"/> is thread-scoped and is armed inside the
/// worker, so a case cannot arm the assertion for a layout test running beside it — which xunit,
/// running an assembly's test classes concurrently, would otherwise make a matter of timing.
/// </para>
/// </remarks>
public sealed class AmbientRenderStateTests
{
    /// <summary>
    /// Runs <paramref name="body"/> on a thread that has established nothing, with the assertion
    /// armed for that thread only, and returns whatever it threw.
    /// </summary>
    private static Exception? OnAFreshArmedThread(Action body)
    {
        Exception? thrown = null;
        var thread = new System.Threading.Thread(() =>
        {
            AmbientRenderState.EnforceOnThisThread = true;
            try
            {
                body();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the worker thread did not finish");
        return thrown;
    }

#if DEBUG
    [Fact(Timeout = 600000)]
    public void AnUnestablishedReadOnAWorkerThreadAssertsWhenEnforcementIsOn()
    {
        var thrown = OnAFreshArmedThread(() => _ = DocumentModeContext.CurrentQuirksMode);

        var failure = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains(nameof(DocumentModeContext.CurrentQuirksMode), failure.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void EstablishSatisfiesEverySlotOnTheThreadThatCallsIt()
    {
        var established = AmbientRenderState.Slots.None;

        var thrown = OnAFreshArmedThread(() =>
        {
            AmbientRenderState.Establish(1280, 1024, "horizontal-tb", quirksMode: false);
            established = AmbientRenderState.EstablishedOnThisThread;

            // One reader per slot the assertion guards.
            _ = DocumentModeContext.CurrentQuirksMode;
            _ = SvgFilterTable.TryGetFlood("nothing", out _);
            _ = SvgClipPathTable.TryGet("nothing", out _);
        });

        Assert.Null(thrown);
        Assert.Equal(AmbientRenderState.Slots.All, established);
    }

    [Fact(Timeout = 600000)]
    public void OneSlotDoesNotVouchForAnother()
    {
        // The reason the record is per-slot rather than a single "ready" bit: a thread that reset
        // the SVG tables has not thereby learned the document's quirks mode, and an assertion that
        // let it through would miss the mistake it exists to catch.
        var thrown = OnAFreshArmedThread(() =>
        {
            SvgFilterTable.ResetForThread();
            _ = DocumentModeContext.CurrentQuirksMode;
        });

        Assert.IsType<InvalidOperationException>(thrown);
    }
#endif

    [Fact(Timeout = 600000)]
    public void EnforcementIsOffByDefaultSoTheSingleThreadedPathIsUnchanged()
    {
        // Not a formality. `DocumentModeContext.CurrentQuirksMode` is never written on the
        // HTML-string render path, so an always-on assertion would fail every render in the
        // repository for a defect that single-threaded execution makes unreachable. The default is
        // load-bearing and is asserted so a later change cannot flip it without noticing.
        Assert.False(AmbientRenderState.EnforceOnThisThread);

        Exception? thrown = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                _ = DocumentModeContext.CurrentQuirksMode;
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the worker thread did not finish");
        Assert.Null(thrown);
    }
}
