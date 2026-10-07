# Why Acid3 scores 91/100

**Status:** fixed and released. The nine failures were fixed by Broiler-Platform/Broiler.DOM#31,
Broiler-Platform/Broiler.HtmlBridge#22 and Broiler-Platform/Broiler.CSS#66, and Acid3 scores 100/100. The
rendering defects were fixed by this repository (the red "YOU SHOULD NOT SEE THIS AT ALL"),
Broiler-Platform/Broiler.CSS#68 (the 500px score), Broiler-Platform/Broiler.CSS#67 (test 28's "FAIL"),
Broiler-Platform/Broiler.Layout#132 with Broiler-Platform/Broiler.HtmlBridge#23 (the object fallback
"FAIL") and Broiler-Platform/Broiler.HTML#246 (the red corner square). What still differs from the
reference is listed under "Still open" at the end. Investigated on 2026-10-07 from a window screenshot
that showed `91/100` drawn five times too large, a red square in the body's top-right corner, a red
"YOU SHOULD NOT SEE THIS AT ALL" and a "FAIL" above the heading.
**Components:** Broiler.DOM, Broiler.HtmlBridge, Broiler.CSS, Broiler.HTML, Broiler.Layout,
Broiler.JS, Broiler.Browser.
**Affects:** the Broiler.Browser window and `Broiler.Cli`, which run Acid3 the same way and both
score 91.

Everything here was measured with this repository at `0406251` and the packages it takes
(Broiler.Dom 0.1.0-preview.13, Broiler.HtmlBridge 0.1.0-preview.18, Broiler.HTML 0.1.0-preview.19,
Broiler.Layout 0.1.0-preview.17, Broiler.CSS 0.1.0-preview.12, Broiler.JavaScript.Engine
0.1.0-preview.6), against the upstream repositories' `main` of the same day.

## Reproducing it

The command line runs the page on the window's pipeline and reads Acid3's own failure log, the one a
shift-click on the heading would show:

```bash
dotnet run --project src/Broiler.Browser.Cli/Broiler.Browser.Cli.csproj -c Release -- \
  --evaluate-page http://acid3.acidtests.org/ --evaluate score --evaluate log --output acid3.json
```

It reports `score` 91 and these nine failures:

| Test | Acid3's message | Bucket |
| --- | --- | --- |
| 01 | `Roses` | 1, DOM Traversal |
| 06 | `Broiler.Dom.DomException: The current node must be within the TreeWalker root.` | 1, DOM Traversal |
| 29 | `Cannot define property` | 2, DOM2 Core |
| 35 | `expected '0' but got '1' - root element, with no parent node, claims to be a :first-child` | 3, Selectors |
| 43 | `expected '1' but got '3' - input element didn't match :checked` | 3, Selectors |
| 49 | `Cannot define property` | 4, Tables |
| 50 | `Cannot define property` | 4, Tables |
| 51 | `Cannot define property` | 4, Tables |
| 80 | `Script in XHTML didn't execute` | 5, Acid3 competition |

Buckets 1 to 5 each miss one to three tests, which is why the first five boxes stay silver and only
the sixth, ECMAScript, turns purple.

## The failures, by cause

| # | Cause | Tests | Where the fix goes | Verified |
| --- | --- | --- | --- | --- |
| 1 | The TreeWalker departs from the DOM Standard's algorithms | 01, 06 | Broiler.DOM | Broiler-Platform/Broiler.DOM#31 |
| 2 | `table.tBodies`, `.rows` and `.cells` throw | 29, 49, 50, 51 | Broiler.HtmlBridge | Broiler-Platform/Broiler.HtmlBridge#22 |
| 3 | The root element matches `:first-child` | 35 | Broiler.CSS | Broiler-Platform/Broiler.CSS#66 |
| 4 | A computed style keeps `:checked` from before a `click()` | 43 | Broiler.HtmlBridge | Broiler-Platform/Broiler.HtmlBridge#22 |
| 5 | A script in an XHTML frame served as `text/xml` does not run | 80 | Broiler.HtmlBridge | Broiler-Platform/Broiler.HtmlBridge#22 |

