# The command line

`Broiler.Cli` (`src/Broiler.Browser.Cli`) loads a page without a window, runs it, and writes out what
it left: the document, an image of it, or what its JavaScript computed. It also carries two tools
that need no page at all, a layout fuzzer and an engine smoke test.

It is the command line from the Broiler repository (`src/Broiler.Cli` at `ac913fd4`, the last
commit on its `main`), which stayed there when the browser moved out on 2026-08-27. It came here on
2026-09-24, and on the way it stopped running pages its own way: it now runs them on the window's
pipeline.

```bash
dotnet run --project src/Broiler.Browser.Cli/Broiler.Browser.Cli.csproj -c Release -- --help
```

## What it does

| Command | Writes |
|---|---|
| `--url <URL> --output <FILE>` | The document the page's scripts left, as HTML — or as text, for a `.txt` output |
| `--capture-image <URL> --output <FILE>` | A PNG or JPEG of it, `--width` × `--height`, or the whole page with `--full-page`, or scrolled to a `#fragment` |
| `--evaluate-page <URL> --evaluate <EXPR>… --output <FILE.json>` | A JSON report of each expression's `typeof` and value, run on the page's own global after its load window settled, with the page's work settled between them |
| `--analyze <URL> --output-dir <DIR>` | Everything below about one page, and a ranked report of what looks wrong with its HTML, CSS, JavaScript, layout, rendering and network — see [Analysing a page](#analysing-a-page) |
| `--fuzz-layout [--count N] [--seed N]` | The seeds whose random documents break a layout invariant, each with a minimised reproduction |
| `--test-engines` | Whether the CSS model and the JavaScript engine this build composes answer a trivial question correctly |

`--url` and `--capture-image` take several inputs and `--output-dir`; each one is then captured in
its own child process, because the render path still keeps unsynchronised caches on process-wide
singletons. `--diagnostic-dir` records a bundle for a capture: every JavaScript failure as it
happens, the page's console, every document, script, stylesheet, fetch and sub-document it loaded,
the document its scripts left, and a summary ranking the failures and the platform features the
page asked for and did not get. The bundle also keeps `exceptions.log` — every exception raised in
the process while it was open, the first-chance ones something caught included, each with the stack
at the throw — and `messages.log`, every message the pipeline logged at every level.

## Analysing a page

```bash
dotnet run --project src/Broiler.Browser.Cli/Broiler.Browser.Cli.csproj -c Release -- --analyze https://example.com/ --output-dir analysis --verbose
```

`--analyze` is for the page that renders or runs wrong and gives no clue why. It loads the page the
way a capture does, runs it, renders it, and writes one directory that holds everything the run
produced and a report that ranks what looks wrong. Open `report.html` first.

**It always runs the page on Broiler.JS.** The capture commands run a page on the engine the build
configuration picks, which under `Debug-VM`/`Release-VM` puts the Broiler.VM JavaScript profile in
front of Broiler.JS. The analysis composes Broiler.JS directly in every configuration
(`HeadlessBrowserOptions.BroilerJsOnly`), and the report names the engine and its version.

| File | What is in it |
|---|---|
| `report.html`, `report.md`, `report.json` | The findings, ranked errors first, each with its evidence and the file that holds the rest; then the page's JavaScript, network, render, layout, HTML and CSS in detail, the phases with their timings, and the versions of every Broiler component that ran |
| `screenshot.png`, `screenshot-full.png` | The viewport, and the whole page, after the scripts |
| `screenshot-without-scripts.png` | The document as fetched, rendered as if no script had run — if this one is right and the first is wrong, look at the JavaScript; if both are wrong, look at the HTML, CSS and layout. The report gives the share of pixels the scripts changed |
| `screenshot-boxes.png` | The viewport with every layout box outlined, coloured by depth; boxes that reach past the right edge are red |
| `resources/` | Every document, script (as fetched and as run, under the `inline-7` labels the logs use), stylesheet, image, font and fetch response, with `index.json` |
| `document-as-fetched.html`, `document-after-scripts.html`, `document-as-rendered.html` | The three states of the document: as the server sent it, as its scripts left it, and as the renderer was given it |
| `exceptions.log`, `exceptions.json` | Every exception in the process — first-chance ones included, with the phase that was running and the stack at the throw — and the same grouped by type and throw site |
| `javascript-errors.log`, `console.log`, `messages.log` | The script failures with their stacks, the page's console, and every message the pipeline logged |
| `network.json`, `network.har` | Every request the page's profile sent, with what asked for it (document, script, style, image, font, fetch), status, redirects, timing and the file its body is in — and the same as an HTTP Archive, which browser developer tools import |
| `layout/` | The fragment tree as text and as JSON, every box's computed style, the display list, and Broiler.Layout's own invariant violations |
| `watchdog.md` | Only when the watchdog ended the run: the phase it was stuck in, the requests still open and the most frequent exceptions |
| `slow-phase-stacks.txt` | Only with `--sample-stacks` |

What the findings look for:

- **HTML** — quirks mode and the doctype that caused it; the parse errors of the document as
  fetched, each with its code, line and column and what Broiler.Dom.Html's parser did about it, the
  repairs that change what renders as findings of their own (an element whose end tag never comes, a
  `<div/>`, which Broiler closes and a browser leaves open, and, as information, an end tag that
  matches no open element, which Broiler repairs as a browser does);
  duplicate ids, elements HTML does not define, obsolete and custom elements, `<script>` elements of
  a type nothing runs, and stylesheets, images and frames that did not load.
- **CSS** — parse problems located by line and column in their own sheet (`style` attributes
  included), property names Broiler.CSS does not know, values its validator rejects, declarations the
  style engine dropped while it cascaded, unknown at-rules, and each `font-family` list whose first
  font is neither declared by `@font-face` nor installed. Every selector is put to Broiler.CSS's own
  account of what it models (`CssSelectorMatcher.DescribeGaps`): a pseudo-class it guesses at matches
  every element — `button:-moz-focusring` outlines every button — and is a warning; one it never
  matches where a browser can (`:placeholder-shown`, `:target`), a pseudo-element it does not render
  and a pseudo-class no browser supports are listed with the first rule's place.
- **JavaScript** — failures grouped by what failed, the platform features they name, unhandled
  promise rejections, exceptions the page caught itself, the load window not settling, a navigation
  the page asked for, long turns and idle gaps from the bridge's turn trace
  (`BROILER_TRACE_JS_ENTRY`), and the time scripts spent waiting for a layout because they asked
  for geometry.
- **Layout** — Broiler.Layout's invariant violations, a runaway page height, boxes far taller than
  their content, elements reaching past the right edge of the viewport, elements with text laid out
  with no size, and elements placed off the page — each named as `tag#id.class` with its ancestors.
  And the CSS the layout engine did not apply as written, as it reported it while it styled the
  rendered page (`LayoutDiagnostics`): the properties it ignored, with example values — those a
  screenshot would not show anyway (`cursor`, `transition`, scrolling) told apart — and the features
  it laid out as something simpler.
- **Render and network** — the renderer's own error reports, each saying what failed and the
  exception, failed requests, and phases that took longer than ten seconds.

Options: `--width`/`--height` set the viewport, `--timeout` the document's fetch, and
`--follow-first-link` analyses the landing page's first link. `--verbose` prints every request,
script failure and console message as it happens, each geometry question that laid the page out
(those that took 10 ms or more), and the first 300 exceptions (all of them are in `exceptions.log`).
`--analysis-timeout <SECS>` bounds the whole run (default 300, `0` for none, at most 30 days): when
it runs out, the watchdog writes what there is and the process exits with code 3, because nothing
below the command line bounds a script that loops or a layout that does not end. An exception counts
once however often it is rethrown on its way out, and one thrown with the stack nearly exhausted is
counted without being recorded, so that recording it cannot overflow the stack. Reusing an output
directory clears the files an earlier analysis wrote there first; nothing else in it is touched. `--sample-stacks` takes the process's stacks with
`dotnet-stack` (`dotnet tool install -g dotnet-stack`) every few seconds while a phase runs longer
than five, and ranks the Broiler methods the busy threads were in. `--analyze` repeated analyses each
page in its own child process, one after another, into `<DIR>/<page name>`: the exception log
listens to the whole process, and concurrent analyses would also measure each other.

The exit code is 0 when the analysis ran to the end, whatever the page did; 1 when the arguments
were wrong, the document could not be fetched or the run failed around its phases; 3 when the
watchdog ended it. For several pages it is the worst of theirs: 1 if any failed, else 3 if the
watchdog ended any.

## How a page runs

`HeadlessBrowser` composes the page from the pieces the window's load worker composes its own from:

1. `PageLoader`, over the network session of a private, ephemeral `BrowserProfile`, loads the
   document. `--timeout` bounds it, from the request to the last byte.
2. `RenderingPipeline` extracts the scripts and fetches the external ones on the same network, as
   the document's own requests.
3. They run on the engine `BrowserApp.NewScriptEngine` picks for the configuration — Broiler.JS, or
   the Broiler.VM JavaScript profile under `Debug-VM`/`Release-VM` — over a bridge made from
   `BrowserApp.BridgeOptions`, whose `HeadlessLayoutView` answers a script asking for
   `getBoundingClientRect()` or `offsetWidth` from a real layout.
4. `InteractiveSession.SettleLoadWindow` runs the load window to a fixed point.

So a capture has run the page the way the window runs it. Two things differ, both on purpose:

- **The page's own navigations are not followed.** A script assigning `location`, or a refresh
  `meta`, would move the window on; a capture is of the document asked for. `--follow-first-link`
  is the one navigation the command line makes, and it makes it as the landing page's navigation.
- **The engine's microtask queue is current while the page settles and while expressions run.** A
  promise created in a timer callback then settles at the next checkpoint on the same thread, not on
  the thread pool.

The image itself is rasterised by Broiler.HTML.Image's `HtmlRender`, over the same render
preparation the window applies (`HtmlPostProcessor.ProcessForBrowsing`), as the Broiler repository's
command line always did. The window renders through a container that fetches images, stylesheets
and fonts on the profile's network; `HtmlRender` fetches them itself.

## What changed on the way

Each of these is a difference from the command line in the Broiler repository.

- **`--url` saves the document the scripts left.** It used to save the markup as fetched, and ran
  the inline scripts only for their errors, against a stub `window` and `document` rather than the
  page's DOM. The markup as fetched is still in a diagnostics bundle, next to the document after the
  scripts.
- **Scripts are extracted, fetched and run the window's way.** That means the tokenizer-backed
  extraction, cookies on the profile's network, and the window's CSP handling, instead of the
  command line's regex pass over the source.
- **`--test-engines` tests the engine a capture runs on.** It used to build a Broiler.JS context of
  its own and report it as "YantraJS"; it now asks the window's factory, and reports `Broiler.JS` or,
  under the `-VM` configurations, `Broiler.VM`.
- **A followed link is the landing page's navigation.** A page on the network is never followed to a
  local file, and a local page is followed only within its own directory tree.
- **Images resolve against the document's own URL**, which after a redirect or a followed link is
  where its relative references point. A `--full-page` capture used to have no base URL at all.
- **`--convert-doc` is gone.** Document conversion is Broiler.Documents' own command line,
  `broilerdoc convert <input> --out <path>`, which reads and writes every format this one did. The
  flag now says so.
- **Navigation Timing has no network phases.** The old command line took over the document's socket
  connect to time the DNS lookup, the connect and the TLS handshake; the profile's network does not
  expose those, as it does not for the window.
- **Script geometry does not resolve CSS anchor positioning.** The layout view used to switch the
  layout engine's native anchor-positioning pass on around each layout. That switch is internal to
  Broiler.Layout, which grants its internals to `Broiler.Cli.Tests` but not to
  `Broiler.Browser.Core`, where the view lives.

Three pieces the command line needed had been deleted from Broiler.HTML on 2026-09-15 as dead code,
because nothing Broiler.HTML could see used them: `HeadlessLayoutView` (603c8083) and the fuzzer's
`HtmlCssGenerator` and `DeltaMinimizer` (4c5a9d58). The fuzzer's two live beside the command line.
The layout view is in `Broiler.Browser.Core`, because the window answers its scripts' geometry
questions with it too; until it did, the window had never registered a layout view, and its scripts
got the bridge's null view, which says 0 to all of them.

## Why the assembly is still called Broiler.Cli

The project is `Broiler.Browser.Cli`, and the assembly keeps its old name. Scripts and notes spell
the executable that way, and Broiler.HtmlBridge's packages grant their internals to `Broiler.Cli`
and `Broiler.Cli.Tests` by name: that is how a capture reaches `ResourceTrace` for its diagnostics
bundle and the page realm (`DomBridge.Realm`) for `--evaluate-page`. None of those assemblies is
strong-named, so the name is the whole grant.

## Not here yet

- **The tests that used the command line as a harness.** The Broiler repository's
  `src/Broiler.Cli.Tests` held 406 files, and all but a dozen test DOM, JavaScript and rendering
  behaviour through the command line's own script loop. That loop is gone, and the behaviour belongs
  to Broiler.HtmlBridge and Broiler.HTML, whose suites should take those tests. What came here are
  the command line's own tests, plus new ones for the pipeline it runs on.
- **Rendering on the profile's network**, the way the window's container does, and with it the
  window's image, stylesheet and font caching.
- **An evaluation seam in Broiler.HtmlBridge.** `--evaluate-page` reaches the realm through an
  internal the package grants by assembly name. `InteractiveSession` could offer it instead.
- **Anchor positioning in the layout view**, which needs Broiler.Layout to expose its switch or
  grant `Broiler.Cli` its internals.
- **The rest of the Broiler repository's tools**: `Broiler.Wpt`, `Broiler.DevConsole`,
  `Broiler.Playback`, `Broiler.DevSite` and `Broiler.Engines.Baseline` are still there.
