# CreepJS P0: proxy recursion no longer terminates the host

Implemented and verified on 2026-10-09 against the updated Broiler.JS checkout
at `a7bdfed6141837aac35c4f26d3efd78bde6b670a`.

**The P0 crash is fixed locally.** The reduced prototype-cycle page now catches
`RangeError: Maximum call stack size exceeded`. The unmodified live CreepJS site
completes CLI analysis, including the BrowserApp window pass, and ordinary image
capture. Both commands exit 0 and save their artifacts. Full site compatibility
still requires the P1 and API work in the
[original investigation](creepjs-investigation-2026-10-09.md).

## Change

The implementation is in the sibling JavaScript repository:

- `D:/Broiler.JS/Broiler.JS/Broiler.JavaScript.BuiltIns/Proxy/JSProxy.cs`
- `D:/Broiler.JS/Broiler.JS/Broiler.JavaScript.BuiltIns.Tests/ProxyRecursionTests.cs`

Proxy forwarding can recurse through CLR property/prototype operations without
entering a new JavaScript function. The existing function-entry stack checks
therefore did not protect the CreepJS cycle.

Each of the 17 principal proxy operation entry points now acquires a lightweight
scope. A thread-local counter limits simultaneous proxy operation nesting to 256.
Exceeding that budget raises a JavaScript `RangeError`. Disposing the scope
decrements the counter on both normal and exceptional returns. There is no
per-operation heap allocation or process-wide shared counter.

`RuntimeHelpers.TryEnsureSufficientExecutionStack()` remains a second guard at
operation entry and target access, covering a smaller available native stack and
target-only consumers such as `IsArray`. The change does not modify ordinary or
proxy prototype-setting semantics, trap ordering, or cycle detection. Finite
reentrant traps are still allowed; the same proxy may be visited repeatedly.

The 256-operation limit is an implementation resource limit. Extremely deep but
finite proxy chains can also reach it. This is deliberately earlier than the
CLR's last available stack reserve: JavaScript catch/finally callbacks need room
to classify the error and restore the prototype. The initially tested native
stack check alone stopped process termination but left too little room for
CreepJS's cleanup. The final bounded implementation gets through the probe phase.

## Validation

| Check | Result |
| --- | --- |
| BuiltIns suite, Release | **2,429 passed, 0 failed, 0 skipped** |
| New regression coverage | **25 cases**, included in the suite |
| Original reduced crash page, separate CLI process | Exit 0; page catches `RangeError`; JSON output written |
| Unmodified live `--analyze`, 1280×900 | Exit 0; 22.86 s; report and screenshots written; watchdog did not fire |
| Separate BrowserApp window phase inside analysis | Completed; reaches fingerprinting and hashing |
| Unmodified live `--capture-image`, 1280×900 | Exit 0; PNG and diagnostic bundle written |

Regression cases cover string, integer and symbol reads/writes, `in`, both
prototype-setting APIs, `__proto__` coercion, cyclic handler lookup, recursive
metadata/prototype traps, repeated recovery, valid 128-proxy chains, receiver
preservation, finite reentry, revocation, and ordinary cycle rejection. Dedicated
512 KiB and 1 MiB thread-stack cases exercise native-function mutation with
JavaScript calls in catch/finally, verifying that cleanup and later proxy access
still work. The tests run in the `dotnet test` child testhost; live and reduced
browser probes run in separate CLI processes.

The live console records **73 API properties analyzed**, followed by
“Fingerprinting complete”, “Hashing complete”, and “loose fingerprint passed”
in both analysis and window runs. These lines show execution continued beyond
the advanced proxy tests without disabling them. CreepJS still calls the
properties “corrupted”; that heuristic is not the acceptance criterion for P0.

The screenshots still show measurement elements instead of the completed
dashboard. The earlier `DocumentFragment`/iframe fallback and console-method
failures remain. Other recorded failures include missing Crypto/interface and
graphics functionality. A successful CLI exit establishes crash recovery, not
correct final page content.

## Local integration and reproducibility

Browser remains on its existing package references. Validation used a separate
copy of its Release CLI output with **only `Broiler.JavaScript.BuiltIns.dll`
replaced** by the build from the updated JS checkout plus this patch. Other
JavaScript assemblies remained the restored preview.6 packages. The loaded
BuiltIns informational version is
`0.1.0-preview.1+a7bdfed6141837aac35c4f26d3efd78bde6b670a`; that version alone does
not encode the uncommitted patch, so the evidence records the binary SHA-256 too.

No NuGet package was published or package version changed. A normal Browser
rebuild still consumes its published dependency; it needs a subsequent fixed
package release and dependency update to carry this fix by default. The ready
local CLI is:

`D:/Broiler.Browser/artifacts/creepjs-p0-2026-10-09/cli/Broiler.Cli.dll`

To recreate a local validation CLI, run from `D:/Broiler.Browser`:

```powershell
dotnet build D:/Broiler.JS/Broiler.JS/Broiler.JavaScript.BuiltIns/Broiler.JavaScript.BuiltIns.csproj -c Release
dotnet build src/Broiler.Browser.Cli/Broiler.Browser.Cli.csproj -c Release `
  --artifacts-path D:/Broiler.Browser/artifacts/creepjs-p0-rebuild

$cliDir = 'D:/Broiler.Browser/artifacts/creepjs-p0-rebuild/bin/Broiler.Browser.Cli/release'
Copy-Item D:/Broiler.JS/Broiler.JS/Broiler.JavaScript.BuiltIns/bin/Release/net10.0/Broiler.JavaScript.BuiltIns.dll $cliDir
$cli = Join-Path $cliDir 'Broiler.Cli.dll'

dotnet $cli --evaluate-page docs/repros/creepjs-proxy-cycle.html `
  --evaluate "document.getElementById('result').textContent" `
  --output artifacts/creepjs-p0-rebuild/reduced.json
dotnet $cli --analyze https://abrahamjuliot.github.io/creepjs/ `
  --output-dir artifacts/creepjs-p0-rebuild/live --width 1280 --height 900 `
  --analysis-timeout 180 --verbose
dotnet $cli --capture-image https://abrahamjuliot.github.io/creepjs/ `
  --output artifacts/creepjs-p0-rebuild/capture.png --width 1280 --height 900 `
  --diagnostic-dir artifacts/creepjs-p0-rebuild/capture-diagnostics
```

Tests, from `D:/Broiler.JS`:

```powershell
dotnet test Broiler.JS/Broiler.JavaScript.BuiltIns.Tests/Broiler.JavaScript.BuiltIns.Tests.csproj `
  -c Release --logger 'trx;LogFileName=builtins.trx' `
  --results-directory artifacts/creepjs-p0
```

## Saved evidence

- [Durable result snapshot and hashes](repros/creepjs-p0-observed-2026-10-09.json).
- [Live analysis report](../artifacts/creepjs-p0-2026-10-09/live-bounded/report.html).
- [Unmodified live capture](images/creepjs-p0-capture-2026-10-09.png).
- [Reduced browser reproduction](repros/creepjs-proxy-cycle.html).

Full logs and images are under `artifacts/creepjs-p0-2026-10-09`; test results are
under `D:/Broiler.JS/artifacts/creepjs-p0`. Artifact directories are ignored by Git.
Only the targeted JavaScript implementation/tests and these documentation/evidence
files were changed; the pre-existing Browser work was preserved.
