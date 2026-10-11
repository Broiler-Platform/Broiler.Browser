# FP-01: Worker-global interfaces, WorkerNavigator, and Worker constructor contracts

Implemented and verified on **2026-10-09**. This completes **FP-01 / P1** from the [compatibility roadmap](fingerprinting-compatibility-roadmap.md).

## Problem

1. **Missing worker-global interfaces and navigator:**
   In worker threads, `WorkerGlobalScope`, `DedicatedWorkerGlobalScope`, `WorkerNavigator`, and `WorkerLocation` were absent. References to `navigator` inside workers failed with `ReferenceError: navigator is not defined`, and prototype queries `self instanceof DedicatedWorkerGlobalScope` or `self.WorkerGlobalScope` failed.
2. **Missing instance constructor link on window `Worker`:**
   `new Worker(...)` constructed instances without linking their prototype to `Worker.prototype`. CreepJS checks worker constructor conformance via `x && x.__proto__.constructor.name == 'Worker'`. Because the handle had `Object.prototype`, CreepJS rejected the worker immediately, leading to `workerScope` being `undefined` on the main page.
3. **WebIDL constructor behavior:**
   `Worker(...)` called without `new` did not raise the WebIDL-mandated `TypeError`.

## Implementation

The fix was implemented cleanly within `Broiler.HtmlBridge.Dom` while preserving JSEAL engine neutrality:

- **`WorkerEnvironmentBinding.cs`**:
  - Registered worker-global interface constructors (`WorkerGlobalScope`, `DedicatedWorkerGlobalScope`, `WorkerNavigator`, `WorkerLocation`), throwing `TypeError: Failed to construct '<name>': Illegal constructor`.
  - Established the prototype hierarchy: `DedicatedWorkerGlobalScope.prototype` -> `WorkerGlobalScope.prototype` -> `EventTarget.prototype` / `Object.prototype`.
  - Configured `[Symbol.toStringTag]` for all four interfaces.
  - Set the worker realm global prototype to `DedicatedWorkerGlobalScope.prototype` via `realm.SetPrototype`.
  - Defined accessors on `WorkerNavigator.prototype` (`userAgent`, `platform`, `hardwareConcurrency`, `deviceMemory`, `language`, `languages`, `onLine`, `appCodeName`, `appName`, `appVersion`, `product`, `productSub`, `vendor`, `vendorSub`, `webdriver`) with receiver checks throwing `TypeError: Illegal invocation` when called on incompatible receivers.
  - Defined accessors on `WorkerGlobalScope.prototype` (`navigator`, `self`, `location`) and `DedicatedWorkerGlobalScope.prototype` (`name`).
  - Ensured worker interfaces remain strictly worker-only (never exposed on `window`).
- **`WorkerBinding.cs`**:
  - Updated `Register` to set `Worker.prototype`'s prototype to `EventTarget.prototype` and define `Symbol.toStringTag = 'Worker'`.
  - In `CreateWorker`, verified `call.NewTarget` and threw `TypeError: Failed to construct 'Worker': Please use the 'new' operator, this DOM object constructor cannot be called as a function.` when called without `new`.
  - Set instance prototype to `Worker.prototype` via `realm.SetPrototype`.
- **`JSWorker.cs`**:
  - Integrated `WorkerEnvironmentBinding.Install(realm, _name, location)`.
  - Added worker global `removeEventListener` matching `addEventListener`.

## Validation

### 1. Unit Tests (`Broiler.HtmlBridge`)

New test suite `WorkerInterfaceTests.cs` (11 tests) covers:
- Interface constructors, prototype hierarchy, `Symbol.toStringTag`, constructor invocation errors, accessor descriptors, receiver brand checks, `name`, worker-only exposure, `removeEventListener`, `Worker` instance identity (`hasConstructor`, `instanceof`), and `new.target` requirement.

Test suites executed:
- **`dotnet test -c Release`**: **2,071 passed**, 0 failed, 21 skipped (1m 5s).
- **`dotnet test -c Release-VM`**: **2,127 passed**, 0 failed, 21 skipped (1m 7s).
- **`python scripts/test-engine-neutrality.py`**: **6 tests passed, OK**.

### 2. Packaging and Verification

- Built packages at version **`0.1.0-preview.26`** using `eng/pack.ps1`.
- Verified consumer restore against nuget.org via `eng/verify-feed.ps1`.
- Restored in `Broiler.Browser` via local NuGet feed mapping (no DLL overlay hacks). Built into isolated directory `artifacts/fp01-worker-2026-10-09/build`.

### 3. HTTP Probe Fixture

Running `fingerprinting-current-probes.html` over HTTP produced:
- `worker.state`: `"message"`
- `worker.data.selfIsGlobal`: `true`
- `worker.data.navigator`: `"object"`
- `worker.data.WorkerNavigator`: `"function"`
- `worker.data.WorkerGlobalScope`: `"function"`
- `worker.data.DedicatedWorkerGlobalScope`: `"function"`
- `worker.data.platform`: `"Win32"`
- `worker.data.userAgent`: `"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Broiler/1.0"`
- `workerConstructor.hasConstructor`: `true`
- `workerConstructor.isWorker`: `true`
- `workerConstructor.isEventTarget`: `true`
- `workerConstructor.toString`: `"[object Worker]"`
- Window `interfaces.WorkerGlobalScope`: `"undefined"` (worker-only exposure confirmed).

### 4. Unmodified Live CreepJS Analysis

Executing live CreepJS (`--analyze https://abrahamjuliot.github.io/creepjs/ --output-dir artifacts/fp01-worker-2026-10-09/creepjs --no-window`):
- Analysis completed with **exit code 0**.
- **`✔ (1886.20ms) dedicated worker passed`** in `console.log`.
- `workerScope` fingerprint was computed, parsed, and recorded in the Loose Fingerprint JSON with hash:
  `$hash: "36369a925e427e59ef234d56d2ce657096ef168da7bfef9b3836fb7c68ea2835"`.
- `ReferenceError: navigator is not defined` unhandled rejection is **completely eliminated**.
- `TypeError: Cannot get property platform of undefined (evaluating 'workerScope.platform')` is **completely eliminated**.
- Only 4 script errors remain on the entire page:
  - `ReferenceError: Window is not defined` (FP-02)
  - `TypeError: Cannot get property bottom of undefined (evaluating 'domRect.bottom')` (FP-03)
  - `TypeError: undefined is not a function` in SVG and Canvas2D (FP-05)

### 5. Unmodified Live Sannysoft Analysis

- Sannysoft analysis completed with exit code 0.
- Scanner rows, canvas results, and baseline behavior preserved with zero regressions.

Durable snapshot: [fp01-worker-environment-observed-2026-10-09.json](repros/fp01-worker-environment-observed-2026-10-09.json).
