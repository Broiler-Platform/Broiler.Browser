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
| `<meta http-equiv="refresh">` | no |
| `location.href = url`, `assign`, `replace`, `reload` | **now yes** |
| `form.submit()` | no — see "Still not wired" |

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

## Still not wired

**`form.submit()`.** It fires the `submit` listeners and returns; `FormSubmitBinding` has no form
serialization, so there is nothing to navigate *to*. Doing it properly means field collection,
`enctype`, `method` and `action` resolution, and multipart for file inputs — `PageRequest` already
models the POST it would produce, so the missing half is entirely on the bridge side.

**`<meta http-equiv="refresh">`.** Never handled, before or after this.

**The non-interactive `ScriptEngine.Execute` path.** It returns serialized HTML with nowhere to put
a pending navigation. A host on that path reads `IDomBridgeRuntime.PendingNavigation` directly,
which is why the property is on the runtime interface rather than only on `InteractiveSession`.

**Frames.** A frame's Location gets no host (`LocationBinding.Build`), so a framed page's navigation
is logged and dropped. Navigating a frame replaces the frame, not the page — a different operation
from the one the host performs, and not one this contract expresses.
