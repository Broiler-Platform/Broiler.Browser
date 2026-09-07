# html5test exceptions

`html5test.com` is a page built entirely out of feature detection, which makes it a good probe
for one specific failure mode: **a wrong answer about support is worse than no answer.** It is
also, structurally, the loader idiom much of the web is built on — inject a `<script src>`, poll
until the global it defines appears — so a gap in that path leaves it visibly half-rendered
rather than subtly wrong.

This records the rule the page forced, and what chasing it turned up.

## The rule: absent, or true — never a plausible stub

A feature detector reads an **absent** API correctly. It reads a stub that returns
plausible-but-wrong values as *support*, and the page then commits to a path that cannot work.
The failure moves from the detection site to somewhere far away, which is the expensive kind.

So:

- **If the answer is genuinely true, answer.** `document.hidden` is `false` and
  `visibilityState` is `"visible"` because a capture renders one document and never backgrounds
  it. That is not a stub; it is the correct value.
- **If nothing true can be produced, be absent.** Do not return zeroes, empty strings or
  `null` shaped like a real result.

The canvas readback API is the worked example. Phase 6 removed a `CanvasDrawCommand` recorder
that no renderer ever read, leaving every drawing method a literal empty body. `getImageData`
and `toDataURL` were then left **deliberately absent** rather than stubbed: returning zeroed
pixels would have turned an honest `TypeError` — which every feature detector on the web reads
correctly as "no canvas readback" — into a false claim of support.

**That exception is now retired.** `CanvasRenderingContext2D` owns a `BBitmap` the size of the
canvas and rasterises through `BCanvas`, so the readback APIs report what was actually drawn.
The reasoning held only while nothing rasterised; a real backing store is what ends it.

What remains approximate there is documented on the type itself: `measureText` reports the
renderer's estimated pen advance rather than a shaped advance, so the non-`start` `textAlign`
values that derive from it are approximate too. There is no transform stack, because the binding
exposes no `translate`/`rotate`/`scale`.

The inverse of the rule is worth stating explicitly, because it is the mistake that looks like
caution: **absence is not a safe default.** Where a true answer exists, omitting it does not read
as "no" — it reads as a third state no page has a branch for. See
`google-search-post-consent-challenge.md`, where `document.hidden` being `undefined` failed a
`== 0` test that `false` passes.

## What the page surfaced

### Dynamic script loading, in two halves

The single line every dynamic script loader is built out of —
`s = createElement("script"); s.src = url; head.appendChild(s)` — was broken twice over.

**`HTMLScriptElement` had no reflected IDL attributes.** `s.src = url` set a plain JS property
on the wrapper and wrote nothing to the DOM. The element serialised as a bare `<script>` with no
attributes, so there was nothing to fetch. (The same shape as the `<link>.href` gap fixed
alongside it.)

**Script-created `<script>` elements never ran on insertion.** With the injected script never
running, the global never appears, the poll reschedules itself forever, and the capture burns
its whole `AsyncDrainIterationLimit` before giving up with "async work did not settle" and a
half-built page. html5test's `waitForWhichBrowser` polls every 100 ms for exactly such a global,
so the entire page below the header was never generated.

`ScriptInsertionRunner` fixes the second half, with two constraints worth keeping:

- **Only script-created elements are eligible.** The candidate set is populated from the
  `document.createElement`/`createElementNS` funnel alone. That is what keeps out the two kinds
  that must *not* run here — parser-inserted scripts (the host already ran those; running them
  again doubles every side effect) and `innerHTML`-produced ones (per spec "already started" at
  birth, never executed). Neither reaches `createElement`.
- **Insertion is observed, not intercepted.** It subscribes to the canonical
  `DomDocument.Mutated` stream rather than hooking `appendChild`, so every path that can connect
  a node — `insertBefore`, `replaceChild`, `append`/`prepend`, `insertAdjacentElement`, or
  attaching a fragment the script was built into — is covered by one subscription.

### `noscript` rendered alongside the content it stands in for

A page's "you need to enable JavaScript" block rendered *above* the content its own scripts had
just built. The two stacked, because nothing suppressed the fallback once the scripts had run.

`HtmlPostProcessor.StripNoscriptContent` drops the element **whole**, not merely emptied —
unlike `StripIframeContent`, since an `iframe` still paints its own replaced box once the
fallback is gone while a `noscript` paints nothing.

This suppresses rendering, not the DOM. Broiler's parser builds `noscript` content as real
elements, where the HTML parsing spec says a scripting-enabled parser takes it as raw text, so a
page can still see markup through the DOM that a browser would not. **That is a separate parser
conformance gap**, not something this fixes.

### `HTMLUnknownElement` is a subtype of `HTMLElement`

An unknown element is an instance of both, and html5test's
`x instanceof HTMLElement && !(x instanceof HTMLUnknownElement)` check relies on exactly that
split. The `instanceof` bridge in `Utilities.DomInterfaces` defines it accordingly: an HTML
element whose tag is neither empty, nor known, nor hyphenated (a custom element is not
"unknown").

### `document.scripts`

The position within the collection is the property's entire purpose:
`document.currentScript || document.scripts[document.scripts.length - 1]` is how a classic
script finds its own element while it runs, since during synchronous execution it is the last
one in the document.

Cloudflare's `email-decode.min.js` ends with exactly that line, and it ships on every page
Cloudflare serves with email obfuscation enabled — so the missing property threw a `TypeError`
out of a script the page never asked for.

**It was reported against html5test.com; nothing about that site is special.** That caveat
applies to this whole document. The page is a convenient probe, not a target, and a fix filed
under its name is almost always a fix for the web.
