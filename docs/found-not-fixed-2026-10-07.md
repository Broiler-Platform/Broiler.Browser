# Fixes waiting on component releases, and gaps found while making them

**Status:** the fixes are on unmerged component branches; the gaps are open, not fixed. Found on
2026-10-07 while making html5test's hover fast, delivering scroll the way Chromium does, and bringing
Google's reCAPTCHA demos level with Chromium: recaptcha-demo.appspot.com (the index, the v2 checkbox,
v2 invisible and v3 demos) and google.com/recaptcha/api2/demo.
**Components:** Broiler.CSS, Broiler.HtmlBridge, Broiler.Layout, Broiler.HTML, Broiler.JS,
Broiler.Graphics, Broiler.Browser.
**Affects:** the Broiler.Browser window.

Chromium's behaviour was measured unless an item says otherwise.

## What the window needs to take

The component fixes are on each repository's `claude/charming-fermi-a98xil` branch. This repository
can only take published packages, so they reach the window through releases, in this order:

1. **Broiler.CSS 0.1.0-preview.12**, Broiler.CSS and Broiler.CSS.Dom.
2. **Broiler.HtmlBridge 0.1.0-preview.17**, which pins Broiler.CSS.Dom 0.1.0-preview.12, and
   **Broiler.Layout 0.1.0-preview.16**.
3. **Broiler.HTML 0.1.0-preview.17**. It builds against Broiler.Layout 0.1.0-preview.15 as it stands,
   and gets the fieldset legend's new place from 0.1.0-preview.16 at run time.
4. This repository, taking all four.

With every fixed assembly put in place of the published one, this repository's suites pass in Release
(Broiler.Browser.Core.Tests 314, Broiler.Cli.Tests 218), so taking the packages needs no change here.

| Component | Commit | What it fixes |
| --- | --- | --- |
| Broiler.CSS | e9bab35 | Each element's custom properties are resolved once, and each selector is read once, instead of every element recomputing its ancestors' styles |
| Broiler.CSS | 0cc11ca | A host can clear the computed styles of just the elements a change reaches |
| Broiler.HtmlBridge | 167d26d | A page's layout and styles are kept through a pointer's moves |
| Broiler.HtmlBridge | ab32440 | A viewport's scroll fires at the document and bubbles, and `<body onscroll>` is the window's handler |
| Broiler.HtmlBridge | 2fe145b | A hover restyles only what its rules reach, and a repaint keeps the page's styles |
| Broiler.HtmlBridge | 163d9c6 | A document asks the network for each stylesheet once |
| Broiler.Layout | 7277c44 | A relatively positioned box's percentage offsets refer to its containing block: reCAPTCHA's image tiles each show their part of the picture |
| Broiler.Layout | 77fdbdc | `text-decoration: underline rgb(…)` keeps its colour, and `underline overline` draws both |
| Broiler.Layout | f405718 | A fieldset's legend sits inside the fieldset's box; a block button, input, select or textarea keeps its own width |
| Broiler.Layout | c050122 | A list marker stands on the item's first line (beside an `<h2>`, too); circle and square are ◦ and ▪ |
| Broiler.Layout | b97efd9 | A padded, bordered or indented block's wrapped lines start at its content edge, not that edge again further in; a box sized to its words keeps them on one line |
| Broiler.Layout | 6983a84 | Monospace text sized by a keyword is 13/16 of it: `code` and `kbd` are 13px in 16px text |
| Broiler.HTML | ef959fc | Links, `<u>`, `<s>` and every other inline text decoration are drawn; none was |
| Broiler.HTML | efea421 | A fieldset has its groove, padding and legend gap, and a form or fieldset no block margins |
| Broiler.HTML | 9a004ef | A natively drawn checkbox or radio drops the page's padding, and is 13px square inside Chromium's margins |
| Broiler.HTML | 2c2f49b | Disabled fields and buttons are greyed out |

What they change, measured in the window:

- **html5test's hover.** The first move after the page loads took about 5 s and each change of hovered
  table row about 2.3 s, with 3.4 s more for the next move. The first move now takes about 1.3 s, a row
  change 0.4–0.7 s, and a move within a row about 25 ms. What remains is item 4 below.
- **Scroll.** `<body onscroll>` fires, and so does a bubbling scroll listener on the document; the
  "Known gaps" entries for both in Broiler.HtmlBridge's `docs/html-control.md` are gone. A narrower
  one is there instead: where an `on…` handler runs among its target's listeners, so a parser-set
  `<body onscroll>` runs after a window listener a script added later, where Chromium runs it first.
- **reCAPTCHA.** The image challenge opens and shows its picture whole, tile by tile. The demo pages
  are drawn as Chromium draws them apart from the items below: links underlined, the form framed by
  its fieldset with the legend in its border, a "Submit" button as wide as its text, the index's
  bullets beside its headings and the sources link in 13px monospace, and the api2 demo's two radios
  alike, greyed out with the fields, as the page has them disabled.

This repository's own part is `HtmlFormControlHost`: a disabled checkbox, radio, drop-down or file
button is hosted disabled, so it is drawn greyed and takes no click.

## Gaps found and not fixed