Each fix was first tried as a prototype built from the upstream repository's `main` and dropped
into a copy of the command line's output folder in place of the packaged assembly: with 1 and 2
Acid3 scored 97/100, with 1 to 4 99/100, and with all five 100/100. The pull requests carry the
fixes with tests that fail without them, and the same check against assemblies built from the three
pull-request branches scores 100/100. Once their packages are published, this repository takes them
in `Directory.Packages.props`.

### 1. The TreeWalker departs from the DOM Standard's algorithms (tests 01 and 06)

`DomTreeWalker` in Broiler.DOM's `Broiler.Dom/DomTraversal.cs` has its own versions of the walker's
methods, and they call the filter where the standard does not:

- `NextNode()` filters the current node before it descends (`Evaluate(node) != Reject` on the
  starting node). The standard starts from `FILTER_ACCEPT` and never filters the node it leaves. Test
  01's filter throws on its fourth call, which lands on `<html>` instead of `<head>`. The next call,
  `assertEquals(w.nextNode(), …)`, filters `<html>` again (the fifth call, which accepts) and then
  `<head>` (the sixth, which throws), so "Roses" escapes from an `assertEquals` and fails the test.
- `PreviousNode()` filters each node it descends through twice: once to decide whether to descend,
  and again to decide whether to return it.
- `TraverseSiblings()` filters the root when it climbs to it; the standard returns null at the root
  without asking the filter. Test 01 checks that `previousSibling()` does not call the filter there.
- `TraverseSiblings()` and `TraverseChildren()` do not climb back out of a skipped subtree, as the
  methods' own `Broiler-Falsified-If` notes say.
- The `CurrentNode` setter throws `NotFoundError` for a node outside the root. The standard's setter
  takes any node, and a walk that starts outside the root continues from there. Test 06 re-grafts the
  current node outside the root and expects `previousNode()` to return `<title>`; the walk finds it
  and then throws assigning it.

The fix is to transcribe the standard's `parentNode()`, traverse-children, traverse-siblings,
`nextNode()` and `previousNode()` (WHATWG DOM §6.2) and drop the setter's check. A prototype that does
exactly that passes Broiler.DOM's 181 tests and turns tests 01 and 06 green.

### 2. `table.tBodies`, `.rows` and `.cells` throw (tests 29, 49, 50 and 51)

Every read of `HTMLTableElement.tBodies`, `HTMLTableElement.rows`, `HTMLTableSectionElement.rows`
and `HTMLTableRowElement.cells` throws `TypeError: Cannot define property`. Broiler.HtmlBridge's
`TableBinding.WrapCollection` (`src/Broiler.HtmlBridge.Dom/Features/TableBinding.cs`) builds each
collection as a JavaScript array and then redefines the array's `length` as an accessor:

```csharp
var array = realm.NewArray(items);
realm.DefineAccessor(array, "length", (in _) => JsValue.Number(items.Length), null);
```

An array's `length` is a non-configurable data property, and ECMAScript refuses to turn one into an
accessor; Broiler.JS now refuses it too. The accessor adds nothing, since the array's own `length`
already is `items.Length`. Returning `realm.NewArray(items)` alone makes all four tests pass. The
binding is the only collection in Broiler.HtmlBridge built on `NewArray`; the others that define a
`length` accessor do it on plain or exotic objects. The collections are still snapshots where the
HTML Standard has live `HTMLCollection`s, which Acid3 does not notice because it reads the property
again after each change.

### 3. The root element matches `:first-child` (test 35)

