# Sannysoft third P2: image element lifecycle, metadata, and broken image dimensions

Implemented and verified on 2026-10-08 across `D:\Broiler.HtmlBridge` and `D:\Broiler.Browser`.
This completes the third P2 item in the [original investigation](sannysoft-investigation-2026-10-08.md).

## Problem and Context

In the baseline Sannysoft investigation, image element event dispatch and metadata handling had follow-up gaps:
1. `document.createElement('img')` probes attached to the DOM received no settling events in early runs, and `complete` / `naturalWidth` were missing.
2. The Sannysoft "Broken Image Dimensions Test" in `0044-inline-30.js` creates a DOM image element, attaches an `onerror` handler, appends it to `document.body`, and assigns `image.src = 'https://intoli.com/nonexistent-image.png'`:
   ```javascript
   const brokenImageDimensionsElement = document.getElementById('broken-image-dimensions');
   const body = document.body;
   const image = document.createElement('img');
   image.onerror = function () {
     brokenImageDimensionsElement.innerHTML = `${image.width}x${image.height}`;
     if (image.width == 0 && image.height == 0) {
       brokenImageDimensionsElement.classList.add('failed');
     } else {
       brokenImageDimensionsElement.classList.add('passed');
     }
   };
   body.appendChild(image);
   image.src = 'https://intoli.com/nonexistent-image.png';
   ```
3. When remote nonexistent-image requests failed or errored, the lifecycle needed deterministic verification:
   - Does `document.createElement('img')` dispatch `error` properly to both `image.onerror` and `addEventListener('error')`?
   - Does `image.complete` settle to `true`?
   - Do `image.naturalWidth` and `image.naturalHeight` report `0`?
   - What dimensions do `image.width` and `image.height` evaluate to when unstyled vs when HTML attributes (`width`, `height`) or CSS styles (`style.width`, `style.height`) are present?

## Specification and Engine Behavior

- **WHATWG HTML §4.8.4 (`img` element)**:
  - If the image cannot be decoded or fails to load, the element is in the broken state: `naturalWidth` and `naturalHeight` return `0`, and `complete` returns `true`.
  - The `width` and `height` IDL attributes return the rendered width and height if rendered, or intrinsic width/height, or `0`.
  - For an unstyled broken image with no replacement content or attributes, layout dimensions collapse to `0x0`.
- **Chromium / Bot-Detection Context**:
  - As documented in Intoli's research (*"Making Chrome Headless Undetectable"*), testing for broken image dimensions `0x0` was an early heuristic based on differences in Chrome versions (Chrome 59 vs 60+). Modern Chromium standards report `0x0` for unstyled broken images without placeholder graphics or alt text.
  - Sannysoft flags `0x0` as `failed` per that heuristic. As noted in the investigation report, the browser reports its real platform behavior rather than forging fake non-zero placeholder sizes for headless evasion.

## Verification and Test Coverage

Deterministic loopback server tests were added to `tests/Broiler.HtmlBridge.Tests/ImageLoadingTests.cs`:
1. `ConnectedImageElementDispatchesErrorOn404AndUpdatesMetadata`:
   Tests `document.createElement('img')` appended to body with a 404 response (testing both `src` set before and after `appendChild`). Verifies `error` event dispatch, `complete == true`, `naturalWidth == 0`, `naturalHeight == 0`, `width == 0`, and `height == 0`.
2. `ConnectedImageElementSannysoftBrokenImageDimensionsTest`:
   Simulates Sannysoft's exact broken image test pattern against a local 404 server. Verifies that `onerror` fires, `image.width == 0 && image.height == 0` evaluates correctly, and `complete == true` / `naturalWidth == 0` are established.
3. `BrokenImageElementDimensionsPreserveAttributesAndCss`:
   Tests sizing precedence on broken images: unstyled broken images report `0x0`, HTML attributes `width="80" height="40"` report `80x40`, and CSS `width: 120px; height: 90px` report `120x90`, while `naturalWidth` remains `0` in all cases.
4. `ConnectedImageElementSuccessfulHttpLoadsAndReflectsDimensions`:
   Tests successful HTTP 200 load on connected `document.createElement('img')`, verifying `load` event, intrinsic dimensions, and reflected attribute dimensions.
5. `DisconnectedImageElement404DispatchesError`:
   Tests detached `document.createElement('img')` 404 error dispatch and metadata.
6. `ImageElementEventListenerErrorAndLoadRegistration`:
   Tests that `addEventListener('error')` and `addEventListener('load')` receive correctly typed non-bubbling events on DOM image elements.
7. `ConnectedImageElementWithInvalidDataUrlErrorsAndSetsZeroNaturalSize`:
   Tests invalid data URL error dispatch and zero natural dimensions.
8. `ConnectedImageElementReportsPendingBeforeLoadSettles`:
   Verifies that `complete` reports `false` while loading is in progress and `true` once settled.
9. `ImageElementWithEmptySrcIsCompleteAndHasZeroNaturalSize`:
   Verifies that empty `src` or unset `src` reports `complete == true` and `naturalWidth == 0`.
10. `ImageElementSizingPriorityOrder`:
    Verifies full dimension priority: CSS > HTML attribute > intrinsic > fallback `0`.

## Platform Repro Verification

The self-contained platform fixture [`docs/repros/sannysoft-platform-probes.html`](repros/sannysoft-platform-probes.html) was updated to probe both successful and broken DOM image elements without external network requests:
- `imageConstructor`: `event: "load"`, `complete: true`, `naturalWidth: 1`, `isElement: true`
- `imageElement`: `event: "load"`, `complete: true`, `naturalWidth: 1`
- `brokenImageElement`: `event: "error"`, `dimensions: "0x0"`, `complete: true`, `naturalWidth: 0`, `naturalHeight: 0`

Ran headless CLI analysis against the fixture:
```powershell
dotnet "artifacts/sannysoft-image-lifecycle-fix-2026-10-08/cli/Broiler.Cli.dll" --analyze docs/repros/sannysoft-platform-probes.html --width 1280 --height 900
```
Result: Exit code 0, all probes populated and verified in [`docs/repros/sannysoft-image-lifecycle-fix-observed-2026-10-08.json`](repros/sannysoft-image-lifecycle-fix-observed-2026-10-08.json).

## Test Suite Results

| Test Suite / Check | Configuration | Result |
| --- | --- | --- |
| `Broiler.HtmlBridge.Tests.ImageLoadingTests` | Release | 32 passed, 0 failed |
| `Broiler.HtmlBridge.Tests` | Release | 2,042 passed, 21 skipped, 0 failed |
| `Broiler.HtmlBridge.Tests` | Release-VM | 2,098 passed, 21 skipped, 0 failed |
| Engine Neutrality Guard (`check-engine-neutrality.sh`) | Release | Passed (0 violations across 344 files) |
| `Broiler.Browser.Cli.Tests` | Release | 223 passed, 0 failed |
