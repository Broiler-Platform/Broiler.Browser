# The load window, and where it is pumped

A page does not finish when its scripts return. It schedules timers, animation frames and
promise continuations that are still due a moment later, and the document only reaches the
state a user would call "loaded" once those have run. That interval is the **load window**.

Two questions decide whether a browser window renders a script-heavy page or freezes on it:
*who runs the load window*, and *when is it over*. Both were answered wrongly at first, and
`google.com` is the page that showed each.

## Who runs it

`ScriptEngine.ExecuteInteractive` drains microtasks and nothing else. Every timer the page
scheduled during load is left for the caller to step.

A host that steps them from its UI thread pays each callback batch **there** — inside the
WndProc, one batch per animation tick. A batch of a heavy page is measured in seconds, so the
window stops answering the message pump for exactly as long as the page's own script runs.
That is the freeze, and it is not a performance problem to be tuned; it is script execution on
the wrong thread.

`InteractiveSession.SettleLoadWindow` exists so a host can run the load window to a fixed point
**off the thread it paints on**. `BrowserApp.LoadUrlOnWorkerAsync` calls it on the load worker,
before the page is ever handed to the viewport.

The CLI never had the freeze. It drains bounded and off any message pump, so it rendered
`google.com` while the browser window hung on it — the two differed in where the work ran, not
in what the work was.

## When it is over

The load window is bounded on two axes, both in `DomBridgeRuntimeLimits`:

- **`AsyncDrainVirtualTimeBudgetMs` = 5000** — how far onto the virtual clock a drain follows
  scheduled work. Timers scheduled past this horizon belong to a page that kept running.
- **`AsyncDrainIterationLimit` = 1000** — the backstop for work that is due *immediately* and
  regenerates, which no time horizon can bound because it never moves the clock.

The predicate a pump drives itself on is `InteractiveSession.HasWorkDueInLoadWindow`, and the
distinction from `HasPendingWork` is the whole point:

| Question | Meaning | On a page holding a `setInterval` |
| --- | --- | --- |
| `HasPendingWork` | are any callbacks queued at all | **true forever**, by design |
| `HasWorkDueInLoadWindow` | is any work due within the horizon | goes false |

An interval always has a next tick. A pump stepping while the unbounded question is true never
stops — and since each step runs a callback batch and re-serialises the document, it does so at
whatever a batch happens to cost. Work scheduled past the horizon is *later*, not stuck, and
the page is loaded without it.

`BrowserApp`'s viewport drives the bounded question for its busy state, its 16 ms animation
tick, and `StopSession`.

## Painting while it settles

`SettleLoadWindow` takes an optional `onIntermediateDocument` callback, invoked after each
batch.

Settling silently is what made a page that animates *while* loading arrive on screen already
finished: Acid3 advances its score one test per `setTimeout`, so the whole count ran during the
settle, before the first paint, and the browser showed only the total. Reporting each batch lets
the host paint what it can keep up with.

The document is passed as a **thunk** rather than a string, because serialising is not free and
a host still painting the previous frame wants to skip this one. It pays only for the frames it
actually shows.

The callback runs on the settling thread, between batches. It must not run the page's script or
touch the DOM — read what it is handed and return.

## After the settle

If `HasWorkDueInLoadWindow` is false once the settle returns, the session is disposed rather
than carried forward. A page whose only remaining work is an interval's later ticks is finished
loading, and handing the viewport a live JavaScript context it is never going to step is a leak
dressed up as a feature.

If work *is* still due, the session goes to the viewport and `StepAnimation` runs it one batch
per tick. That path re-parses only when a step actually changed the document:

> A callback batch that touched no DOM — a timer that only reads, schedules, or measures —
> still returns the serialised document, and `google.com` runs many of those.

Re-parsing costs a full parse and layout, so it is compared against the last applied HTML first.
When the bounded question finally goes false, `StepAnimation` stops the session itself.

## The shape of the bug, if it comes back

A window that stops repainting while a page loads, and recovers when the page's script happens
to finish, is script running on the UI thread. Look for a drain or step called from the message
pump rather than from the load worker.

A window that never goes idle on a page that is visibly finished is the unbounded question:
something is driving a pump on "are callbacks queued" rather than "is work due".
