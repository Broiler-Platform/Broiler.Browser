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

The session goes to the viewport whether or not work is still due: it is the page's scripts, and
what the user does to the page is delivered to them through it (`HandlePointerButton` →
`InteractiveSession.DispatchPointer`). It used to be disposed when `HasWorkDueInLoadWindow` was
false once the settle returned, on the reasoning that a page whose only remaining work is an
interval's later ticks is finished loading and a context nobody steps is a leak. It is finished
loading, but it is not finished: a click handler, a checkbox drawn by script, reCAPTCHA's widget
— none of them answered, because nothing was left to deliver a click to.

What keeps that from costing anything is the same bounded question. `StepAnimation` runs only
while work is due — in the load window, or within the same span after the user's last click,
which `DispatchPointer` opens — one batch per tick. That path re-parses only when a step actually
changed the document:

> A callback batch that touched no DOM — a timer that only reads, schedules, or measures —
> still returns the serialised document, and `google.com` runs many of those.

Re-parsing costs a full parse and layout, so it is compared against the last applied HTML first.
When the bounded question goes false, the tick stops; the session stays until the page is left.

## Keys, typed text, and the controls the window hosts

Input meant for the page reaches its scripts before the window acts on it
(`BrowserViewport.TryDispatchThroughPage`):

- **A key while the page has focus** — the viewport's, or a control the window hosts over the page.
  The page hears `keydown` first (`InteractiveSession.DispatchKey`); a key it cancels goes no
  further and types nothing, and a key it acted on itself — Tab moving focus, Enter submitting a
  form or following a link, Space clicking a button — is not handled again. Anything else goes on
  to the window: the field editor edits, the viewport scrolls.
- **Typed text.** With the field editor open, the editor takes the text in first and the page is
  told what the field now holds (`DispatchText` with `EditedValue`), hearing `keypress`,
  `beforeinput` and `input`; a change the page cancels is undone in the editor. A deletion or a
  paste the editor makes reaches the page the same way (`DispatchEdit`). Without the editor — a
  field in a frame — the page puts the text in the field itself.
- **The field editor follows the page's focus.** It opens on a text field of the page that a press,
  Tab or a script focused, and closes once the page's focus leaves it. `InteractiveSession.FocusVersion`
  says when to look, since asking where the focused field is lays the page out.
- **A press, a release or a move over a hosted control** — the field editor, a checkbox — which the
  viewport never sees. The release of a press on a text field went to the editor, so the page had
  no `click` for it. A point on a list a control has open outside itself is not on the page, and is
  not delivered.

- **The field's selection and composition.** The page hears the selection the user makes in the
  editor — a drag, Shift and an arrow, a click that placed the caret — through `DispatchSelection`,
  and an input method's composition through `DispatchComposition`. The editor follows what the page's
  scripts did to the field — a mask that reformatted its value, `setSelectionRange`, a Tab that
  selected it all — when `InteractiveSession.FieldVersion` moves. A key an input method takes reaches
  the page as `Process`, its `keyCode` 229.

What the page changed is shown when its `RenderVersion` moved, as after a click — and that includes
hover and focus: the page the window is handed carries each element's `:hover`, `:active` and focus
state (`data-broiler-user-action`), and its target and the controls the user has interacted with
(`data-broiler-state`), which the renderer's selectors match, so a `:hover` rule applies as the
pointer crosses into its element and a field outlined only when `:user-invalid` is outlined once the
user has left it wrong.

## The page's own form activation, fragments and history

- **A click on a submit or reset button is the page's.** It submitted nothing: the bridge left a
  button's activation to the window, and the renderer's link click, which the window submits a form
  from, never reaches a submit button. The page now validates and submits the form, or resets it, and
  says so (`PointerInputResult.Handled`); the window then does nothing of its own for the click. A page
  with no script at all gets a realm for this when it has a form, as it does for event handler
  attributes and frames, so its submit buttons submit too.
- **A link into the page** scrolls to the fragment as before, and the page hears it as its own
  fragment navigation (`InteractiveSession.NavigateToFragment`): `location.hash`, `hashchange` and
  `:target` follow. A page with no scripts has the renderer find the target
  (`HtmlContainer.TargetFragment`) and style the document again without parsing it
  (`RestyleDocument`), so what the user typed stays.
- **The page's session history is the window's.** A page's `pushState`, `replaceState` and fragment
  navigations are entries in the window's history too (`InteractiveSession.TakeHistoryChanges`, read
  after every input and every step): the address moves and back is enabled. Back and forward between
  the page's entries are the page's traversals (`TraverseHistory`) -- it hears `popstate` and nothing
  loads -- and its `history.length` counts the window's entries around its own (`SetSessionHistory`).
  A `history.back()` past the page's first entry is the window's to make. A frame's entries are the
  window's too, at the page's address, so its back button goes back through what a frame did. Back or
  forward to another page puts the view where the user left it (`PageRequest.LeftAtScrollY`), once that
  page has loaded; within the page's own entries the page restores it itself.
- **A `javascript:` URL** -- a link, the address bar -- runs its script in the page on screen
  (`RunJavaScriptUrl`) if the page's policy allows inline script. It loaded an error page before. A
  string the script answers is the page's new document (`NavigationRequest.Document`), shown at the
  page's URL without a fetch and without a history entry of its own (`PageRequest.InlineDocument`); a
  reload fetches the URL again.
- **A submission names its button.** The page's request carries its submitter (`SubmitterIndex`, an
  image button's point) and what its `formdata` listeners changed (`FormDataEdits`), which the window
  replays on the entry list it builds from the serialised document. Enter submits as the form's default
  button, as in Chromium. A `method="dialog"` form closes its dialog and loads nothing. A frame's form
  that targets the page arrives with no form index and the URL the bridge built from its entries, and
  is loaded as it is. A choice in a select the window draws for the page's (`HtmlFormControlHost`) is
  the page's select's too (`SelectOptionByUser`), with its `input` and `change`.
- **Scrolling is shared.** The window follows the page's own scroll -- `scrollTo`, `scrollIntoView`, a
  script's fragment navigation (`ViewportScroll`) -- and tells the page where the user scrolled
  (`ScrollViewportTo`), so its `scrollY` and its geometry answer for what is on screen. The page hears
  that `scroll` in a task, queued when the frame that reported the scroll is drawn, so `RenderFrame`
  arms the tick for it; nothing else would step it before the next input.
- **History.** The window remembers every page it has shown, fragment included, for as long as it is
  open, and writes it nowhere. The renderer asks it about each link (`VisitedLinkPredicate`), so a
  link to one of them takes its `:visited` colours — only colours, as in a browser, and never where
  a page's scripts could read them.

## The shape of the bug, if it comes back

A window that stops repainting while a page loads, and recovers when the page's script happens
to finish, is script running on the UI thread. Look for a drain or step called from the message
pump rather than from the load worker.

A window that never goes idle on a page that is visibly finished is the unbounded question:
something is driving a pump on "are callbacks queued" rather than "is work due".
