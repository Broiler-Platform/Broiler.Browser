using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Broiler.CSS;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Multithreading item #8's half of the P0-c contract: the image-prefetch worker is the first thread
/// in the repository that runs render-path work off the layout thread, so it is the first that has to
/// establish the ambient state and arm the assertion that says it did.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a test rather than a comment.</b> <c>multithreading.md</c> §3 records the debt in
/// exactly these terms — <i>the first item that creates a worker has to arm
/// <see cref="AmbientRenderState.EnforceOnThisThread"/> in the same place it calls
/// <see cref="AmbientRenderState.Establish"/>, or the instrumentation bought by P0-c goes unused</i>.
/// Every ambient slot is <c>[ThreadStatic]</c>, so a worker that skips the contract does not fail: it
/// reads a default and produces a plausible-looking wrong render. Nothing about that is visible in a
/// pixel comparison of documents whose images happen not to read a slot, which is every document in
/// the corpus. So the contract is asserted directly, on the real worker.
/// </para>
/// <para>
/// <b>And why the assertion has to be disarmed again.</b> These workers are pool threads. One that
/// went back to the pool still armed would make an ordinary unestablished read on some later,
/// unrelated piece of work throw — including in another test running concurrently in this same
/// assembly. That is the flaky-assertion-about-thread-safety failure the enforcement switch's own
/// remarks warn about, so leaving the switch as it was found is part of the contract and is checked
/// here.
/// </para>
/// </remarks>
public sealed class ImagePrefetchWorkerContractTests
{
    private const float ViewportWidth = 1024;
    private const float ViewportHeight = 768;

    /// <summary>
    /// Every load runs on a thread that has established all three slots, and none of them runs on a
    /// thread that has only inherited the caller's.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void EveryPrefetchWorkerEstablishesTheAmbientState()
    {
        var established = new ConcurrentBag<AmbientRenderState.Slots>();
        RunAll(8, () => established.Add(AmbientRenderState.EstablishedOnThisThread));

        Assert.Equal(8, established.Count);
        Assert.All(established, slots => Assert.Equal(AmbientRenderState.Slots.All, slots));
    }

    /// <summary>
    /// The viewport and quirks flag a worker sees are the document's, not whatever the pooled thread
    /// last rendered. This is the failure the assertion exists to catch, so it is worth checking that
    /// the values are right and not merely that a record was written.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void APrefetchWorkerSeesThisDocumentsViewportAndDocumentMode()
    {
        var quirks = DocumentModeContext.CurrentQuirksMode;
        try
        {
            DocumentModeContext.CurrentQuirksMode = true;

            // The viewport is read through a length rather than through a record, because a viewport
            // that was recorded but not applied is exactly the failure this slot has: every
            // vw/vh silently resolves to zero and the render looks like a layout bug.
            var seen = new ConcurrentBag<(double Vw, double Vh, bool Quirks)>();
            RunAll(4, () => seen.Add((
                CssLengthParser.ParseLength("50vw", 0, 16),
                CssLengthParser.ParseLength("25vh", 0, 16),
                DocumentModeContext.CurrentQuirksMode)));

            Assert.Equal(4, seen.Count);
            Assert.All(seen, s =>
            {
                Assert.Equal(ViewportWidth / 2, s.Vw, precision: 3);
                Assert.Equal(ViewportHeight / 4, s.Vh, precision: 3);
                Assert.True(s.Quirks);
            });
        }
        finally
        {
            DocumentModeContext.CurrentQuirksMode = quirks;
        }
    }

    /// <summary>
    /// A worker leaves the enforcement switch as it found it, so a pooled thread returning to the
    /// pool does not arm an assertion for whatever runs on it next.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void APrefetchWorkerLeavesTheEnforcementSwitchAsItFoundIt()
    {
        var threads = new ConcurrentDictionary<int, byte>();
        var armedInside = new ConcurrentBag<bool>();

        // Two rounds over the same pool: the second round is what can observe the first round's
        // threads, which is the only way a leaked switch shows up at all.
        for (var round = 0; round < 2; round++)
        {
            RunAll(4, () =>
            {
                threads.TryAdd(Environment.CurrentManagedThreadId, 0);
                armedInside.Add(AmbientRenderState.EnforceOnThisThread);
            });
        }

        // Armed inside every load — that is the contract's active half.
        Assert.All(armedInside, armed => Assert.True(armed));

        // And disarmed again by the time the load returns. Observed from plain pool workers, which
        // is how a leaked switch would reach unrelated work in the first place.
        var leaked = new ConcurrentBag<string>();
        RunAllWithoutContract(threads.Count, () =>
        {
            if (AmbientRenderState.EnforceOnThisThread)
                leaked.Add($"thread {Environment.CurrentManagedThreadId} came back armed");
        });

        Assert.Empty(leaked);
    }

