# FP-02: Window and Navigator Interface and Prototype Contracts

Implemented and verified on **2026-10-09**. This completes **FP-02 / P1** from the [compatibility roadmap](fingerprinting-compatibility-roadmap.md).

## Problem

1. **Missing `Window` and `Navigator` interface globals on `window`:**
   Neither `Window` nor `Navigator` constructor was exposed on the JavaScript global scope. In CreepJS, the headless detector ran `'ontouchstart' in Window`, throwing `ReferenceError: Window is not defined` and aborting the headless section.
2. **Missing prototype chains and branding:**
   - `window` had no `Window.prototype` in its prototype chain (`window.__proto__ === Object.prototype`). Per HTML/WebIDL, `window.__proto__ === Window.prototype` and `Window.prototype.__proto__ === EventTarget.prototype`.
   - `navigator` had properties installed directly as own properties rather than inheriting from `Navigator.prototype`.
3. **CreepJS Lie Detector invariants:**
   - CreepJS audits `Navigator` properties using `searchLies(() => Navigator, { target: [...] })`.
   - Any target property found as an *own* property on `navigator` triggers `['failed undefined properties']`.
   - Accessors on `Navigator.prototype` must throw `TypeError: Failed to read the '{prop}' property from 'Navigator': Illegal invocation` when called with an invalid receiver (`Reflect.get(Navigator.prototype, prop)` or on alien objects).
   - Calling `new Window()` or `new Navigator()` must throw `TypeError: Illegal constructor`.
4. **Sub-window inheritance:**
   Iframes and child window proxies did not expose `Window` / `Navigator` constructors or inherit from `Window.prototype`.

## Implementation

The fix was implemented within `Broiler.HtmlBridge.Dom` while preserving JSEAL engine neutrality:

- **[`WindowBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/WindowBinding.cs)**:
  - Registers the `Window` interface constructor throwing `TypeError: Illegal constructor`.
  - Configures `Window.prototype` inheriting from `EventTarget.prototype` with `[Symbol.toStringTag] = 'Window'`.
  - Configures the global object prototype to `Window.prototype` via `realm.SetPrototype`.
- **[`NavigatorBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/NavigatorBinding.cs)**:
  - Registers the `Navigator` interface constructor throwing `TypeError: Illegal constructor`.
  - Configures `Navigator.prototype` with `[Symbol.toStringTag] = 'Navigator'`.
  - Installs 26 standard prototype accessors (`userAgent`, `platform`, `language`, `languages`, `cookieEnabled`, `onLine`, `vendor`, `vendorSub`, `appCodeName`, `appName`, `appVersion`, `product`, `productSub`, `webdriver`, `maxTouchPoints`, `hardwareConcurrency`, `deviceMemory`, `pdfViewerEnabled`, `plugins`, `mimeTypes`, `storage`, `permissions`, `userAgentData`, `webkitTemporaryStorage`, `webkitPersistentStorage`) with receiver brand checks backed by `ConditionalWeakTable<IJSObject, INavigatorHost>`.
  - Installs 5 standard prototype methods (`sendBeacon`, `javaEnabled`, `getGamepads`, `getBattery`, `requestMediaKeySystemAccess`) with receiver brand checks.
  - Zero own properties on the `navigator` instance for prototype accessors.
- **[`user-activation.js`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Polyfills/user-activation.js)**:
  - Updated `userActivation` accessor definition on `Navigator.prototype` with receiver brand check `if (this !== host.navigator) throw new TypeError(...)` to avoid passing `Object.create(Navigator.prototype)`.
- **[`SubWindowBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/SubWindowBinding.cs)**:
  - Added `"Window"` and `"Navigator"` to `MirroredGlobals`.
  - Configured `Window.prototype` as the prototype for sub-window objects.
- **Surface & Capability Binding Refactoring**:
  - Refactored [`NavigatorSurfacesBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/NavigatorSurfacesBinding.cs), [`NavigatorCapabilityBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/NavigatorCapabilityBinding.cs), and [`StorageQuotaBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/StorageQuotaBinding.cs) to provide factory methods (`BuildStorage`, `BuildPermissions`, `BuildUserAgentData`, `BuildStorageQuota`, `GetBattery`, `RequestMediaKeySystemAccess`) consumed by `NavigatorBinding`.

