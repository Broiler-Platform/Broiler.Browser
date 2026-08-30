namespace Broiler.Layout.Tests;

/// <summary>
/// <see cref="RefreshCoalescer"/> — the bound on <c>ILayoutEnvironment.RequestRefresh</c>.
/// </summary>
/// <remarks>
/// The path being guarded is layout → host → layout: a late image load asks the host to refresh
/// from inside a layout pass, and a host that lays out again in its handler re-enters the request.
/// The tests here pin the two properties that make that safe — the chain terminates, and a
/// relayout asked for mid-pass is not silently lost — plus the two the guard must <em>not</em>
/// break: separate containers and separate threads still refresh normally.
/// </remarks>
public class RefreshCoalescerTests
{
    [Fact]
    public void Request_RaisesOnce_WhenTheHandlerDoesNotReenter()
    {
        var coalescer = new RefreshCoalescer();
        var raises = new List<bool>();

        coalescer.Request(true, layout => raises.Add(layout));

        Assert.Equal([true], raises);
    }

    [Fact]
    public void Request_StopsAtMaxPasses_WhenTheHandlerAlwaysRequestsAgain()
    {
        // The regression: this handler is the one that used to recurse until the stack gave out.
        var coalescer = new RefreshCoalescer();
        var raises = 0;

        coalescer.Request(true, _ =>
        {
            raises++;
            coalescer.Request(true, _ => throw new InvalidOperationException(
                "a re-entrant request must be recorded, never raised"));
        });

        Assert.Equal(RefreshCoalescer.MaxPasses, raises);
    }

    [Fact]
    public void Request_StopsEarly_WhenTheHandlerStopsRequesting()
    {
        var coalescer = new RefreshCoalescer();
        var raises = 0;

        coalescer.Request(false, _ =>
        {
            raises++;
            if (raises == 1)
                coalescer.Request(false, _ => { });
        });

        Assert.Equal(2, raises);
    }

    [Fact]
    public void Request_CarriesTheRelayoutDemand_IntoTheFollowUpPass()
    {
        // A paint-only refresh whose handler uncovers a relayout: dropping the re-entrant request
        // instead of coalescing it would lose the relayout entirely.
        var coalescer = new RefreshCoalescer();
        var raises = new List<bool>();

        coalescer.Request(false, layout =>
        {
            raises.Add(layout);
            if (raises.Count == 1)
                coalescer.Request(true, _ => { });
        });

        Assert.Equal([false, true], raises);
    }

    [Fact]
    public void Request_KeepsTheRelayoutDemand_WhenOnlyOneOfSeveralRequestsAsksForIt()
    {
        var coalescer = new RefreshCoalescer();
        var raises = new List<bool>();

        coalescer.Request(false, layout =>
        {
            raises.Add(layout);
            if (raises.Count > 1)
                return;

            coalescer.Request(false, _ => { });
            coalescer.Request(true, _ => { });
            coalescer.Request(false, _ => { });
        });

        Assert.Equal([false, true], raises);
    }

    [Fact]
    public void Request_OnAnotherCoalescer_RaisesNormallyFromInsideAPass()
    {
        // A frame's container is a separate instance; refreshing it from the page's handler is
        // ordinary nesting, not re-entrancy.
        var page = new RefreshCoalescer();
        var frame = new RefreshCoalescer();
        var frameRaises = 0;

        page.Request(true, _ => frame.Request(true, _ => frameRaises++));

        Assert.Equal(1, frameRaises);
    }

    [Fact]
    public void Request_FoldsBackIntoTheOuterPass_WhenANestedCoalescerRefreshesIt()
    {
        // page → frame → page is the cycle that has to be caught through the nesting, not just at
        // the innermost level.
        var page = new RefreshCoalescer();
        var frame = new RefreshCoalescer();
        var pageRaises = new List<bool>();
        var frameRaises = 0;

        page.Request(false, layout =>
        {
            pageRaises.Add(layout);
            if (pageRaises.Count > 1)
                return;

            frame.Request(true, _ =>
            {
                frameRaises++;
                page.Request(true, _ => throw new InvalidOperationException(
                    "the outer pass owns this container; the request must fold into it"));
            });
        });

        Assert.Equal([false, true], pageRaises);
        Assert.Equal(1, frameRaises);
    }

    [Fact]
    public void Request_OnAnotherThread_IsNotSuppressedByAnInFlightPass()
    {
        // An asynchronous image completes on whichever thread finished it. Suppressing that
        // refresh because the UI thread happens to be servicing one would drop real work, which is
        // why the pass chain is per thread rather than per instance.
        var coalescer = new RefreshCoalescer();
        var passOpen = new ManualResetEventSlim();
        var otherFinished = new ManualResetEventSlim();
        var otherRaises = 0;

        var other = new Thread(() =>
        {
            Assert.True(passOpen.Wait(TimeSpan.FromSeconds(10)));
            coalescer.Request(true, _ => Interlocked.Increment(ref otherRaises));
            otherFinished.Set();
        })
        { IsBackground = true };
        other.Start();

        coalescer.Request(false, _ =>
        {
            passOpen.Set();
            Assert.True(otherFinished.Wait(TimeSpan.FromSeconds(10)));
        });

        Assert.True(other.Join(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, Volatile.Read(ref otherRaises));
    }

    [Fact]
    public void Request_LeavesNoPassOpen_WhenTheHandlerThrows()
    {
        // The raise callback reports its own handler errors, but an escape must still not strand
        // the chain — the next request has to raise rather than fold into a dead pass.
        var coalescer = new RefreshCoalescer();

        Assert.Throws<InvalidOperationException>(
            () => coalescer.Request(true, _ => throw new InvalidOperationException("boom")));

        var raises = 0;
        coalescer.Request(true, _ => raises++);

        Assert.Equal(1, raises);
    }

    [Fact]
    public void Request_RejectsANullRaiseCallback()
    {
        var coalescer = new RefreshCoalescer();

        Assert.Throws<ArgumentNullException>(() => coalescer.Request(true, null!));
    }
}
