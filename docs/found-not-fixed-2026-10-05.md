# Gaps found while bringing the window level with Chromium

**Status:** open, not fixed. Found on 2026-10-05 while bringing the window's rendering and
interaction level with Chromium's: the reCAPTCHA demo, popovers and dialogs, the top layer, hosted
form controls and `javascript:` URLs.
**Components:** Broiler.HTML, Broiler.HtmlBridge, Broiler.JSeal, Broiler.VM, Broiler.Browser.
**Affects:** the Broiler.Browser window and `Broiler.Cli --analyze`.

The work that found these landed as Broiler.DOM #28, Broiler.JSeal #3, Broiler.CSS #61, Broiler.Layout
#127, Broiler.HTML #240, Broiler.HtmlBridge #12 and this repository's change that takes their packages.
Chromium's behaviour was measured unless an item says otherwise.

| # | Gap | Where the fix goes |
| --- | --- | --- |
| 1 | A `<button>` is `content-box` | Broiler.HTML, Broiler.CSS.Dom |
| 2 | Anchored boxes the layout engine does not place | Broiler.HtmlBridge, Broiler.Layout |
| 3 | Anchors in frames are not resolved | Broiler.HtmlBridge |
| 4 | `delete options[i]` answers true; option entries are read-only | Broiler.JSeal |
| 5 | A select a script empties shows its first option | Broiler.HtmlBridge, Broiler.Browser |
| 6 | A document runs under one policy | Broiler.HtmlBridge |
| 7 | Removing a frame that holds focus blurs nothing | Broiler.HtmlBridge |
| 8 | A refused `eval` throws `Error`, not `EvalError` | Broiler.HtmlBridge |
| 9 | The file button keeps the picked file's name | Broiler.Browser |
| 10 | After Stop, the window's record outranks the page again | Broiler.Browser |
| 11 | The VM has no indexed-write hook for a host object | Broiler.VM, Broiler.JSeal |
| 12 | A worker test is flaky under load | Broiler.HtmlBridge tests |

## 1. A `<button>` is `content-box`

Chromium's user-agent sheet gives `button` `box-sizing: border-box`: a `<button style="height: 30px">`
is 30px tall. Broiler.HTML's `CssDefaults.cs` sets no box sizing for it, so the same button is 34px
tall, its padding and border added. The fix is a user-agent rule in Broiler.HTML and the same rule in
Broiler.CSS.Dom's `CssUserAgentDefaults`. The button-like `input` types and `select` should be checked
in the same pass. The window's top-layer tests give their button `box-sizing: border-box` until then.
Broiler.HTML's roadmap carries this in section 5.

## 2. Anchored boxes the layout engine does not place

The bridge leaves boxes in the subset that Broiler.Layout's anchor placement takes
(`IsMvpNativeAnchorBox`) to the renderer, which places them both for what is drawn and for what a
script measures. Any other anchored box is baked by the bridge, and only into a page that is drawn.
That has two consequences:

- A script measures such a box (`getBoundingClientRect`, `offsetTop`) where it would stand with no
  anchor, because the projection a geometry snapshot lays out has no bakes.
- The bake of an auto-sized `position-area` box stretches it over its area. Chromium gives it its
  content's size unless it stretches.

Widening the engine's subset closes both. Baking into geometry snapshots would close the first only.

## 3. Anchors in frames are not resolved

A frame's top layer is stamped into the markup the frame is rendered from. But nothing in a frame's
document resolves `anchor()`, `position-area` or a popover's implicit anchor.

## 4. `delete options[i]` answers true; option entries are read-only

In Chromium, `delete select.options[0]` answers `false` (a `TypeError` in strict code), and
`Object.getOwnPropertyDescriptor(select.options, 0).writable` is `true`. Broiler answers `true` and
removes nothing, and describes the entry as read-only. Both come from JSEAL's exotic contract, which
describes every handler entry as read-only and gives the deletion of an index the ordinary path. It
is tracked as J20 in Broiler.JSeal's roadmap (Broiler.JSeal #3).

## 5. A select a script empties shows its first option

After `select.selectedIndex = -1`, Chromium draws the select blank. The markup the bridge hands the
renderer then marks no option `selected`, which is also what a select with nothing marked looks like,
and that select has its first option selected. So the window's drop-down shows the first option. The
page itself keeps no selection and hears no `change`, which the window test
`A_Select_A_Script_Empties_Stays_Empty` pins.

The fix needs a way for the projection to say that no option is selected, and the window's
`HtmlFormControlHost.ShownOption` to honour it.

## 6. A document runs under one policy

This gap is older than these rounds. `ScriptEngine` runs a document under its own `<meta>` policy when
it declares one, and otherwise under the policy the host set (`Csp`). So a policy the host delivered is
dropped when the document declares its own. That covers a header policy, and a `javascript:` document's
inherited policy. Content Security Policy enforces every policy a document has. Frames already keep a
set (`ContentSecurityPolicySet`); the page should keep one too.

## 7. Removing a frame that holds focus blurs nothing

With Broiler.DOM's `DomDocument.Removing`, a focused element taken out of its document is blurred
first, as Chromium does. `DomRemoval.Removes` does not look into a frame's document, though, so
removing an `iframe` whose document holds focus moves no focus and fires nothing. Chromium's behaviour
here has not been measured yet.

## 8. A refused `eval` throws `Error`, not `EvalError`

This gap is older than these rounds, and applies on every page. Under a policy that forbids
evaluation, the engine's eval stub (`ScriptEngine.cs`) throws a plain `Error`, and `new Function` gets
the realm's `SyntaxError`. Chromium throws `EvalError`. The stub's comment says what retiring it would
take. The window's `javascript:` policy test asks only whether `eval` was refused.

## 9. The file button keeps the picked file's name

The window sends the submission the page encoded, so a file the page cleared from its input is not
sent. But the window's hosted file button still names the file the user picked:
`HtmlFormControlHost.DescribeFile` reads the window's own record, because the markup carries no files.
The fix is for the session to report an input's file names, or for the label to follow the input's
`value`.

## 10. After Stop, the window's record outranks the page again

The page's state outranks the window's record of the user's choices while the page's scripting session
runs (`HtmlFormState.PageHoldsState`): the record steps aside, and the bridge reflects the page's state
into the markup.

Stop during loading ends that session. From then on the record outranks the markup again, so a
submission the window builds can send a choice the page had already changed. The hosted controls
themselves keep showing what the page last did.

The fix is to forget the choice records when a session that held them stops, keeping the file
records, which markup cannot carry. That needs a test window that can press Stop while a page is
loading.

## 11. The VM has no indexed-write hook for a host object

Broiler.JSeal #3 serves `IJsExoticIndexedSet` on the VM provider with a `set` trap on an internal
`Proxy`. JSEAL's I06, which drops that `Proxy` once the VM has a native deletion hook, cannot drop it
for these objects until the VM also has a native indexed-write hook. That is I19 in Broiler.JSeal's
roadmap.

## 12. A worker test is flaky under load

`WorkerPortTests.AWorkersOwnChannelCopiesItsMessages` in Broiler.HtmlBridge failed once, as `waiting`,
in a full Release-VM run on a busy machine. It passed in two more full runs and in five runs on its own.
When the machine is loaded, the worker's answer can miss the load window.

## Not verified locally

The checks that need PowerShell 7 were not run on the machine used: each repository's `eng/pack.ps1`
and Broiler.JSeal's `eng/test-package-consumer.ps1`. `dotnet pack` stood in for the first. CI runs
both.
