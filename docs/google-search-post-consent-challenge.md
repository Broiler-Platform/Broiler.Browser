# Google Search, after the consent page

Google Search is the browser's hardest single page, and it is hard in a specific way: **it
almost never fails where it broke.** Its anti-abuse code is an interpreter running its own
bytecode, so one wrong value early derails the decode for the rest of the page and surfaces
much later as a `TypeError` that names nothing recognisable.

Every entry below was found by chasing such an error back to a missing or wrong binding. They
are recorded here because the next one will look the same, and the instinct to read the
reported error as the bug is the thing to resist.

## Prerequisite: getting past consent at all

In the EU the search is behind a cookie-consent interstitial, and its form submission answered
`POST https://consent.google.de/save` with `400 Bad Request` — so the search never happened and
nothing below was reachable.

The cause was in the request, not the page. `StringContent`'s constructor already sets a
`Content-Type` (`text/plain; charset=utf-8` for the UTF-8 overload), and
`TryAddWithoutValidation` **appends** to a header rather than replacing it. The form therefore
went out as:

```
Content-Type: text/plain; charset=utf-8, application/x-www-form-urlencoded
```

A server reading the first value sees `text/plain`, never decodes the body, and rejects the
submission. `PageLoader.WithContentType` now removes the default before setting the real one;
`PageLoaderContentTypeTests` guards it.

## What the bot-check VM needed

### `location.replace` — a missing method, not a missing property

The URL components were present and the navigation methods were not, so `location.replace(url)`
was `undefined is not a function` — a `TypeError` **at the call**, which aborts the rest of the
caller rather than the one line. Google reaches it on the path its bootstrap takes when
`window.prs` is absent: `W(a)` ends in `b !== void 0 && a.replace(b)` over `a = location`.

The methods exist now and **do not navigate, and do not pretend to**. Each records what was
asked for and returns, which is what a browser blocking a navigation does too, and unlike a
throw it leaves the calling script running. The URL components are deliberately not updated to
the target — the document did not change.

The one exception is a **fragment navigation**, because it is not a load: a target resolving to
this document's URL differing only after the `#` moves `location.hash` and `location.href` and
fires `hashchange`, per HTML §7.4.5. All four spellings — `location.hash = x`,
`location.href = "#x"`, `assign("#x")` and `replace("#x")` — take that one path. They did not
before: `hash` was a writable data property, so the first stuck and fired nothing while the
other three did nothing at all, and `href` went on answering the old fragment either way.
Detection is conservative, because under-reading a fragment navigation costs a debug line while
over-reading a real one would hide the fact that the capture stayed put. `LocationBindingTests`
pins both halves.

### `iframe.contentWindow.String` — an empty sub-window

A sub-window carried `document`, `location` and the event constructors and nothing else, so
`contentWindow.String` — and `Object`, `Array`, `Function`, `JSON`, `Math`, every one — read
`undefined`.

Taking a built-in from a fresh frame rather than from the current global is the standard way to
get one that page script has not patched, and Google's anti-abuse bundle does it on the first
run of its interpreter: `contentWindow.String`, then `.prototype`. Reading `prototype` off
`undefined` threw, and that is what derailed the interpreter into decoding its own bytecode
wrongly for the rest of the page — a single early read costing every later one.

`SubWindowBinding` now mirrors the parent realm's globals. **These are the parent's objects**:
`contentWindow.String === String` here, where a browser gives two distinct functions. Broiler
runs every document in one JavaScript context, so a per-frame realm is not something the
binding can conjure. Code that harvests a built-in gets a working built-in; only code comparing
the two for identity can tell, and `undefined` was defensible to nobody.

### `document.hidden` — absent is not false

The bot-check VM gates its "may I yield to the event loop?" predicate on `document.hidden == 0`.
That loose comparison is true for `false` and false for `undefined`.

This is the general lesson of the page: **a missing property does not read as "not hidden"; it
reads as a third state no page has a branch for.** `hidden` and `visibilityState` now answer
(`false` and `"visible"` — a capture renders one document and never backgrounds it), and
`onvisibilitychange` exists as a null-valued slot because `'onvisibilitychange' in document` is
the feature test pages use to decide whether the Page Visibility API is available at all.
Answering false there sent them to legacy focus/blur polling despite `visibilityState` being
correct.

### The watchdog, and the error that names nothing

botguard carries a watchdog comparing a wall-clock delta against 2^14 ms — `qA += K >> 14 > 0`.
Crossing it on two samples makes the VM poison its own state, and a later `new` opcode then
fails with:

```
TypeError: Cannot read properties of undefined (reading '360')
```

That error is a symptom of elapsed time, not of the property it names. `JsEntryTrace.WatchdogMs`
is 16384 for exactly this reason.

## Busy or idle: the ambiguity the watchdog creates

A page that misbehaves "after nothing happened for a while" has two possible causes, and they
look identical from outside the browser:

- the browser was **busy** for N seconds inside one turn — a stall;
- the browser was **idle** for N seconds between two turns — a gap.

Nothing that measures wall clock across a turn boundary can separate them, and a script's own
watchdog will read the second as the first. Reading botguard's source cannot settle it either,
because the number it compares is exactly that ambiguous quantity.

`BROILER_TRACE_JS_ENTRY` records both at the same boundary, off by default:

