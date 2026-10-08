# Where Acid2 differs from its reference

**Status:** three of four differences fixed and released, by Broiler-Platform/Broiler.CSS#70
(Broiler.CSS 0.1.0-preview.16), Broiler-Platform/Broiler.HTML#248 (Broiler.HTML 0.1.0-preview.23)
and this repository's `RenderListReplay`. One remains, under "Still open".
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