Broiler.CSS's `CssSelectorMatcher` answers both `getComputedStyle` (through `CssStyleEngine`) and
`Element.matches` (through Broiler.HtmlBridge's `DomBridge.MatchesSelector`). It finds an element's
siblings with

```csharp
private static List<DomElement> ElementSiblings(DomElement element) => element.ParentNode?.ChildNodes.OfType<DomElement>().ToList() ?? [];
```

(`Broiler.CSS.Dom/CssSelectorMatcher.cs:959`). The root's parent is the document, whose only element
child is the root, so the root counts as child 1 of 1, and every child-indexed pseudo-class matches it:
`:first-child`, `:last-child`, `:only-child`, `:nth-child(1)`, `:nth-last-child(1)` and the five
`-of-type` forms all answer true for `document.documentElement`. Chromium and Firefox match none of
them on the root. Tests 36, 37, 39 and 40 exercise those pseudo-classes but never on the root; only
test 35 asks. A document parent should give no siblings:

```csharp
private static List<DomElement> ElementSiblings(DomElement element) =>
    element.ParentNode is { } parent and not DomDocument
        ? [.. parent.ChildNodes.OfType<DomElement>()]
        : [];
```

Elements in a fragment or a shadow root keep their siblings, since `DomShadowRoot` derives from
`DomDocumentFragment`, not `DomDocument`. The prototype passes test 35.

### 4. A computed style keeps `:checked` from before a `click()` (test 43)

`input.click()` checks the box: `input.checked` and `input.matches(':checked')` are both true
afterwards. But `getComputedStyle` still returns the style it computed before the click, when only
`:enabled` matched. `CssStyleEngine` caches computed styles per element (`CssStyleEngine.cs:70`) and
clears them on the document's `Mutated` event (`CssStyleEngine.Computed.cs:592`). Checkedness is not
in the DOM: it lives in Broiler.Dom.Html's `HtmlFormState`, whose writers — `click()`'s
pre-activation, the `.checked` setter and radio-group exclusivity — end in `OnStateChanged`.
Broiler.HtmlBridge wires that to bump the runtime epoch and note a render change
(`src/Broiler.HtmlBridge.Dom/DomBridge.cs:374`), and clears no style. `matches` has no cache, which
is why it answers right.

Probing the rest of test 43, which stops at its first failure: `input.checked = false`,
`input2.click()` on a radio and `input1.checked = true` would each leave a stale style too;
`disabled`, `setAttribute("checked", …)` and `type = "text"` are attribute writes and invalidate. Any
unrelated attribute write also "fixes" the stale style, which is how the bug hides.

Clearing the computed styles in `OnStateChanged` fixes it:

```csharp
_formState.OnStateChanged = () =>
{
    BridgeRuntimeStateEpoch.Bump();
    NoteRenderStateChange();
    if (_renderProjectionDepth == 0)
        ClearComputedPropsCache();
};
```

The guard matters because cloning elements for a render projection copies control state and raises
`OnStateChanged` midway through building it. Clearing every computed style on each change is coarse;
if a text field's keystrokes make it costly, `OnStateChanged` could name its element and clear only
the styles that element's rules reach, as hover does — a Broiler.DOM API change. The prototype passes
test 43. (The initialiser `OnStateChanged = BridgeRuntimeStateEpoch.Bump` at
`Hosts.Elements.cs:275` is dead: the constructor overwrites it.)

### 5. A script in an XHTML frame served as `text/xml` does not run (test 80)

Test 65 loads `xhtml.1`, `xhtml.2` and `xhtml.3` into iframes, and test 80 expects only the first to
have called `parent.notify`: `xhtml.1` is well-formed XHTML, `xhtml.2` is not well-formed, and
`xhtml.3` puts its root in the namespace `http://www.w3.org/1999/xhtml#`. acid3.acidtests.org serves
all three as `text/xml`. Broiler.HtmlBridge sends every XML type to `BuildSubDocumentFromXml`
(`src/Broiler.HtmlBridge.Dom/DomBridge/SubDocuments.Loading.cs`), which builds the document but runs
its scripts only when the type is exactly `application/xhtml+xml`. The same script did run in frames
served as `application/xhtml+xml` and `text/html`, from a `data:` URL and from `srcdoc`, and did not in
`text/xml` or `application/xml` ones — so the frame, `parent` and `notify` all work, and the type check
is all that stops it.

A browser runs an element's script when the element is an HTML `script` — `script` in the XHTML
namespace — whatever XML type brought it. A prototype that runs the scripts whenever the root is in
the XHTML namespace makes test 80 pass, and Acid3 scores **100/100** with all five prototypes;
`xhtml.2` still fails to parse and `xhtml.3` still runs nothing. The proper fix decides per element,
on the parsed `XElement`, because `BuildDomElementFromXElement` makes every element from its
lower-cased local name and drops its namespace, so the DOM cannot tell `xhtml.1` from `xhtml.3`
afterwards. And it runs the scripts in the frame's window after the frame document is registered, as
the HTML path does; this path runs them in the parent's context, before registration, which
`parent.notify` happens not to notice.

## What the screenshot shows, and why

None of these costs a point; each makes the page differ from the reference rendering.

| What shows | Cause | Where the fix goes |
| --- | --- | --- |
| The score at 500px | `inherit` takes the parent's specified `5em`, not its computed 100px | Broiler.CSS — fixed, Broiler-Platform/Broiler.CSS#68 |
| A red square top right | `::after` text sits on the positioned box, not in a child, so it is laid out in the flow | Broiler.HTML — fixed, Broiler-Platform/Broiler.HTML#246 |
| "FAIL" above the heading | An `<object>` whose data loaded still renders its fallback | Broiler.HtmlBridge, Broiler.Layout — fixed, Broiler-Platform/Broiler.HtmlBridge#23, Broiler-Platform/Broiler.Layout#132 |
| A red "YOU SHOULD NOT SEE THIS AT ALL" | A frame's navigation is not recorded for `:visited` | Broiler.Browser — fixed |
| Test 28's "FAIL", below the first screen | Selector parsing drops an escaped space: `#\ ` matches nothing | Broiler.CSS — fixed, Broiler-Platform/Broiler.CSS#67 |
| A red heading, `--capture-image` only | `HtmlRender` applies a stylesheet served as `text/html` | Broiler.HTML |

### The score is drawn at 500px

`#result` is `font-size: 5em` of the root's 20px, 100px, and the reference draws "100/100" at
100px. Its three spans match `* { font: inherit; }`. The renderer's computed styles give the paragraph
75pt (100px) and each span 375pt (500px). The same happens under `#instructions` (`0.8em`): its span
is 9.6pt where it should be 12pt. A plain `font-size: inherit` compounds the same way.

`inherit` is meant to take the parent's computed value, but Broiler.CSS.Dom's `CssStyleEngine` hands
the renderer the parent's specified one. `ComputeStyle` keeps a font size as written (`5em`) and the
keyword `inherit` as written, and then `FoldInheritKeyword` (`CssStyleEngine.cs:606` and `652`)
replaces a child's `inherit` with the parent's value from that map. So `#score` gets `5em`, and the
renderer resolves it again against its parent's 100px. `#result`'s own `font-family: inherit` folds to
the body's literal `inherit` in the same way, which is why every run below `<body>` is drawn in a
fallback face rather than Arial; the analysis report counts 46 such runs.

The renderer would get it right by itself. Broiler.Layout's `CssBoxProperties` font-size setter copies
the parent box's computed size for `inherit`, and Broiler.HTML's `DomParser.InheritStyle` runs first,
but it never sees an `inherit` because the engine has already folded it. The fix spans two packages
that ship together: stop folding `inherit` for inherited properties in the engine, and resolve it
against the parent box where Broiler.HTML's `SharedRendererCascade.ProjectCascadedStyle` projects the
cascade, as `DomParser` already does for pseudo-elements. The quicker engine-only alternative is to
drop an inherited property's `inherit` instead of copying it, so the renderer's own inheritance
stands. Its catch is that a presentational hint such as `<font size>` would then beat an author's
`inherit`.

### A red square in the body's top-right corner

The body's background is a red 20×20 PNG at `99.8392283% 1px`. In a browser it is covered by
`map::after { position: absolute; top: 18px; left: 638px; content: "X"; background: fuchsia;
color: white; font: 20px/1 AcidAhemTest; }`, whose `@font-face` font draws "X" as a full-em square: a
white 20px block. Broiler's fragment tree has the `::after` box at 638,18, but 0×0.

The pseudo-element is generated, its font loads and is measured at a 20px advance, and its position
is right. What goes wrong is where the generated text lives. Broiler.HTML's `CreatePseudoElementBox`
puts it on the pseudo box itself (`pseudoBox.Text`, `DomParser.cs:586`), where a real element's text
is an anonymous child box. Broiler.Layout places an absolutely positioned box's content only through
its children. So the parent's line layout picks up the box's own words and draws them in the normal
flow, white on white, at (57,828), where Acid3's display list shows "X". Meanwhile the positioned box
has no children, ends up 0×0, and its fuchsia background covers nothing. A minimal page shows the
same with Arial, outside `<map>`, and with `display: block`. A real `<span style="position:absolute">`
is placed correctly, and so is an absolutely positioned `::after` whose `content: url(…)` makes a
child box. A `float: right` `::after` with text paints nothing at all, for the same reason.

The fix is to put the text in an anonymous inline child of the pseudo box, as
`ApplySummaryDisclosureMarker` already does for a summary's marker. That covers the red. It will
still not be the reference's solid white: web fonts are measured but drawn with an installed face
(item 2 of `found-not-fixed-2026-10-07.md`), so the square will show a white "X" on fuchsia.

### "FAIL" above the heading

Test 16 appends nested objects: `support-a.png` (a 404) holding `support-b.png` (a 200 `text/html`
page with a transparent body) holding `support-c.png` holding the text "FAIL". The HTML Standard has
the first show its fallback, the second, since its data loaded, show the nested page and not its own
fallback; so neither the third object nor "FAIL" is rendered. Broiler renders all three objects and
the text. Broiler.HtmlBridge knows each object's outcome — the first has no `contentDocument`, the
second has a document titled "FAIL" — but nothing passes it to the renderer, which decides from the
markup alone:

- Broiler.Layout's `CssBoxHelper` (`Engine/CssBoxHelper.cs:20`) draws an `<object>` as an image only
  when its `data` is a `data:image` URL, and `FragmentTreeBuilder` (`IR/FragmentTreeBuilder.cs:705`)
  treats one as a nested document only when its `type` or its URL's extension says so; `support-b.png`
  has neither.
- Broiler.HTML's `DomParser.CorrectObjectBoxes` (`Broiler.HTML.Orchestration/Parse/DomParser.cs:1791`)
  removes fallback children only from those `data:image` objects.
- Broiler.HtmlBridge's render projection (`ProjectScriptedFrameDocuments`,
  `DomBridge/Serialization.Rendering.cs:268`) stamps a frame's document onto the render copy only when
  scripts changed it, and never records an object's outcome.