    /// <summary>
    /// A worker clears its record, so the next document's loads cannot be satisfied by state this
    /// document established.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void APrefetchWorkerClearsItsRecordOnTheWayOut()
    {
        RunAll(4, () => { });

        var afterwards = new ConcurrentBag<AmbientRenderState.Slots>();
        RunAllWithoutContract(4, () => afterwards.Add(AmbientRenderState.EstablishedOnThisThread));

        Assert.All(afterwards, slots =>
            Assert.NotEqual(AmbientRenderState.Slots.All, slots));
    }

    /// <summary>
    /// A budget of one runs the loads inline on the calling thread — the pre-change path — so it
    /// neither establishes nor arms anything.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void ABudgetOfOneRunsInlineAndTouchesNoAmbientState()
    {
        var previous = ImagePrefetch.MaxDegreeOfParallelism;
        try
        {
            ImagePrefetch.MaxDegreeOfParallelism = 1;

            var callerThread = Environment.CurrentManagedThreadId;
            var threads = new List<int>();
            var armed = new List<bool>();
            var loads = new Action[4];
            for (var i = 0; i < loads.Length; i++)
            {
                loads[i] = () =>
                {
                    threads.Add(Environment.CurrentManagedThreadId);
                    armed.Add(AmbientRenderState.EnforceOnThisThread);
                };
            }

            ImagePrefetch.RunAll(loads, ViewportWidth, ViewportHeight, rootWritingMode: null);

            Assert.All(threads, id => Assert.Equal(callerThread, id));
            Assert.All(armed, a => Assert.False(a));
        }
        finally
        {
            ImagePrefetch.MaxDegreeOfParallelism = previous;
        }
    }

    /// <summary>
    /// Every load runs exactly once, and the call does not return until all of them have. The join is
    /// what makes a replaced box's intrinsic size independent of scheduling.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void RunAllRunsEveryLoadExactlyOnceAndJoins()
    {
        var counts = new int[16];
        var loads = new Action[counts.Length];
        for (var i = 0; i < loads.Length; i++)
        {
            var index = i;
            loads[i] = () =>
            {
                Thread.Sleep(1);
                Interlocked.Increment(ref counts[index]);
            };
        }

        var previous = ImagePrefetch.MaxDegreeOfParallelism;
        try
        {
            ImagePrefetch.MaxDegreeOfParallelism = 4;
            ImagePrefetch.RunAll(loads, ViewportWidth, ViewportHeight, rootWritingMode: null);
        }
        finally
        {
            ImagePrefetch.MaxDegreeOfParallelism = previous;
        }

        Assert.All(counts, count => Assert.Equal(1, count));
    }

    private static void RunAll(int loadCount, Action body)
    {
        var previous = ImagePrefetch.MaxDegreeOfParallelism;
        try
        {
            ImagePrefetch.MaxDegreeOfParallelism = 4;

            var loads = new Action[loadCount];
            for (var i = 0; i < loadCount; i++)
                loads[i] = body;

            ImagePrefetch.RunAll(loads, ViewportWidth, ViewportHeight, rootWritingMode: null);
        }
        finally
        {
            ImagePrefetch.MaxDegreeOfParallelism = previous;
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> on pool threads without establishing anything — the observer for
    /// "what did the prefetch leave behind on a thread it borrowed".
    /// </summary>
    private static void RunAllWithoutContract(int count, Action body)
    {
        var tasks = new System.Threading.Tasks.Task[Math.Max(1, count)];
        using var barrier = new Barrier(tasks.Length);
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = System.Threading.Tasks.Task.Run(() =>
            {
                // A barrier rather than a plain fan-out: without it the pool can satisfy every task
                // with one thread, and the observation would cover one of the threads the prefetch
                // borrowed rather than all of them.
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                body();
            });
        }

        System.Threading.Tasks.Task.WaitAll(tasks);
    }
}
