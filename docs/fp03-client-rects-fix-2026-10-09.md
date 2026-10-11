# FP-03: DOMRect, DOMRectList, and Element/Range Geometry

Implemented and verified on **2026-10-09**. This completes **FP-03 / P1** from the [compatibility roadmap](fingerprinting-compatibility-roadmap.md).

## Problem

1. **Missing WebIDL geometry interfaces:**
   `DOMRectReadOnly`, `DOMRect`, and `DOMRectList` constructors were not exposed on `window` or worker scopes. `Element.getBoundingClientRect()` and `Element.getClientRects()` previously returned plain JavaScript objects and arrays (`{ left, top, width, height, ... }` and `[ rect ]`), breaking `instanceof DOMRect`, `instanceof DOMRectList`, `[Symbol.toStringTag]`, `rect.toJSON()`, `DOMRect.fromRect()`, and receiver brand checks.
2. **CreepJS crash on live page (`rects failed`):**
   In CreepJS `creep.js`, the `getClientRects()` collector measures layout dimensions:
   ```javascript
   const ghostEl = [...DOC.getElementsByClassName('rect-ghost')][0];
   const ghostDimensions = toNativeObject(ghostEl.getClientRects()[0]);
   ```
   where `toNativeObject` reads `domRect.bottom`.
   Previously:
   - Elements with zero area (`width: 0; height: 0`, such as `.rect-ghost`) returned `[]` (`Array.Empty`) instead of a `DOMRectList` with the attached 0×0 box.
   - `ghostEl.getClientRects()[0]` was `undefined`.
   - `toNativeObject(undefined)` crashed with `TypeError: Cannot get property bottom of undefined (evaluating 'domRect.bottom')`.
3. **Subdocument / iframe geometry omission:**
   In CreepJS, the test elements are appended into an iframe subdocument (`DOC = PHANTOM_DARKNESS.document`).
   - `BuildSharedGeometrySnapshot` passed `contentDocumentResolver` to the layout view, which returned geometry for subdocument elements. However, the projection mapping only tracked top-level cloned elements. Because subdocument elements were not in `projection.SourceFor`, they were discarded from `sourceGeometry`.
   - `CurrentLayoutSnapshotKey` only tracked `_document.Version`, so mutations inside subdocuments (`DOC.body.appendChild(...)`) failed to invalidate the retained geometry cache.
4. **Range geometry:**
   `Range.getBoundingClientRect()` and `Range.getClientRects()` were returning placeholder objects rather than real `DOMRect` / `DOMRectList` instances over the range's overlapping content.

## Implementation

The solution was implemented across `Broiler.HtmlBridge.Dom` while strictly preserving JSEAL engine neutrality:

1. **Geometry Binding ([`GeometryBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/GeometryBinding.cs)):**
   - **`DOMRectReadOnly`:**
     - Constructor: `new DOMRectReadOnly(x, y, width, height)` with WebIDL parameter coercion.
     - Static method: `DOMRectReadOnly.fromRect(other)`.
     - 8 prototype accessors: `x`, `y`, `width`, `height`, `top`, `right`, `bottom`, `left`.
     - Prototype method: `toJSON()`.
     - `[Symbol.toStringTag] = 'DOMRectReadOnly'`.
     - WebIDL illegal invocation brand checks backed by `ConditionalWeakTable<object, DomRectState>`.
     - Exposed on both `Window` and `Worker` (`[Exposed=(Window,Worker)]`).
   - **`DOMRect`:**
     - Inherits from `DOMRectReadOnly` (`Object.setPrototypeOf(DOMRect.prototype, DOMRectReadOnly.prototype)`).
     - Constructor: `new DOMRect(x, y, width, height)`.
     - Static method: `DOMRect.fromRect(other)`.
     - 4 mutable prototype accessors: `x`, `y`, `width`, `height` with setters modifying underlying state.
     - `[Symbol.toStringTag] = 'DOMRect'`.
     - Exposed on both `Window` and `Worker`.
   - **`DOMRectList`:**
     - Constructor: throws `TypeError: Illegal constructor` when invoked directly.
     - Prototype accessor: `length`.
     - Prototype method: `item(index)`.
     - Own indexed properties (`list[0]`, `list[1]`, ...) and `[Symbol.iterator]` on `DOMRectList.prototype` for `for...of` loops and spread syntax.
     - `[Symbol.toStringTag] = 'DOMRectList'`.
     - Exposed on `Window` only.
2. **Element Geometry Binding ([`ElementGeometryBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/ElementGeometryBinding.cs)):**
   - `getBoundingClientRect()` now constructs and returns a real `DOMRect` instance.
   - `getClientRects()` now constructs and returns a real `DOMRectList` instance.
