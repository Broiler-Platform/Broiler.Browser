# reCAPTCHA's image challenge: what a click on a tile did, and what it costs

**Status:** fixed, in Broiler.HtmlBridge 0.1.0-preview.18, Broiler.Graphics 0.1.0-preview.8, Broiler.Layout
0.1.0-preview.17 and Broiler.HTML 0.1.0-preview.19, which the window takes; the gaps at the end are open.
Found on 2026-10-07 on recaptcha-demo.appspot.com's v2 checkbox demo, whose checkbox opens reCAPTCHA's
image challenge.
**Components:** Broiler.HtmlBridge, Broiler.Graphics, Broiler.Layout, Broiler.HTML, Broiler.Browser.
**Affects:** the Broiler.Browser window.

The challenge showed its picture correctly, but a click on a tile selected another one, and a selected
tile was drawn as a scrap of some other part of the picture. Each click took one to two and a half
seconds. Chromium's behaviour was measured unless an item says otherwise; the window's was measured by
driving `BrowserApp` headlessly, on Linux.

## What was wrong, and where it is fixed

| Component | Commit | What it fixes |
| --- | --- | --- |
| Broiler.HtmlBridge | 6f06b1e | A point hits only what an element shows: an ancestor's `overflow` clip, followed along the containing blocks as CSS clips, keeps a press off the part of an element it hides |
| Broiler.HtmlBridge | d23b479 | `querySelector`, `querySelectorAll`, `matches` and `closest` match a selector list; `document.querySelectorAll('div, p')` found nothing |
| Broiler.Graphics | 79c43c3 | A transform pushed inside another applies first, in every backend that composes them (the CPU renderer the Linux, Vulkan and Android backends draw with, Direct2D, and the WebAssembly canvas planner) |
| Broiler.Layout | a270810 | An absolutely positioned box sized or placed by its containing block's height (`top` and `bottom`, `bottom`, a percentage `top` or `height`) gets that height when it comes from the containing block's content |
| Broiler.HTML | de97254 | A container that is measured and never painted reads image sizes from their headers (`HtmlContainer.ImageSizesOnly`), and an image whose body the subresource cache keeps is decoded once for as long as it is kept |

What each was, in the challenge:

- **The wrong tile.** The challenge shows one picture across its tiles: each tile's image is the whole
  picture, three or four times the tile's size, shifted so that the tile's `overflow: hidden` wrapper
  shows its part. The bridge's hit test ignored the clip, so every tile's image covered the whole grid,
  and the last tile's, painted last, took every press. A click anywhere selected the last tile.
- **The scrap of picture.** A selected tile's wrapper is scaled to 80% about its centre. The window
  draws the page under a translation to its viewport, and the backends applied the page's transform
  after that translation instead of before it, so the scale was about a point the translation had
  moved: on a page the tile was drawn 14px too high, (1 - 0.8) times the 72px of the toolbar, and in the
  challenge's frame, further from the window's corner, most of it fell outside the tile.
- **No tick.** A selected tile is marked with a tick drawn by an empty box with all four insets 0. Its
  containing block's height comes from its content, which the box was laid out before, so it was as
  tall as nothing.
- **Selector lists** did not show in the challenge itself; they were found while measuring it, and
  fail any page script that queries with one.

## Measured in the window, with all five changes

On the v2 checkbox demo, three clicks on tiles, after the challenge had opened:

| | Before | After |
| --- | --- | --- |
| The tile a click selects | the last one, wherever the click is | the one clicked |
| A selected tile | a scrap of the picture, partly outside the tile | scaled to 80% in place, with its tick |
| Dispatching the click | 1.0 to 2.4 s | 0.27 to 0.43 s |
| Until the window has settled | 1.4 to 2.6 s | 0.55 to 0.67 s |

The timings before are with the first four changes and without Broiler.HTML's; with the hit-test change
alone, two clicks dispatched in 1.45 and 1.8 s.

Most of the time went to decoding the challenge's JPEG. The bridge lays the page out in a container of
its own for every layout question a script asks or a press needs, three times a click, and each layout
decoded every image though it reads only their sizes: the picture is in every tile, 9 or 16, so 18 to 48 decodes
a click, 0.7 to 1.5 s. The window's own containers parse the page again after each change, and decoded
every image again though the cache had kept the bytes: 0.25 to 0.45 s more.

## What the window takes

The window takes the components' packages: Broiler.HtmlBridge 0.1.0-preview.18, Broiler.Graphics
0.1.0-preview.8 with its backends, Broiler.Layout 0.1.0-preview.17 and Broiler.HTML 0.1.0-preview.19. And
`HeadlessLayoutView`, the container the bridge asks for geometry, sets `ImageSizesOnly = true`: it lays the
page out and never paints it. The timings above were measured with that line, against local builds of the
code these packages were published from.

## Gaps found and not fixed

| # | Gap | Where the fix goes |
| --- | --- | --- |
| 1 | Every render list build copies every image's pixels twice | Broiler.HTML.Graphics, Broiler.Browser |
| 2 | The page, its frames' documents in it, is serialized whole and parsed again after each change | Broiler.HtmlBridge, Broiler.Browser |
| 3 | A press lays the whole page out three times for its hit tests | Broiler.HtmlBridge |
| 4 | An image the page has not shown before is fetched on the window's thread, inside a layout | Broiler.Browser, Broiler.HTML |
| 5 | A hit test clips to a padding box's rectangle, not its rounded corners | Broiler.HtmlBridge |
| 6 | A positioned table cell's inset-sized boxes do not follow the height the table gives the cell | Broiler.Layout |

### 1. Every render list build copies every image's pixels twice

`HtmlGraphicsRenderListBuilder.GetImage` turns each image into a pixel buffer, a copy, and hands it to
the renderer, which copies it again; the next build does both again for the same picture. Across the
challenge's run that was about 0.6 s of copying, and 165 to 188 ms of the 236 to 277 ms the window takes
to draw after a click on a tile. The renderer's images could be kept from one build to the next for the same
decoded picture.

### 2. The page is serialized whole and parsed again after each change

After each change the bridge serializes the page, with each frame's document stamped into it as an
attribute, and the window parses the result again. Across the run that was 0.46 s of serializing and
0.4 s more of escaping the frames' documents into their attribute values.
This is item 4 of `found-not-fixed-2026-10-07.md` at another scale; the fix is the same incremental path
between the session and the renderer.

### 3. A press lays the whole page out three times

The move, the press and the release each hit-test, and a listener or a hover change between them makes
the bridge's geometry stale, so each lays the page out again: 50 to 70 ms each in the challenge's frame,
even where the change was only to hover or active styles that move nothing.

### 4. A new image is fetched on the window's thread

The window's containers load images synchronously (`AvoidAsyncImagesLoading`), so when reCAPTCHA replaces
a tile's picture, the frame's next layout fetches it over the network on the window's thread: 290 ms
once in the run.

### 5. A hit test clips to rectangles

An ancestor with `overflow: hidden` and `border-radius` clips its content to its rounded padding box; the
hit test clips to the rectangle, so a press in a rounded corner still reaches the content there.

### 6. A positioned table cell's inset-sized boxes

Broiler.Layout lays an absolutely positioned box out again once its containing block has its height, but
a table cell is given its final height by the table algorithm after that, and its inset-sized boxes keep
the content height.
