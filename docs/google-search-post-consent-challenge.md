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

Fixed in `Broiler.JS`, pinned by `25da60d`. It is worth reading twice because it turns silence
into a throw: page script relying on the old answer now stops where it used to carry on. That is
the point — the old behaviour fed a wrong value onward and surfaced far from its cause, which is
this page's entire failure mode.

## Open

**Which cause fed the watchdog was never recorded.** `BROILER_TRACE_JS_ENTRY` was added to settle
it in one reproduction, and the nullish indexed-read fix landed alongside as a plausible
contributor, but no run confirming either is in the tree. Anyone picking this up should
reproduce with the trace on before assuming the engine fix closed it.

## Supporting surface

The polyfills this page needs are registered as "Google Search Compliance" work in
`Registration/Polyfills.cs` and `Registration/Window.cs`: `performance` (with a `timeOrigin`
belonging to the navigation, not to the call), `Image`, `IntersectionObserver`, `ResizeObserver`,
`TextEncoder`/`TextDecoder`, `URL`/`URLSearchParams`, `AbortController`, `crypto`, `CSS`, and an
in-memory `document.cookie`. The content-rendering set is a versioned embedded asset,
`Polyfills/content-rendering-polyfills.js`, rather than inline C# string literals.
