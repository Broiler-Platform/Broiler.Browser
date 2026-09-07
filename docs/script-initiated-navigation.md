# When a script navigates

A page that leaves by calling `location.replace(url)` used to render as the page it was leaving.
This is what that cost, what the fix is, and what is still not wired.

## The bug was invisible, and it looked like a working capture

The whole failure was one line of debug output:

```
[Debug] [JavaScript/DomBridge.location] location.replace(https://www.google.de/search?…&q=test&…&gbv=1&…)
requested; the capture renders the document it was given and does not navigate
```

Read that URL rather than the message and the picture changes: `/search` rather than `/`,
`q=test`, `source=hp` (submitted from the homepage), `btnG=Google+Suche` (the submit button),
`gbv=1` (Google's no-JavaScript variant). A search had been typed and submitted, Google's script
turned the submission into a navigation, and the browser stayed on the homepage. **The render was
of the box the query was typed into.**

That is the shape of the whole class: nothing throws, nothing is blank, and the output is a real
page — just not the one that was asked for. Only the log says so, and only if someone reads the
URL in it.

## What follows a redirect, and what did not

| Mechanism | Followed? |
| --- | --- |
| HTTP 3xx | yes, inside `HttpClient` |
| `location.href = url`, `assign`, `replace`, `reload` | **now yes** |
| `<meta http-equiv="refresh">` | **now yes**, up to a stated wait |
| `form.submit()` | **now yes** |

JS-initiated navigation was the gap, and it was the one pages reach for most.

## Why a binding cannot just navigate

`LocationBinding` has no loader, no session history and no window. It cannot fetch the target, and
it must not tear down the JavaScript context it is running inside — the script that called
`location.replace` has more to do before the document goes away, and a browser lets it finish.

So the work is split, and the split is the design:

- **The binding resolves and records.** `location.*` resolves the target against the document's own
  URL and hands the host a `NavigationRequest` — the URL and which method was called. Then it
  returns. Returning rather than throwing is the original point of these methods existing at all:
  a throw aborts the caller exactly as `undefined is not a function` did.
- **The host decides and performs.** `IDomBridgeRuntime.PendingNavigation` carries the request out;
  `InteractiveSession.PendingNavigation` is where a browser reads it. Following is policy, and
  policy differs: an interactive browser navigates, a capture pinned to one document may not.

A **fragment** navigation never reaches the host. It is same-document — no fetch, just a moved
`location.hash` and a `hashchange` — so the binding performs it outright.

## Reading it at the right moment

Two ordering constraints, both easy to get wrong and both silent when you do:

- **After the load window settles**, not straight after the synchronous scripts. The script that
  decides to leave usually runs on a timer, so asking early misses exactly the pages that navigate.
- **Before the session is disposed.** The request lives on the bridge, and disposal takes the bridge
  with it.

`BrowserApp.LoadUrlOnWorkerAsync` does both, in that order, and
`ANavigationRequestedFromATimerIsStillWaitingAfterTheLoadWindow` pins the first.

## Following, and knowing when to stop

The browser follows by default, in a loop around load-execute-settle. Three rules bound it:

- **`MaxScriptNavigations` = 10.** A page that navigates on load is ordinary; a page that navigates
  to itself on every load is also ordinary, and following that one forever is a hang with nothing on
  screen to explain it. The cap separates the two without having to tell them apart.
- **A request for the URL already loaded is not followed** — unless it came from `reload()`, where
  asking for the current document is the entire meaning of the call. From `assign`/`replace` it is a
  page re-stating where it is, and following it would fetch the same bytes to run the same script to
  ask again.
- **`SamePathLoadLimit` = 3**, counted per URL path within one navigation. This is the rule that
  actually fires, and the section below is why.

### The rule that exact-URL equality could not catch

Following shipped with only the first two rules, and google.de answered **429 Too Many Requests**.

The chain looked like this — one path, a longer query every hop:

```
/search?…&q=test&gbv=1&oq&gs_l
/search?…&q=test&gbv=1&oq&gs_l&sei=9Q2fauioIY…
/search?…&q=test&gbv=1&oq&gs_l&sg_ss=*pJiamMLyAAZ5zFMzWcx9…(900 chars)…&sei=9w2farjGL7X…
```

Google's bootstrap re-navigates to the page it is already on, carrying one more token each round:
first `sei`, then a large `sg_ss` signal blob. **Every hop is a URL nobody has seen before**, so the
"already loaded" check never fires, and the chain runs to the hop cap — ten requests at one search
endpoint inside twenty seconds, which is what a rate limiter exists for.

Two things were wrong, and only one of them was the guard:

- **The guard tested the wrong thing.** What repeats in a re-submission loop is the *path*; the query
  is what changes. Counting loads per path catches it on the second round. A budget of three still
  leaves room for a handshake that converges in a hop or two.
- **The round does not converge for this engine at all.** Each `sg_ss` is Google collecting more
  signal because it is not satisfied with what it has — the same wall as
  `google-search-post-consent-challenge.md`. Following harder was never going to reach the results
  page; it only reached the rate limiter faster. The budget stops the bleeding. It does not make the
  search work.

A 429 is not swallowed, incidentally: `PageLoader.FetchAsync` calls `EnsureSuccessStatusCode`, so
the throw ends the loop and the error page is what the user sees. The loop stops on the first
failing hop rather than retrying it.

The document that asked to leave is never shown. Frames already published for it stay on screen
until the next load publishes its own, because a blank pane for the length of another fetch is worse
than a stale one.

### History

A followed chain is **one** history entry, rewritten to the final URL once the load lands. Back
should return to where the user came from, not step them through a bot-check interstitial, and the
entry is what reload and back/forward re-issue.

The rewrite happens only when a script navigation was actually followed. History entries can carry a
POST body — that is how revisiting a submission re-issues it, behind a confirmation — and rewriting
unconditionally would quietly turn every form submission into a GET of its own action URL.

## The one whose target the bridge cannot finish

`form.submit()` is the only navigation where naming the URL is not enough. A GET form's target
*includes* its data set — the fields go in the query — so knowing where it goes means serializing
the form, and the bridge has no serializer.

It also must not grow one. `HtmlFormSerializer` and `HtmlFormState` already do this for a clicked
submit button and for Enter pressed in a field: entry list, `enctype`, `multipart`, `text/plain`,
action resolution. A second implementation on the bridge side would be the two drifting apart, and
the bug that results is a form that submits differently depending on what triggered it.

So the request carries **which form**, and the host builds the rest:

- The bridge resolves the form's `action` into `Url` — the whole target for a POST, the stem of it
  for a GET — so a log line names where the page was going.
- `FormIndex` names the form **by position in document order**, because that is what survives the
  trip. The host re-parses the serialized document rather than sharing the bridge's nodes, and a
  form with no `id` or `name` has nothing else to be identified by. Both walks are the same order.
- `HtmlFormState.TryBuildScriptSubmitRequest` turns that into the `PageRequest`, with **no
  submitter** — `form.submit()` submits without any button contributing its name and value, which is
  exactly what separates it from a click on one.

Two guards needed adjusting for it. A form with no `action` submits to its own page, so the
"already loaded" refusal had to stop catching it — the resulting request is not the one already
made, since a GET carries a new query and a POST a body. The same reasoning applies to the
post-load check below, which refuses a repeat only when the request is repeatable.

`preventDefault()` on the `submit` listener now means something. The default action used to be
nothing at all, so cancelling it cancelled a no-op; it is the difference between the form going and
staying.

## After the page has loaded

The load loop reads the pending navigation while a page is loading. That is the wrong and only
moment for `form.submit()`, whose usual shape is a user filling a form and a click handler
submitting it — long after the load window closed.

`BrowserApp.StepAnimation` asks the same question on the UI thread, which is the one place
`NavigateTo` can be called from, and `BrowserViewport.TakePendingNavigation` answers it using the
viewport's own `HtmlFormState` — by then its control overrides hold what the user actually typed,
where during a load there was nothing to hold.

**A post-load navigation is not a hop in a chain.** It goes through `NavigateTo` like a link click:
its own history entry, and a fresh set of loop budgets. A page navigating five seconds after load is
not redirecting; it is doing what the user asked for.

## The one that is not script at all

`<meta http-equiv="refresh">` reaches the host as the same `NavigationRequest`, so it inherits the
guards above without knowing they exist — a refresh loop is bounded by the same per-path budget that
stopped the search re-submission.

**It does not come through the bridge, and that is the point.** `MetaRefreshDiscovery` reads the
fetched markup directly, because `ExecuteScriptsInteractive` returns `null` for a page with no
scripts and a refresh interstitial is usually exactly that page. Discovering it on the bridge would
have missed every document that actually uses it. A script navigation found later supersedes it —
both are this document asking to leave, and the script asked second.

Two things are specific to it:

- **The `content` attribute has more forms than its one job suggests** — `0;url=/next`,
  `0, URL='next'`, `0.0;url=…`, a bare `5` meaning "reload me". `MetaRefreshDiscovery.TryParseContent`
  covers those. A value with no leading time is rejected rather than guessed at: the time is the one
  part the syntax requires, so a `content` without one more likely belongs to a different
  `http-equiv`.
- **The wait is the only part of a navigation request a host has to weigh rather than act on.** A
  second or two is a redirect with a courtesy message; half a minute is a notice meant to be read,
  and replacing it immediately would take away the thing it exists to show.
  `MetaRefreshFollowLimit` is two seconds, and it is a judgement rather than a rule — see below for
  what would replace it.

## Still not wired

**The non-interactive `ScriptEngine.Execute` path.** It returns serialized HTML with nowhere to put
a pending navigation. A host on that path reads `IDomBridgeRuntime.PendingNavigation` directly,
which is why the property is on the runtime interface rather than only on `InteractiveSession`.

**Frames.** A frame's Location gets no host (`LocationBinding.Build`), so a framed page's navigation
is logged and dropped. Navigating a frame replaces the frame, not the page — a different operation
from the one the host performs, and not one this contract expresses.

**A scheduled navigation.** Nothing here can navigate *later*, which is why a long meta refresh is
declined rather than deferred. A browser honours any wait by scheduling it, and doing the same would
retire `MetaRefreshFollowLimit` entirely — the threshold exists only because the choice today is
between acting now and not acting. It needs a timer that survives the load loop and respects the
navigation generation, so it is a real piece of work rather than a constant to delete.

**`outerHTML` reflects an input's value and not a textarea's or a select's.** Document
serialization runs `ReflectRenderState` over a render projection, and that is the path a submission
is built from, so all three controls are right there. `element.outerHTML` does not: it goes through
the attribute enumeration alone, which can reflect an `input` — a value *is* an attribute — and has
nowhere to put the other two, whose values are child text and a sibling's attribute. Measured, on a
page that set all three:

```html
<div id="o"><input id="i" value="wi"><textarea id="t">pt</textarea>
<select id="s"><option value="a" selected="">A</option><option value="b">B</option></select></div>
```

`wi` is the written value; `pt` and the `selected` on `A` are what the page shipped with. A page
reading its own markup back sees one control current and two stale. Submissions are unaffected.

### What the value reflection did

`input.value` reached the serialized document only when the input had **no `value` attribute** and
the value was **non-empty**. So an author's `value="…"` outlived every script that overwrote it, and
clearing a prefilled field left the old text in the markup. Since a submission is built by
re-parsing that markup, what went to the server was the value the page shipped with rather than the
one on screen — silently, and looking entirely correct.

`textarea` and `select` did not reflect at all, which is the same bug reached from further back:
there was no machinery to loosen. Each control now writes into the place HTML actually keeps its
value — an `input`'s attribute, a `textarea`'s child text (§4.10.11), and for a `select` the
`selected` attribute moving to the option it chose, through the same option walk the select binding
selects with, so "the third option" cannot mean two things.

`RuntimeValue.TryGet` answers "did a script set this", which is the only condition any of it needed;
a control the page never touched is left exactly as authored.

Reflection ran only onto an input that had **no `value` attribute**, and only for a **non-empty**
string. So an author's `value="…"` outlived every script that overwrote it, and clearing a prefilled
field left the old text in the markup. Since a submission is built by re-parsing that markup, what
went to the server was the value the page shipped with rather than the one on screen — silently, and
looking entirely correct.

`RuntimeValue.TryGet` already answers "did a script set this", which is the only condition the
reflection ever needed. Both extra guards are gone, and the attribute enumeration now skips the stale
attribute instead of emitting it alongside the new one.