3. **Layout & Box-Model Metrics ([`LayoutMetrics.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/DomBridge/LayoutMetrics.cs), [`LayoutMetrics.Geometry.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/DomBridge/LayoutMetrics.Geometry.cs)):**
   - Added `GetClientRectsForDomElement(DomElement element, bool isRoot)`.
   - Attached elements (including zero-area boxes like `.rect-ghost`) produce a `DOMRectList` with 1 item representing their attached layout box.
   - Detached elements and `display: none` elements return an empty `DOMRectList`.
   - Added connected fallback in `GetClientRectsForDomElement` to ensure attached elements with an active box always receive their rendered bounding client rect even if omitted from layout snapshots.
   - Updated `CurrentLayoutSnapshotKey()` to include subdocument versions (`_browsingContexts.ContentDocuments`), guaranteeing that mutations in iframe documents invalidate retained geometry.
   - Updated `BuildSharedGeometrySnapshot` to preserve live subdocument element identities and support nested subdocument containers in the layout view callback.
4. **Range Geometry ([`TraversalBinding.Range.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/TraversalBinding.Range.cs), [`Traversal.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/DomBridge/Traversal.cs)):**
   - Wired `Range.getBoundingClientRect()` and `Range.getClientRects()` to return `DOMRect` and `DOMRectList` instances reflecting overlapping nodes.
5. **Sub-Window & Worker Exposure ([`SubWindowBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/SubWindowBinding.cs), [`JSWorker.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/JSWorker.cs)):**
   - Added `DOMRect`, `DOMRectReadOnly`, and `DOMRectList` to `MirroredGlobals`.
   - Installed `DOMRect` and `DOMRectReadOnly` in worker global realms.

## Validation

### 1. Unit Tests (`Broiler.HtmlBridge`)

New test suite [`DomRectAndGeometryTests.cs`](file:///d:/Broiler.HtmlBridge/tests/Broiler.HtmlBridge.Tests/DomRectAndGeometryTests.cs) (30 tests) covers:
- Constructors, prototypes, and prototype inheritance (`DOMRect` inherits from `DOMRectReadOnly`).
- `DOMRectList` illegal constructor check (`TypeError: Illegal constructor`).
- WebIDL illegal invocation brand checks on all accessors and methods (`item`, `toJSON`, `x`, `y`, `width`, `height`, `top`, `right`, `bottom`, `left`).
- Mutability of `DOMRect` vs immutability of `DOMRectReadOnly`.
- `DOMRectList` indexing (`list[0]`, `list.item(0)`), bounds handling, and `Symbol.iterator` / `for...of` iteration.
- Element geometry (`getBoundingClientRect()`, `getClientRects()`) on sized boxes, zero-size attached boxes, and `display: none` elements.
- Range geometry (`Range.getBoundingClientRect()`, `Range.getClientRects()`).
- Sub-window mirroring and Worker global exposure.

Test suite execution results:
- **`dotnet test -c Release`**: **30 passed**, 0 failed.
- **`dotnet test -c Release-VM`**: **30 passed**, 0 failed.
- **Engine neutrality (`test-engine-neutrality.py`)**: **6 passed, OK**.
- **Full suite (`dotnet test -c Release`)**: **2,178 passed**, 0 failed, 21 skipped.

### 2. Packaging and Verification

- Built packages at version **`0.1.0-preview.28`** using `eng/pack.ps1`.
- Verified consumer restore against nuget.org via `eng/verify-feed.ps1`.
- Restored in `Broiler.Browser` via local NuGet feed mapping (no DLL overlay hacks). Built `Broiler.Browser.Cli` in `Release`.
- Browser test suites:
  - `Broiler.Browser.Core.Tests`: **319 passed**, 0 failed.
  - `Broiler.Browser.Cli.Tests`: **224 passed**, 0 failed.
  - `Broiler.Traffic.Tests`: **20 passed**, 0 failed.

### 3. HTTP Probe Fixture

Running `fingerprinting-current-probes.html` over HTTP:
- `interfaces.DOMRect`: `"function"`
- `interfaces.DOMRectList`: `"function"`
- `elementRects`: `length: 1, item: "function", tag: "[object DOMRectList]", first: { x: 8, y: 8, width: 100, height: 20, bottom: 28 }`
- `emptyElementRects`: `length: 1, item: "function", tag: "[object DOMRectList]", first: { x: 8, y: 28, width: 0, height: 0, bottom: 28 }`
- `rangeRects`: `length: 1, item: "function", tag: "[object DOMRectList]", first: { x: 8, y: 8, width: 100, height: 20, bottom: 28 }`

### 4. Unmodified Live CreepJS Analysis

Executing live CreepJS (`--analyze https://abrahamjuliot.github.io/creepjs/ --output-dir artifacts/fp03-creepjs-2026-10-09 --no-window`):
- Analysis completed with **exit code 0**.
- **`✔ (1582.47ms) rects passed`** in `console.log`.
- `TypeError: Cannot get property bottom of undefined (evaluating 'domRect.bottom')` is **completely eliminated**.
- `clientRects` section populated with complete `elementClientRects` and `rangeClientRects`.
- Remaining live errors on the entire page reduced to just 2:
  - Canvas2D/SVG (`TypeError: undefined is not a function`, FP-05)
  - SpeechSynthesis (`speech failed`, FP-09)

### 5. Unmodified Live Sannysoft Analysis

- Sannysoft analysis completed with exit code 0.
- All 20 scanner rows and canvas results preserved with zero regressions.

Durable snapshot: [fp03-client-rects-observed-2026-10-09.json](repros/fp03-client-rects-observed-2026-10-09.json).