The fix belongs in the render projection, which is a copy: drop an object's children from it when the
object's data loaded, stamp the loaded document onto it, and give a loaded image its response's type
so the renderer draws it; keep the children only when the load failed. The bridge also loads the
third object, which the standard never does while its parent object shows its content.
`HtmlPostProcessor.StripObjectContent` in this repository is unused and would be wrong here, since it
strips the first object's correct fallback too.

### A red "YOU SHOULD NOT SEE THIS AT ALL"

Test 48 adds `<a id="linktest" class="pending" href="empty.html?<time>">` and loads that same URL into
the `selectors` iframe, whose `onload` removes the class. The sheet makes `#linktest:link` red and
`#linktest.pending, #linktest:visited` white; in Chromium the link has been visited, through the frame,
so it is white on white. Scripts are not meant to see this — `matches(':visited')` false and a red
computed colour are what visited-link privacy requires, and what Broiler answers — but the paint is.

Broiler.HTML asks the host whether a link is visited, and this repository answers from
`BrowserApp._visitedUrls`. Only top-level navigations wrote to it (`NoteVisited`): a frame navigation
goes through Broiler.HtmlBridge's fetch on the profile's network and was never noted. **Fixed here:**
`FrameVisitTransport` wraps the window's network and notes every URL of a nested navigation's
response (a frame or an object, any status, the whole redirect chain), so the link is painted white
on the next repaint. `WindowFrameTests` covers it, with Acid3's own sequence. `Broiler.Cli --analyze` cannot show this either way: its render
(`Analysis/RenderProbe.cs:147`) sets no visited-link check, so it paints every link `:link`.

