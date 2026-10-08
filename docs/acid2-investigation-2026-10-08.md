# Where Acid2 differs from its reference

**Status:** three of four differences fixed and released, by Broiler-Platform/Broiler.CSS#70
(Broiler.CSS 0.1.0-preview.16), Broiler-Platform/Broiler.HTML#248 (Broiler.HTML 0.1.0-preview.23)
and this repository's `RenderListReplay`. One remains, under "Still open". The orange dither the
Windows window drew behind the eyes at 150% is fixed too, by Broiler.Graphics 0.1.0-preview.11 and
Broiler.HTML 0.1.0-preview.25.
**Components:** Broiler.CSS, Broiler.HTML, Broiler.Browser, Broiler.Layout.
**Affects:** the Broiler.Browser window and `Broiler.Cli --analyze`, which draw the same way.

Measured on 2026-10-08 with `Broiler.Cli --analyze 'http://acid2.acidtests.org/#top'`. The face
was compared pixel for pixel against the test's own `reference.png`, which `reference.html` places
at (72, 108) below the "Hello World!" heading. The face is built on a 12px grid, so each difference
is named by its row of the face, counting from 0 at the scalp.

| Row | Reference | Broiler | Cause |
| --- | --- | --- | --- |
| 1 | `...##yyyy##...` | `..............` | `[class=second\ two]` not read |
| 6–7 | a black diamond | a black square | border corners not split along their diagonal |
| 8 | `#yyyyyyyyyyyy#` | `#yy########yy#` | `.nose div :after` taken as `.nose div:after` |
| 12–13 | | 1px lower | the chin's line is 12.3px tall |

Before these fixes, 2,388 of the face's 28,224 pixels differed by more than 40 in some channel.
With them, 120 do. 48 of those are the nose's antialiased diagonals, which the reference draws
aliased; the other 72 are the chin.

## Row 1: an attribute value with an escaped space

The second line of the face is an absolutely positioned `blockquote` with black side borders,
shrink-wrapped around a 48×12 yellow float:

```css
[class~=one][class~=first] [class=second\ two][class="second two"] { float: right; width: 48px; height: 12px; background: yellow; … }
```

Broiler.CSS's attribute pattern ended an unquoted value at whitespace and never decoded escapes,
so `[class=second\ two]` was not read, the rule never matched, and the `address` was an empty
block. The blockquote then had no content, and its borders had no height. A value is now a string
or an identifier, either holding escapes, decoded before it is compared.

## Row 8: a pseudo-element after a combinator

```css
.nose div    :after { display: block; border-style: solid solid none; … }
```

This gives an after box to each element inside a div inside the nose, which is only the innermost
div. Broiler.CSS cut `:after` off and trimmed what was left, leaving `.nose div`, which matches the
nose's own div as well. That div's after box drew a black top border across the face under the
nose. A pseudo-element after whitespace or a combinator now leaves a universal selector in its
place: `.nose div *`.

## Rows 6–7: border corners

The nose is two boxes of nothing but borders. The first has a black bottom between two yellow
sides and no top; the second has a black top between two yellow sides and no bottom. CSS splits
each corner between its two sides along the diagonal from the outer corner to the inner one, which
turns the two boxes into a black diamond.

The window's render list (`HtmlGraphicsRenderList`, in Broiler.HTML) drew each side as a rectangle
the full length of its edge, so the side drawn last covered the corner. It now draws a corner whose
two sides are opaque, solid-filled and of different colours on its own: the square in one side's
colour, then the other side's half over it as a triangle. Other corners keep the rectangles.

This also needed a fix here. `RenderListReplay` copies a page's render commands into the window's
list and had no case for triangles, so it dropped them. Before that was fixed, the nose
disappeared entirely.

## Rows 3–4 in the Windows window: an orange dither behind the eyes

At 150% display scaling, the Windows build drew the area behind the eyes as an orange dither. The
Linux window and `Broiler.Cli` drew it solid yellow.

The yellow there is two layers of a 2×2 checkerboard, yellow at two corners and transparent at the
other two, tiled over red (`#eyes-b`, and the innermost `object`'s background shifted `1px 0`).
The second layer is a pixel to the right of the first, so together they cover everything. Direct2D
drew every image with linear interpolation. At 150% a 2px tile is 3 device pixels wide, so its
middle pixel blended yellow with transparent, and the red showed through. The CPU renderer samples
nearest-neighbour and covers only the pixels whose centres a tile covers, so it never showed this.

- **Broiler.Graphics** (Broiler-Platform/Broiler.Graphics#37) gives `DrawImage` a `BImageSampling`.
  Direct2D draws a `NearestNeighbor` image with nearest-neighbour interpolation and aliased edges,
  as the CPU renderer does.
- **Broiler.Graphics** (Broiler-Platform/Broiler.Graphics#38) keeps the four-parameter forms that
  preview.10 had replaced. Without them, packages built against older versions threw
  `MissingMethodException`.
- **Broiler.HTML** (Broiler-Platform/Broiler.HTML#249) asks for nearest-neighbour sampling only
  for a background tile drawn at its image's own size. A tile the page scales stays smooth.
- **Here:** both replay paths, `RenderListReplay` and the Linux `CpuReplayRenderer`, now pass the
  sampling on.

A Direct2D test in Broiler.Graphics' Windows CI renders the eyes' two layers at 150%. It finds
every device pixel yellow with nearest-neighbour sampling, and blended pixels with linear sampling.

## Still open

- **The chin and the two rows below it sit 1px low.** `.chin` holds an inline `div` in
  `font: 2px/4px serif` on a 12px line. Its inline box should fit inside the strut; Broiler.Layout
  makes the line 12.28px tall.

  `HalfLeading` floors half the leading to a whole pixel, as Blink does. Blink floors it against a
  font's ascent and descent rounded to whole pixels. Broiler.Layout uses fractional ones, 0.8 and
  0.2 of the font's height. For the 2px text, all 1.68px of the leading goes below the glyphs, and
  its box ends 0.28px below the strut.

  Rounding the ascent and descent as Blink does fixes the chin, but it moves text throughout the
  engine: 352 of Broiler.Layout's 3,015 tests fail. That change needs to be made deliberately,
  with the tests reviewed, rather than to move one line of Acid2 by 0.3px.