| # | Gap | Where the fix goes |
| --- | --- | --- |
| 1 | A reCAPTCHA page takes 77–95 s to load, where Chromium takes about 4 s | Broiler.JS |
| 2 | Web fonts are measured but not drawn | Broiler.Graphics, Broiler.HTML.Graphics |
| 3 | Glyphs this machine's fonts lack come from Unifont, and a variation selector is drawn | Broiler.Graphics |
| 4 | A hover that changes the page re-parses and lays out the whole of it | Broiler.Browser, Broiler.HTML |
| 5 | The space between two decorated boxes of one link is not underlined | Broiler.HTML |
| 6 | Checkboxes and radios keep the page's border, and `appearance: none` still hosts a widget | Broiler.HTML, Broiler.Browser |
| 7 | A text field's width does not follow its font | Broiler.HTML, Broiler.Layout |
| 8 | Keyword font sizes differ from Chromium's above `large` | Broiler.Layout |
| 9 | Scripts read 16px for monospace text drawn at 13px | Broiler.CSS.Dom |
| 10 | A quirks-mode form has no 1em bottom margin | Broiler.HTML |
| 11 | A disabled `<select>` and a multiple select's list are not greyed | Broiler.HTML, Broiler.Browser |

## 1. A reCAPTCHA page takes 77–95 s to load

recaptcha-demo.appspot.com's v2 checkbox, explicit render, v2 invisible, v3 and Content Security Policy
demos, and google.com/recaptcha/api2/demo,
settle in 77–95 s in the window and in about 4 s in Chromium; the demos' index, which loads reCAPTCHA
v3 in the background, settles in 11–14 s. The widget is drawn well before the window says it is done.
A profile of a load puts the time in running reCAPTCHA's scripts: its web worker
(`api2/webworker.js`) ran for about 33 s, nearly all of it in function calls, and compiling scripts
took about 12 s. Neither waits on the network. The fix is in Broiler.JS's speed, not in the window.

## 2. Web fonts are measured but not drawn

reCAPTCHA's frames set their text in Roboto from Google Fonts. Layout measures it with Roboto's
advances, but the render list names its family and the backend draws with an installed face, so the
words drift: the checkbox reads "I'mnota robot" and the api2 demo's legend "SampleFormwithReCAPTCHA",
and in the image challenge's header letters overlap. The fix is a font the render list can carry to
the backend, so drawing uses the face layout measured.

## 3. Glyphs this machine's fonts lack come from Unifont

On the Linux machine used, the demos' "↩️ Home" and "Submit ↦" were drawn with Unifont's pixel glyphs,
and the emoji's variation selector (U+FE0F), which should not be drawn at all, as a hex box, though
DejaVu Sans has the arrows. Fallback should prefer an outline font that has the glyph, and should skip
default-ignorable code points. Not checked on Windows, where the fonts differ.

## 4. A hover that changes the page re-parses and lays out the whole of it

When the page's render version moves, the window hands the renderer the whole serialised document
(`ApplyPageDocument`), and on html5test that parse and layout is 300–650 ms of each row change. Only
the elements whose styles changed need restyling, and Broiler.HtmlBridge now knows which they are. The
fix is an incremental path between the session and the renderer, so a hover restyles in place.

## 5. The space between two decorated boxes of one link is not underlined

A decoration is handed down to the boxes that hold the words, and each is underlined across its own
words. In `<a>foo <b>bar</b></a>`, the space after "foo" belongs to the anonymous box of "foo ", whose
extent ends at its last word, so the underline has a gap there; Chromium draws it through. A single
text, the usual link, is underlined whole. The fix is to extend a box's decoration over its trailing
space when the next box on the line carries the same decoration from the same element.

## 6. Checkboxes and radios keep the page's border, and `appearance: none` still hosts a widget

Chromium drops a natively drawn checkbox's or radio's padding and border. Broiler.HTML now drops the
padding; the border stays, because nothing draws the widget where the window does not host one, and
the renderer's 1px box is what shows there. And the window hosts a widget over a checkbox or radio
the page draws itself with `appearance: none`, because the markup it reads does not carry the computed
appearance. The renderer could draw the widget itself, and the host could skip an element whose
`appearance` is `none`.

## 7. A text field's width does not follow its font

Chromium sizes a text field from its `size` and its font's average character width: the api2 demo's
13px Roboto fields are 169px wide there and 191px in the window, whose default sheet gives every
field `min-width: 173px`.

## 8. Keyword font sizes differ from Chromium's above `large`

Broiler.Layout's keyword table gives `x-large` 20px and `xx-large` 21.3px, where Chromium uses 24px
and 32px, and `xx-small` 10.7px where Chromium uses 9px.

## 9. Scripts read 16px for monospace text drawn at 13px

Broiler.Layout draws `code`, `kbd`, `samp` and `tt` at 13px now, but Broiler.CSS.Dom's computed style,
which `getComputedStyle` reads, still says 16px. The same 13/16 rule belongs there.

## 10. A quirks-mode form has no 1em bottom margin

The HTML Standard gives a form `margin-block-end: 1em` in quirks mode only. The default sheet no longer
gives a form CSS 2.1's 1em margins, which were wrong in standards mode, and it has no rules that apply
in one mode alone.

## 11. A disabled `<select>` and a multiple select's list are not greyed

Chromium draws a disabled select at 0.7 opacity with grey text. The default sheet now greys disabled
fields and buttons, not selects, and the window's multiple-select list has no disabled state to show.