```bash
BROILER_TRACE_JS_ENTRY=1                  # turns >= 250 ms, gaps >= 1000 ms
BROILER_TRACE_JS_ENTRY=500                # both thresholds 500 ms
BROILER_TRACE_JS_ENTRY=turn=100,gap=5000  # set independently
```

A `WATCHDOG` mark on a **turn** is a real stall, and the label names what stalled. On a **gap**
the page was idle, and no amount of engine speed would have changed it. It writes to standard
error rather than only `RenderLogger`, because `RenderLogger` routes to `Debug.WriteLine`, which
a Release build does not emit — and this is a diagnostic whose whole value is "set the variable,
reproduce once, read the output".

## The engine bug found alongside it

An indexed read off `null` or `undefined` answered `undefined` instead of throwing, for the one
shape that did not throw: an index the compiler holds unboxed — a numeric local, a loop counter,
a constant-folded expression. `var u; var k = 3; u[k]` was `undefined`, and is now a `TypeError`
per §6.2.5.5.

Fixed in `Broiler.JS` by `0031018c`, "Throw on an indexed read off null or undefined". It is worth
reading twice because it turns silence into a throw: page script relying on the old answer now
stops where it used to carry on. That is the point — the old behaviour fed a wrong value onward
and surfaced far from its cause, which is this page's entire failure mode.

> This was recorded as `25da60d` until 2026-09-07, and no object with that prefix exists in
> `Broiler.JS` — a wrong SHA, not a missing commit. `0031018c` is an ancestor of the pin.

## Where the page fails now

The entries above were each a missing or wrong binding, and each one moved the page further along.
That is no longer where it stops.

Reaching the results at all needs a navigation: the search submits from the homepage through
`location.replace`, which this engine only logged, so the render was the box the query had been
typed into. That is fixed — see `script-initiated-navigation.md` — and following it uncovered the
wall behind it.

**Google's bootstrap now re-navigates to `/search` with a longer query each round**, carrying one
more token: first `sei`, then a `sg_ss` signal blob of some nine hundred characters. It is
collecting evidence because it is not satisfied with what it has, and it does not become satisfied.
google.de answers **429 Too Many Requests**.

**The 429 is the verdict, not the rate.** It was first read as a rate limit — the chain ran to the
hop cap, and ten requests at one endpoint inside twenty seconds is what a limiter is for. Then
`SamePathLoadLimit` cut the same page to three loads, and the answer was 429 again. Three is not ten,
so the count was never what was being objected to: the check has decided what this client is, and
says so with the status code it has. Tuning the budget further will not change it, and reading the
429 as "slow down" is what sends the next person tuning a constant instead of reading `sg_ss`.

**Watch `gbv`.** An earlier run of the same search carried `gbv=1` — Google's basic, no-JavaScript
variant, which does not run this check at all and is the version that would render. A later one,
after the engine had grown a working `location.replace`, `form.submit()` and control-value
serialization, carried `gbv=2` and the full JavaScript path. The capability the browser presents is
what selects the route, so making the engine better can move it onto the harder one. Pinning `gbv=1`
on the URL is the way to see results today, and the difference between the two runs is worth keeping
in view when judging whether a change helped.

So the shape of the problem has changed. Every entry above was a binding that could be written, and
writing it moved the page on. This one is the anti-abuse check declining the client, and there is no
single binding whose absence explains it — the same ambiguity the watchdog section describes, one
level up. `SamePathLoadLimit` stops the browser paying for a conversation that is going nowhere; it
is a courtesy, not a fix.

Anyone picking this up should start by finding out *what* the check is unhappy about, rather than
adding another binding and re-running. `sg_ss` growing between hops is the signal to read.

## Open

**~~Which cause fed the watchdog was never recorded.~~ Settled: it is a gap.** A run with
`BROILER_TRACE_JS_ENTRY=1` on 2026-09-07 marked one crossing, and it is on the idle side:

```
gap       28472 ms idle before Script:inline-0   <-- WATCHDOG
```

Per the ambiguity above, that is the answer the trace was built to give. **No turn came near the
16384 ms threshold** — the slowest were 4585 ms and 2875 ms, botguard's interpreter running, which
is slow but is not what crossed the line. So the engine was not stalled, and the nullish
indexed-read fix, whatever else it was worth, is not what this was waiting on.

What it *was* waiting on is the next question and a different one. The gap runs from the previous
document's last JS to the new document's first inline script, so it spans a navigation: fetch,
parse, and the handoff into the load window. Twenty-eight seconds of that is a lot, and none of it
is script. Anyone picking this up should measure inside that span rather than inside the engine —
`BridgePhaseTrace` already brackets parse and document registration.

**What the bot check wants is not known.** The `sg_ss` round above is where the page stops, and
nothing in the tree records which signal it is failing on.

## Supporting surface

The polyfills this page needs are registered as "Google Search Compliance" work in
`Registration/Polyfills.cs` and `Registration/Window.cs`: `performance` (with a `timeOrigin`
belonging to the navigation, not to the call), `Image`, `IntersectionObserver`, `ResizeObserver`,
`TextEncoder`/`TextDecoder`, `URL`/`URLSearchParams`, `AbortController`, `crypto`, `CSS`, and an
in-memory `document.cookie`. The content-rendering set is a versioned embedded asset,
`Polyfills/content-rendering-polyfills.js`, rather than inline C# string literals.