## Validation

### 1. Unit Tests (`Broiler.HtmlBridge`)

New test suite [`WindowAndNavigatorInterfaceTests.cs`](file:///d:/Broiler.HtmlBridge/tests/Broiler.HtmlBridge.Tests/WindowAndNavigatorInterfaceTests.cs) (77 tests) covers:
- `Window` and `Navigator` constructors and `[Symbol.toStringTag]`.
- Prototype inheritance chains (`window` -> `Window.prototype` -> `EventTarget.prototype` -> `Object.prototype`, `navigator` -> `Navigator.prototype` -> `Object.prototype`).
- WebIDL illegal constructor errors on `new Window()` and `new Navigator()`.
- Zero own properties on `navigator` instance for all prototype accessors.
- Illegal invocation `TypeError` on all 26 prototype accessors and 5 prototype methods when invoked on invalid receivers.
- Sub-window mirroring and prototype chain.

Test suites executed:
- **`dotnet test -c Release`**: **2,148 passed**, 0 failed, 21 skipped.
- **`dotnet test -c Release-VM`**: **2,204 passed**, 0 failed, 21 skipped.
- **`python scripts/test-engine-neutrality.py`**: **6 tests passed, OK**.

### 2. Packaging and Verification

- Built packages at version **`0.1.0-preview.27`** using `eng/pack.ps1`.
- Verified consumer restore against nuget.org via `eng/verify-feed.ps1`.
- Restored in `Broiler.Browser` via local NuGet feed mapping (no DLL overlay hacks). Built into isolated directory `artifacts/fp02-creepjs-2026-10-09/build`.

### 3. HTTP Probe Fixture

Running `fingerprinting-current-probes.html` over HTTP produced:
- `interfaces.Window`: `"function"`
- `interfaces.Navigator`: `"function"`
- `interfaces.windowIsWindow`: `true`
- `interfaces.windowIsEventTarget`: `true`
- `interfaces.navigatorIsNavigator`: `true`
- `interfaces.navigatorOwnPropsCount`: `0`
- `interfaces.ontouchstartInWindow`: `false`
- `interfaces.windowConstructorThrowsIllegal`: `true`
- `interfaces.navigatorConstructorThrowsIllegal`: `true`
- `interfaces.userAgentIllegalInvocationThrows`: `true`
- `childRealm.Window`: `"function"`
- `childRealm.Navigator`: `"function"`
- `childRealm.childIsWindow`: `true`

### 4. Unmodified Live CreepJS Analysis

Executing live CreepJS (`--analyze https://abrahamjuliot.github.io/creepjs/ --output-dir artifacts/fp02-creepjs-2026-10-09 --no-window`):
- Analysis completed with **exit code 0**.
- **`✔ (543.85ms) navigator passed`** in `console.log`.
  - Navigator fingerprint generated with `$hash: "4d0dfccdd738c37ff53868cde097b1599f1d80ad1f3dc1091edf951c0955ca79"`.
- **`✔ (137.67ms) headless passed`** in `console.log`.
  - Headless fingerprint generated with `$hash: "4252ec3a6756eb70a9bcb5ffa17a7be775d2f65bc0425b68fd1b6b01beb4022e"`.
- `ReferenceError: Window is not defined` is **completely eliminated**.
- Only 2 remaining script failures on the page:
  - `TypeError: Cannot get property bottom of undefined (evaluating 'domRect.bottom')` (FP-03)
  - `TypeError: undefined is not a function` in Canvas2D/SVG (FP-05)

### 5. Unmodified Live Sannysoft Analysis

- Sannysoft analysis completed with exit code 0.
- All 20 scanner rows and canvas results preserved with zero regressions.

Durable snapshot: [fp02-window-navigator-observed-2026-10-09.json](repros/fp02-window-navigator-observed-2026-10-09.json).
