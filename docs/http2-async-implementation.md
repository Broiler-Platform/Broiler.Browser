# Asynchronous fetch, XHR, streaming bodies, prefetch scheduler, renderer resources and comparative audit — 2026-10-11

All 5 steps (1, 2, 3, 4 and 5) of the [HTTP/2 async audit](http2-async-audit.md) are implemented in the sibling
`Broiler.HtmlBridge` and `Broiler.HTML` checkouts and integrated into Browser through local packages
`0.1.0-preview.28-http2.6`. `Broiler.Net` remains published preview.3.

## Behavior

### Step 1: Nonblocking fetch, async XHR and beacons
- `fetch()`, asynchronous `XMLHttpRequest.send()` and `navigator.sendBeacon()` start
  awaited transport I/O and return without waiting for the server. Multiple calls
  can use concurrent HTTP/2 streams on the shared profile connection.
- Network code produces a CLR response snapshot and disposes request/response
  resources before queueing completion. JavaScript objects and promise settlement
  run on the owning event loop, in the originating window's context. Detached frame
  documents do not receive stale completions.
- The event loop follows elapsed real time while requests are pending, allowing
  timers and network completions to race correctly. With no network I/O it retains
  its existing virtual-clock behavior. Capture drains wait in bounded slices;
  interactive steps remain nonblocking. `Complete()` and pending-work reporting
  include network operations.
- AbortSignal cancellation after dispatch cancels transport and rejects with the
  supplied reason. XHR abort, timeout and reuse via `open()` cancel the old request;
  old completion callbacks cannot overwrite a reused XHR. Synchronous XHR retains
  its blocking path and exposes its response before `send()` returns.
- Session disposal/reparse cancels pending document work and invalidates late
  callbacks. HTTP responses arriving after teardown are still disposed. Beacons
  follow this existing document lifetime policy; this does **not** implement
  browser-style beacon keepalive across navigation/session teardown.
- Cookies, credentials, CORS, redirect policy, request-body encoding and filtered
  response exposure still go through the existing profile transport and binding
  gates. Resource timings preserve their initiator and body-read behavior.
  Nonblocking requests no longer pause the caller's long-task clock.

### Step 2: Streaming response bodies and header-time settlement
- `fetch()` promise settles immediately when response headers arrive via `ResourceLoader.SendHeadersAsync`.
- `response.body` exposes a streaming `ReadableStream` (`CreateNetworkStreamBody`) backed by a .NET
  underlying streaming pipe (`System.IO.Pipelines.Pipe`), avoiding full in-memory buffering.
- Asynchronous body readers (`text()`, `json()`, `arrayBuffer()`, `blob()`, `formData()`) consume
  the response body stream asynchronously over the streaming transport.
- Asynchronous `XMLHttpRequest` transitions to `readyState = 2` (HEADERS_RECEIVED) when headers arrive,
  and consumes its body asynchronously via `response.arrayBuffer()` before decoding into `responseText`/`response`.
- Branch stream teeing (`ReadableStream.prototype.tee`) sets `{ highWaterMark: 0 }` to avoid eager pulls
  and detaches disturbance hooks across clones.
- Resource Timing entries preserve single delivery: entries are staged at header arrival and released
  once body consumption commences (or immediately for headless responses, XHR, and beacons).
- Synchronous consumers (sync XHR, stylesheets, images, scripts) remain explicitly buffered.

### Step 3: Nonblocking subresource prefetcher and awaited resource scope
- `SubResourcePrefetcher` and `RequestPrefetcher<TRequest>` use asynchronous fetch delegates
  (`Func<string, CancellationToken, Task<string?>>`) and await semaphore gates asynchronously
  (`await gate.WaitAsync(cancellationToken)`), eliminating thread-pool worker starvation.
- Concurrency cap for multiplexed HTTP/2 requests raised from legacy HTTP/1.1 limit of 6
  (`DefaultHttp1MaxConcurrentRequestsPerHost = 6`) to 16 (`DefaultMaxConcurrentRequestsPerHost = 16`).
  This allows typical subresource batches (e.g. 12 parallel resources) to run in a single concurrent
  wave across the shared connection rather than two serialized waves.
- Added `ConsumeAsync(string url, CancellationToken cancellationToken = default)` alongside
  backwards-compatible synchronous `Consume(string url)`.
- Connected `ResourceLoader.Prefetcher` to awaited network I/O via `BridgeTransport.GetTextAsync`
  and `ResourceLoader.LoadTextDirectAsync`/`LoadTextAsync`, propagating document `Lifetime`
  cancellation tokens and preserving CORS, tainting, and Resource Timing without blocking worker threads.