### Test 28's "FAIL", further down the page

Test 28 appends `<div id=" ">FAIL</div>` to the body, which the rule `#\  { color: transparent; …
position: fixed; … }` hides. Broiler draws it static and black at y=950, below the first screen.

The matcher compares ids correctly; it is the parsing around it that drops the escaped space.
Broiler.CSS's `CssParser` trims each rule's selector text (`CssParser.cs:117`), which deletes the
space that `\` escapes. `#\ ` becomes `#\`, an empty id. The same plain `Trim()` is at
`CssSelectorParser.cs:12`, `CssSelectorMatcher.cs:71` and `CssStyleEngine.cs:1129`. And both selector
splitters, `CssSelectorMatcher.SplitPartsUncached` and `CssSelectorParser.SplitCompounds`, take an
escaped space inside a selector for a descendant combinator. `#q\ r` is filed in the rule index under
the type `r` and never reaches a `<div>`. `#\ `, `#\20 x` and `#a\ b` all fail to match; the same
ids written without whitespace (`#\20{`, `#a\000020b{`) match. The fix is a trim that keeps
whitespace escaped by a backslash, at all four sites, and splitters that copy an escape whole.

That fix must not ship alone. Once the selector matches, the rule's second declaration,
`color: hsla(0, 0, 0, 1)`, is invalid (hsla needs percentages), so `color: transparent` should
stand. But `CssStyleEngine.Values.cs:573`'s colour check accepts anything that does not start with
an unknown vendor prefix, so the engine takes the invalid colour and paints "FAIL" black at 40px in
the top-left corner, in plain sight. Broiler.CSS's own `CssValueParser.TryParseColor` already rejects
the value; the check should ask it, or `CssColor4.TryParse`, for functional colours.

