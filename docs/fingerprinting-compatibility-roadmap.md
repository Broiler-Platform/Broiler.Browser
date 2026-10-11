# Sannysoft and CreepJS compatibility roadmap

Updated 2026-10-09. This roadmap covers the agreed practical scope: working
collectors, Canvas2D/SVG operations, targeted layout fixes and useful diagnostics.
Iframe realm redesign, WebGL, WebRTC, audio and speech are deferred by request.
Completion here does not mean Chromium conformance or an all-green bot score.

The [completion report](fingerprinting-completion-2026-10-09.md) records the
implementation, tests, reference comparison and remaining limits. The earlier
[reinvestigation](fingerprinting-reinvestigation-2026-10-09.md) is the published
preview.25 baseline, not the current implementation status.

## Completed checklist

- [x] **P0:** contain proxy recursion and preserve process survival, frame document
  ownership, parent DOM and populated results. Consumed through JavaScript preview.7.
- [x] **FP-01:** worker globals and WorkerNavigator; dedicated-worker collector.
  [Evidence](fp01-worker-environment-fix-2026-10-09.md).
- [x] **FP-02:** Window/Navigator interfaces and receiver checks.
  [Evidence](fp02-window-navigator-fix-2026-10-09.md).
- [x] **FP-03:** DOMRect/DOMRectList and Element/Range geometry, including attached
  zero-size boxes. [Evidence](fp03-client-rects-fix-2026-10-09.md).
- [x] **FP-04:** MediaDevices contracts for an environment without capture devices;
  legacy getUserMedia compatibility. [Evidence](fp04-mediadevices-fix-2026-10-09.md).
- [x] **FP-05:** linear/radial gradient pixels, ellipse and Bezier/quadratic paths,
  affine drawing state, SVG shape/group bounds and font-based text measurements.
  Canvas bitmaps now reach the page renderer through an isolated render projection.
  CreepJS's Canvas2D and SVG collectors finish.
- [x] **FP-07:** remove unused empty line projections while retaining actual blank
  lines; wrap oversized text with `overflow-wrap: break-word` / `word-wrap`,
  preserve grapheme boundaries and restore original words on resize. Validate
  fixed-width cards and table cells, plus live layout invariants.
- [x] **FP-08:** exclude embedded stylesheet source from collapsed-text warnings;
  distinguish SVG element names from obsolete HTML; group repeated resource
  failures by run scope, URL and outcome while retaining every network record.
  Investigate the original nine broken-image requests and the HTML `<image/>` warning.
- [x] Validate both bridge providers, the Layout suite and Browser tests; restore
  actual local packages and verify loaded versions; run both unmodified sites with
  analysis and ordinary image capture; assert populated results and repaired collectors.
- [x] Compare a committed seven-check graphics/layout fixture with Chrome and retain
  screenshots and source hashes. Font metrics differ; pixel equality is not a gate.

## Deferred scope and known limits

**FP-06:** separate iframe intrinsics/prototypes require the deferred realm redesign.
Separate documents do not imply separate Function/Object identities.

**FP-09:** WebGL, WebRTC, OfflineAudioContext, speech and physical capture-device
implementations remain separate projects. Unsupported collectors are expected.

The completed graphics slice is deliberately bounded. SVG text bounds describe
font advance cells, not exact glyph outlines; complex shaping, text paths and
complete SVG transform/prototype conformance remain incomplete. Canvas lacks full
DOMMatrix/CanvasGradient interface conformance, shadows and clipping; strokeText
still fills glyphs, transformed image sampling is nearest-neighbour, and transformed
stroke widths remain in device pixels. These limits must not be reported as supported.

Layout supports emergency word wrapping, not full CSS Text conformance. `anywhere`
uses the same wrapping path but does not yet alter min-content sizing. A word joined
across inline element boundaries can still overflow. General ellipsis, axis-specific
overflow, font parity and animation are outside this targeted layout completion.

## Reproduce and release

Browser currently consumes **local** HtmlBridge preview.32 and Layout preview.23
packages through the sources in `NuGet.config`; these versions were not published
by this work. Rebuild the feeds from the sibling checkouts before restoring elsewhere:

```powershell
# Run the corresponding command in each component checkout, into an empty output.
# Broiler.HtmlBridge:
./eng/pack.ps1 -Version 0.1.0-preview.32 -Output artifacts/compatibility-ready
# Broiler.Layout:
./eng/pack.ps1 -Version 0.1.0-preview.23 -Output artifacts/compatibility-completion-final
# Broiler.Browser:
dotnet build src/Broiler.Browser.Cli/Broiler.Browser.Cli.csproj -c Release -o artifacts/compatibility-completion/final-cli
./eng/verify-fingerprinting.ps1
```

The [runner](../eng/verify-fingerprinting.ps1) fails on collector, fixture, result-DOM
or layout-invariant regressions and records downloaded source hashes. It does not
require matching fingerprints or suppress expected probe errors. Source publication
and public NuGet release remain release-management work, not a completed action.