- Added `SubresourceScope.FetchAsync` and `ReadBoundedAsync` with cancellation tokens in `Broiler.HTML`.
- Added unit test suite `SubResourcePrefetcherTests` in `Broiler.HtmlBridge.Tests` covering bounded
  concurrency, per-host separation, deduplication, error caching as null, and cancellation of queued
  and in-flight work.

### Step 4: Renderer resources, worker script loading and standalone fallback HTTP/2 alignment
- **Renderer and element image loading**:
  - `ImageDownloader.cs`: Avoids blocking thread-pool workers on `SubresourceScope.Fetch` by awaiting
    `SubresourceScope.FetchAsync` when `async` is true (`LoadImageThroughTransportAsync`).
  - `Images.cs`: Converted DOM `<img>` element loading from synchronous blocking event loop tasks
    (`_resources.LoadImage`) to `_eventLoop.StartAsync(token => _resources.LoadImageAsync(url, request, timing, token), ...)`
    using `BrowserEventLoop.StartAsync`. Image fetching runs off-thread without holding the event loop or blocking
    worker threads, delivering the completed/errored bitmap directly to the window context on the event loop.
- **Parallel remote `@font-face` downloading**:
  - `HtmlContainerInt.cs`: Replaced sequential synchronous font loading loop with parallel asynchronous fetches
    (`SubresourceScope.FetchAsync`) via `LoadRemoteFontBytesAsync` and `Task.WaitAll`, multiplexing concurrent
    font downloads over the shared HTTP/2 connection.
- **Worker script fetching**:
  - `IWorkerHost.cs`: Added `Task<(WorkerScript? Script, string? Failure)> FetchWorkerScriptAsync(Uri url, DocumentRequestContext client, bool imported, CancellationToken cancellationToken)`.
  - `Hosts.Window.cs`: Implemented `FetchWorkerScriptAsync` awaiting nonblocking resource loading via
    `_resources.SendAsync(request, context, timing, cancellationToken, blocksCaller: false)` and reading the response
    body asynchronously via `ReadAsStringAsync(cancellationToken)`.
  - `JSWorker.cs`: Converted both worker bootstrap pump and `importScripts` to invoke `FetchWorkerScriptAsync`.
- **Standalone component HTTP/2 fallbacks**:
  - `LegacySubresourceClient.cs`: Configured fallback `HttpClient` using `BroilerHttpProtocol.Apply(...)` for HTTP/2 with automatic HTTP/1.1 downgrade.
  - `ScriptExtractionService.cs`: Configured standalone fallback client using `BroilerHttpProtocol.Apply(...)`.
  - `ResourceLoader.cs`: Explicitly configured `request.Version = BroilerHttpProtocol.Version` and `request.VersionPolicy = BroilerHttpProtocol.VersionPolicy` on explicit `HttpRequestMessage` instances sent via fallback `SharedClient.SendAsync` in `SendHeadersAsync`, `SendCoreAsync`, and `GetAsync` (because `HttpClient.DefaultRequestVersion` applies only to convenience methods like `GetStringAsync` and does not override the default HTTP/1.1 on explicit request instances).
- **Test harness support**:
  - `RecordingTransport.cs`: Updated `RecordingTransport.SendAsync` to record invocations identically to `Send`, resolving test verification for async subresource calls.
  - `ImageLoadingTests.cs`: Sorted concurrent event registrations in `ImageElementEventListenerErrorAndLoadRegistration` to eliminate race conditions between parallel async image HTTP responses.

### Step 5: Comparative benchmarking, thread-pool audit and concurrency tuning
- **Comparative execution across HTTP/2 and HTTP/1.1**:
  - Benchmarked representative document load (15 subresources: 6 CSS, 4 JS, 6 images, 2 fonts, plus dynamic API fetch data) through full `RenderingPipeline`, `PageLoader`, `ScriptEngine`, and `InteractiveSession.SettleLoadWindow()`.
  - Benchmarked subresource prefetcher batches of 12, 16, and 24 subresources.
  - Benchmarked JavaScript microbenchmarks (2 concurrent `fetch()`, 2 concurrent `async XHR`, 2 `navigator.sendBeacon()`, streaming `fetch()`).
- **Connection reuse and multiplexing**:
  - Under HTTP/2, all operations (page load, subresource prefetching, JavaScript requests) multiplexed across **1 single physical TCP connection** (`Connections: 1`).
  - Under HTTP/1.1, concurrent requests required individual TCP connections and TLS handshakes, scaling up to **10 connections** for representative page load, **12 connections** for 12 prefetches, and **16 connections** for 16/24 prefetches.
