# FP-04: Modern MediaDevices and MediaDeviceInfo

Implemented and verified on **2026-10-09**. This completes **FP-04 / P1** from the [compatibility roadmap](fingerprinting-compatibility-roadmap.md).

## Problem

1. **Missing WebIDL interfaces:**
   `MediaDevices` and `MediaDeviceInfo` interfaces were absent on `window` and global scopes. Calling `new MediaDevices()` or `new MediaDeviceInfo()` did not throw standard WebIDL illegal constructor errors, and `MediaDevices.prototype` did not inherit from `EventTarget.prototype`.
2. **Missing `navigator.mediaDevices` accessor:**
   `navigator.mediaDevices` was `undefined`. In bot detection suites such as Sannysoft (`bot.sannysoft.com`):
   ```javascript
   let devices = await navigator.mediaDevices.enumerateDevices();
   ```
   threw an unhandled promise rejection:
   `TypeError: Cannot get property enumerateDevices of undefined (evaluating 'navigator.mediaDevices.enumerateDevices')`.
3. **Missing legacy `navigator.getUserMedia`:**
   In Sannysoft inline script 16:
   ```javascript
   document.write(navigator.getUserMedia());
   ```
   evaluated to `TypeError: undefined is not a function`, causing an unhandled script failure.

## Implementation

The solution was implemented cleanly and simply across `Broiler.HtmlBridge.Dom` while strictly preserving JSEAL engine neutrality:

1. **MediaDevices Binding ([`MediaDevicesBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/MediaDevicesBinding.cs)):**
   - **`MediaDevices`:**
     - Constructor: throws `TypeError: Failed to construct 'MediaDevices': Illegal constructor`.
     - Prototype chain: inherits from `EventTarget.prototype` (`Object.setPrototypeOf(MediaDevices.prototype, EventTarget.prototype)`).
     - `[Symbol.toStringTag] = 'MediaDevices'`.
     - Receiver brand checks backed by `ConditionalWeakTable<object, MediaDevicesState>`.
     - `enumerateDevices()`: returns Promise resolving to empty array `[]` (since headless environment has no physical capture devices attached).
     - `getUserMedia(constraints)`: returns Promise rejecting with `NotFoundError` `DOMException`.
     - `getDisplayMedia(options)`: returns Promise rejecting with `NotAllowedError` `DOMException`.
     - `getSupportedConstraints()`: returns standard dictionary of supported constraint flags.
     - `ondevicechange`: accessor property with receiver check and event handler storage.
   - **`MediaDeviceInfo`:**
     - Constructor: throws `TypeError: Failed to construct 'MediaDeviceInfo': Illegal constructor`.
     - `[Symbol.toStringTag] = 'MediaDeviceInfo'`.
     - Prototype accessors: `deviceId`, `groupId`, `kind`, `label` with illegal invocation checks.
     - Prototype method: `toJSON()` with illegal invocation checks.
2. **Navigator Binding ([`NavigatorBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/NavigatorBinding.cs)):**
   - Added `mediaDevices` accessor to `Navigator.prototype` returning a cached singleton per `Navigator` instance.
   - Added legacy `getUserMedia` method to `Navigator.prototype`:
     - When called with 0 arguments (as Sannysoft does), returns `undefined` without error.
     - When called with callbacks, invokes error callback with `NotFoundError` `DOMException`.
     - Receiver checks throw `TypeError: Illegal invocation`.
3. **Sub-Window Mirroring ([`SubWindowBinding.cs`](file:///d:/Broiler.HtmlBridge/src/Broiler.HtmlBridge.Dom/Features/SubWindowBinding.cs)):**
   - Added `MediaDevices` and `MediaDeviceInfo` to `MirroredGlobals`.

## Validation

### 1. Unit Tests (`Broiler.HtmlBridge`)

New test suite [`MediaDevicesInterfaceTests.cs`](file:///d:/Broiler.HtmlBridge/tests/Broiler.HtmlBridge.Tests/MediaDevicesInterfaceTests.cs) (25 tests) covers:
- Interface constructor globals exist on window and globalThis.
- Direct construction throws `TypeError: Illegal constructor`.
- Prototypes have correct `[Symbol.toStringTag]` and `prototype.constructor === Interface`.
- `MediaDevices.prototype` inherits from `EventTarget.prototype`.
- `navigator.mediaDevices` is an instance of `MediaDevices` and `EventTarget`, and returns a cached singleton.
- WebIDL receiver brand checks throw `TypeError: Illegal invocation` on all methods and accessors.
- `enumerateDevices()` resolves to `[]`.
- `getUserMedia()` rejects with `NotFoundError` `DOMException`.
- `getDisplayMedia()` rejects with `NotAllowedError` `DOMException`.
- `getSupportedConstraints()` returns dictionary with supported constraints.
- Legacy `navigator.getUserMedia()` returns `undefined` for 0 arguments and invokes error callback when provided.
- SubWindow mirrors `MediaDevices` and `MediaDeviceInfo`.

Test suite execution results:
- **`dotnet test -c Release`**: **25 passed**, 0 failed.
- **`dotnet test -c Release-VM`**: **25 passed**, 0 failed.
- **Engine neutrality (`test-engine-neutrality.py` & `check-engine-neutrality.sh`)**: **6 passed, OK**, every count matches its budget.
- **Full suite (`dotnet test -c Release`)**: **2,203 passed**, 0 failed, 21 skipped.
- **Full suite (`dotnet test -c Release-VM`)**: **2,259 passed**, 0 failed, 21 skipped.

### 2. Packaging and Consumer Verification

- Built packages at version **`0.1.0-preview.29`** using `eng/pack.ps1`.
- Verified consumer restore against nuget.org via `eng/verify-feed.ps1`.
- Restored in `Broiler.Browser` via local NuGet feed mapping without DLL overlay hacks. Built `Broiler.Browser.Cli` in `Release`.
- Browser test suites:
  - `Broiler.Browser.Core.Tests`: **319 passed**, 0 failed.
  - `Broiler.Browser.Cli.Tests`: **224 passed**, 0 failed.
  - `Broiler.Traffic.Tests`: **20 passed**, 0 failed.

### 3. Platform Probes Fixture

`docs/repros/sannysoft-platform-probes.html`:
- `mediaDevicesType`: `"object"` (previously `"undefined"`)
- `legacyGetUserMediaType`: `"function"` (previously `"undefined"`)
- `javascriptFailures`: **0**

### 4. Unmodified Live Sannysoft Analysis (`https://bot.sannysoft.com/`)

- Analysis completed with exit code 0.
- **Diagnostics: 0 JavaScript failure(s)** (all 31 scripts executed to full completion).
- Both historical script failures eliminated:
  - `inline-16: undefined is not a function` (`document.write(navigator.getUserMedia())`)
  - `Unhandled promise rejection: TypeError: Cannot get property enumerateDevices of undefined` (`navigator.mediaDevices.enumerateDevices()`)

### 5. Unmodified Live CreepJS Analysis (`https://abrahamjuliot.github.io/creepjs/`)

- Analysis completed with exit code 0.
- The listed core modules passed (`media passed`, `rects passed`, `navigator passed`, `headless passed`, `features passed`). Canvas/SVG were completed in the later scoped pass; large unsupported subsystems remain deferred.
- Zero regressions.

Durable snapshot: [fp04-mediadevices-observed-2026-10-09.json](repros/fp04-mediadevices-observed-2026-10-09.json).