### A red heading, in `--capture-image` only

Acid3 links `empty.css`, which is served as `text/html` and makes `h1` red if a browser applies it.
The window and `--analyze` refuse it ("The response is not acceptable for Style: text/html") and draw
the heading black. `Broiler.Cli --capture-image` draws it red, with or without the prototypes: its
image comes from Broiler.HTML.Image's `HtmlRender` (`CaptureService.cs:343`–`373`), which fetches
stylesheets itself and does not check their type. The window is unaffected; a capture of Acid3 is
not a faithful picture of what the window shows until `HtmlRender` refuses a stylesheet that is not
`text/css`, as the window's container does.

## Timing

Acid3 also counts a test that took more than 33 ms as less than perfect. 29 of the 91 passing tests
did in this run, and test 26 took 2.7 s. Test 26 runs `new Date().valueOf() * 2.813435e-9 - 2412` iterations,
2,628 on this date, of a loop that creates a text node and an element, inserts and removes it in the
body and builds an object from a fresh function expression. Measured on the settled page:

| Operation | Per iteration |
| --- | --- |
| `new (function (x) { return { … } })(1)` | 196 µs |
| `insertBefore` and `removeChild` of an `<a>` in the body | 203 µs, rising to 548–585 µs over the next 3,000 |
| `appendChild` and `removeChild` on a detached `<p>` | 256–361 µs |
| `new Date()` | 3 µs |

A function expression costs Broiler.JS about 200 µs each time it is evaluated, which looks like a
compile per evaluation rather than a closure over code compiled once; and a DOM mutation costs
hundreds of microseconds, more the more nodes the page has made, even on a node outside the
document. Both are speed work in Broiler.JS and Broiler.DOM or Broiler.HtmlBridge, not correctness.

## Still open

- **The corner square is fuchsia with a white "X", not solid white.** The `::after` box is now
  placed and painted, but web fonts are measured and not drawn (item 2 of
  `found-not-fixed-2026-10-07.md`), so AcidAhemTest's full-em "X" is drawn in an installed face.
- **An absolutely positioned box's words are also laid out in its parent's line.** Broiler.Layout
  places the text of a positioned inline box (`map::after` here, or a real
  `<span style="position:absolute">`) in its parent's line as well as at its offsets, which makes that
  line taller: on Acid3 the instructions paragraph sits about 9px lower than the reference.
- **The score sits a little higher than the reference's.** "100/100" is drawn about 35px above the
  reference's top of 230px.
- **Timing**, as above.