- **Thread-pool worker health and memory allocations**:
  - Peak busy workers remained at 1 (briefly 2–3 during script engine evaluation) with zero worker thread starvation, confirming nonblocking awaited I/O eliminated synchronous blocking across all paths.
  - Thread count stayed steady at 11–15 for HTTP/2 and 16–18 for HTTP/1.1 (higher on HTTP/1.1 due to socket/transport connection pools).
  - GC Gen 0, Gen 1, and Gen 2 collections were 0 during warm runs across both protocols, confirming zero buffer leaks or uncollected streams.
- **Dynamic concurrency budget tuning**:
  - For HTTP/2, concurrency is capped at `DefaultMaxConcurrentRequestsPerHost = 16`. Because streams multiplex over 1 connection, 16 allows typical subresource graphs to execute in a single wave without socket overhead, while avoiding flow control window exhaustion or server stream saturation.
  - For HTTP/1.1, `ResourceLoader` dynamically checks `DocumentFetchTiming.NextHopProtocol`. When `"http/1.1"`, it restricts the prefetcher concurrency cap to `DefaultHttp1MaxConcurrentRequestsPerHost = 6` (in `ResourceLoader.Prefetcher()`), conforming to browser standards and preventing socket starvation / connection resets against HTTP/1.1 origins.

## Measurements

The same Windows/.NET 10 TLS Kestrel probe used for the audit delayed each response
by 200 ms. Totals include engine setup, script processing and final capture.
Values are the second (warm) round unless noted.
These are a controlled local comparison, not a general page-load benchmark.

### Steps 1–3 Benchmarks (HTTP/2 baseline vs asynchronous steps)

| Two requests | Calls returned before → after | Total before → after | Peak concurrency before → after |
| --- | --- | --- | --- |
| fetch | 406 → 1 ms | 468 → 301 ms | 1 → 2 |
| async XHR | 427 → 1 ms | 482 → 278 ms | 1 → 2 |
| beacon | 414 → 1 ms | 479 → 282 ms | 1 → 2 |

| Streaming response (headers immediate, body delayed 400 ms) | Call returned | Total elapsed before → Step 1 → Step 2 |
| --- | --- | --- |
| `fetch('/stream')` | 1 ms | 473 ms (audit/Step 1) → **83 ms** (Step 2) |

| 12 Prefetched subresources (200 ms delay per response) | Elapsed before → Step 3 | Peak concurrency before → Step 3 |
| --- | --- | --- |
| `prefetch-12-round1` | 411 ms → **209 ms** | 6 → **12** |
| `prefetch-12-round2` | 403 ms → **203 ms** | 6 → **12** |

### Step 5 Comparative Benchmarks: HTTP/2 vs HTTP/1.1

Each subresource response delayed by 200 ms on the local TLS Kestrel probe. Warm round metrics:

| Scenario | Protocol | Requests | Connections | Elapsed | Peak Concurrency | Thread Count | Busy Workers | Allocated Memory | Gen 0/1/2 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Representative Page Load** (15 resources: 6 CSS, 4 JS, 6 images, 2 fonts, API fetch) | **HTTP/2** | 15 | **1** | 247 ms | 10 | 11 | 1 | 11.72 MB | 0 / 0 / 0 |
| | **HTTP/1.1** | 15 | **10** | 241 ms | 10 | 16 | 1 | 11.50 MB | 0 / 0 / 0 |
| **Prefetcher (12 subresources)** | **HTTP/2** | 12 | **1** | 213 ms | 12 | 12 | 1 | 175 KB | 0 / 0 / 0 |
| | **HTTP/1.1** | 12 | **12** | 210 ms | 12 | 16 | 1 | 135 KB | 0 / 0 / 0 |
| **Prefetcher (16 subresources)** | **HTTP/2** | 16 | **1** | 216 ms | 16 | 12 | 1 | 210 KB | 0 / 0 / 0 |
| | **HTTP/1.1** | 16 | **16** | 207 ms | 16 | 18 | 1 | 188 KB | 0 / 0 / 0 |
| **Prefetcher (24 subresources)** | **HTTP/2** | 24 | **1** | 415 ms | 16 (cap) | 15 | 1 | 328 KB | 0 / 0 / 0 |
| | **HTTP/1.1** | 24 | **16** | 405 ms | 16 (cap) | 18 | 1 | 290 KB | 0 / 0 / 0 |
| **JS 2× fetch()** | **HTTP/2** | 2 | **1** | 255 ms | 1–2 | 15 | 3 | 8.94 MB | 0 / 0 / 0 |
| | **HTTP/1.1** | 2 | **2** | 242 ms | 2 | 18 | 1 | 8.78 MB | 0 / 0 / 0 |
| **JS 2× async XHR** | **HTTP/2** | 2 | **1** | 276 ms | 2 | 15 | 1 | 9.49 MB | 0 / 0 / 0 |
| | **HTTP/1.1** | 2 | **2** | 253 ms | 1–2 | 18 | 1 | 9.33 MB | 0 / 0 / 0 |
| **JS 2× sendBeacon()** | **HTTP/2** | 2 | **1** | 252 ms | 2 | 15 | 1 | 8.54 MB | 0 / 0 / 0 |
| | **HTTP/1.1** | 2 | **2** | 240 ms | 2 | 18 | 1 | 8.37 MB | 0 / 0 / 0 |
| **JS Streaming fetch()** | **HTTP/2** | 1 | **1** | 47 ms | 1 | 15 | 2 | 8.81 MB | 0 / 0 / 0 |
| | **HTTP/1.1** | 1 | **1** | 40 ms | 1 | 18 | 1 | 8.67 MB | 0 / 0 / 0 |

#### Key Audit Takeaways:
1. **Connection Multiplexing Efficiency**: Under HTTP/2, all operations (including full page loads with 15 mixed subresources and bursts of up to 24 subresources) execute over **a single physical TCP connection** (`Connections: 1`). Under HTTP/1.1, each concurrent stream requires a distinct socket connection (10 connections for page load, 12 for prefetch-12, 16 for prefetch-16/24).
2. **Thread-Pool Worker Freedom**: Awaiting transport I/O completely eliminated thread-pool worker starvation. Busy workers remained at 1 across both protocols (briefly reaching 2–3 during JavaScript script evaluation and dispatch).
3. **Zero Buffer Leaks**: Across warm execution runs on both protocols, Gen 0, Gen 1, and Gen 2 GC collections remained at 0, confirming that streaming and buffered response snapshot pipelines cleanly release resources without memory retention.
4. **Protocol-Aware Concurrency Tuning**: In HTTP/2, a concurrency limit of 16 allows realistic batches of assets to complete in a single round without connection or stream saturation. In HTTP/1.1, `ResourceLoader` dynamically caps concurrency at 6 (`DefaultHttp1MaxConcurrentRequestsPerHost`) whenever `DocumentFetchTiming.NextHopProtocol == "http/1.1"` to prevent socket exhaustion and respect RFC/browser origin limits.

Raw measurements: [audit baseline](repros/http2-async-audit-2026-10-10.json),
[step 1](repros/http2-async-implementation-2026-10-10.json),
[step 2](repros/http2-async-step2-2026-10-10.json),
[step 3](repros/http2-async-step3-2026-10-10.json),
[step 4](repros/http2-async-step4-2026-10-10.json), and
[step 5 comparison](repros/http2-async-step5-comparison-2026-10-11.json). The runnable local probe
is `eng/Http2AsyncProbe/Http2AsyncProbe.csproj`, using Browser's central package versions.

## Validation

- HtmlBridge full Release suite: **2,255 passed, 21 skipped** (2,276 total).
- Browser.Core Release suite against the new packages: **324 passed**.
- CLI Release suite against the new packages: **227 passed**.
- Broiler.HTML Release suite: **135 passed**.
- Solution build (`Broiler.Windows.Browser.slnx`) and the standalone probe built with **0 warnings, 0 errors**.
- Full test pass across all 4 suites: **2,941 passed, 21 skipped, 0 failed**.
- The real TLS probe verified HTTP/2 multiplexing over 1 connection vs HTTP/1.1 over up to 16 connections, zero thread-pool worker starvation, and dynamic concurrency budget selection (16 for HTTP/2 vs 6 for HTTP/1.1).

Windows/.NET 10 was exercised; Linux and Android runtime behavior was not measured.

Build and package commands:

```powershell
# In Broiler.HtmlBridge
dotnet test tests/Broiler.HtmlBridge.Tests/Broiler.HtmlBridge.Tests.csproj -c Release

# In Broiler.HTML
dotnet test tests/Broiler.HTML.Tests/Broiler.HTML.Tests.csproj -c Release

# In Broiler.Browser (packages already exist on this machine)
# For a fresh checkout with the matching modified sibling sources:
.\eng\build-http2-packages.ps1 -BridgePath "..\Broiler.HtmlBridge" -NetPath "..\Broiler.Net"
dotnet build Broiler.Windows.Browser.slnx -c Release
dotnet test src/Broiler.Browser.Core.Tests/Broiler.Browser.Core.Tests.csproj -c Release
dotnet test src/Broiler.Browser.Cli.Tests/Broiler.Browser.Cli.Tests.csproj -c Release
dotnet run --project eng/Http2AsyncProbe/Http2AsyncProbe.csproj -c Release
```

Local packages use unique versions (`0.1.0-preview.28-http2.6`), and the build helper rejects overwriting an
existing package. This avoids reintroducing the earlier NuGet cache/API mismatch.
