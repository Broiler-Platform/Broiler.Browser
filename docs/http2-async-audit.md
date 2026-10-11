# HTTP/2 and asynchronous request audit — 2026-10-10

HTTP/2 is working, including multiplexing on one connection. The browser and its
dependent loaders do **not** yet use asynchronous I/O effectively end to end.
The earlier HTTP/2 change made the synchronous transport compatible with HTTP/2;
it did not remove blocking in its consumers.

## Scope and evidence

Inspected the browser's current package pins and the matching component source:

- Broiler.Net `0.1.0-preview.3`, package source commit `0f8bd3c`.
- Broiler.HtmlBridge `0.1.0-preview.27`, package source commit `baed39c`.
- Broiler.HTML `0.1.0-preview.27`, package source commit `590d7b5`.
- Broiler.Layout's legacy HTTP policy helpers and the browser composition root.

The relevant source files matched the commits identified in the packages. Runtime
probes used the released Net and HtmlBridge packages, the Broiler.JS script engine,
and an in-process TLS Kestrel server on Windows/.NET 10.0.12. A private test
certificate was accepted only by the probe's handler; system trust was not changed.
The server recorded the actual request protocol, connection IDs and overlapping
requests. The same warmed session was used for two rounds.

Each ordinary request delayed its response by 200 ms. The streaming case flushed
headers and a first chunk immediately, then delayed the final chunk by 400 ms.
These are controlled latency probes, not production throughput benchmarks.

| Path | Observed result, two rounds | Concurrent requests | Connections |
| --- | --- | --- | --- |
| 12 awaited transport requests, `Task.WhenAll` | 203–216 ms total | 12 | 1 |
| 12 requests through the existing prefetcher | 399–411 ms total | 6 | 1 |
| Two JavaScript `fetch()` calls | Calls returned after 406–456 ms | 1 | 1 |
| Two XHR calls opened with `async=true` | Calls returned after 427–489 ms | 1 | 1 |
| Two `sendBeacon()` calls | Calls returned after 414–421 ms | 1 | 1 |
| `fetch()` with immediately flushed headers and delayed body | Call returned after 406–409 ms | 1 | 1 |

Every recorded request used HTTP/2. JavaScript figures are measured inside the page
and exclude engine setup/settling. The first two rows measure the whole batch.
For prefetching, the probe exercised the real public `SubResourcePrefetcher` with
the same synchronous session/body-read shape its production callers use; it did
not benchmark a full rendered page.

[Raw measurements](repros/http2-async-audit-2026-10-10.json). The runnable probe is
in the local ignored `artifacts/http2/async-audit` directory:

```powershell
dotnet run --project artifacts/http2/async-audit/AsyncAudit.csproj -c Release
```

## Findings, in priority order

### 1. JavaScript fetch, asynchronous XHR and beacons block the script thread

`FetchBinding.Callbacks.cs:193` calls `SendAuthorRequest` before returning its
promise. `FetchBinding.Requests.cs:293` implements that call with
`_resources.SendAsync(...).GetAwaiter().GetResult()`.
`FetchBinding.Xhr.cs:452` and `FetchBinding.Beacon.cs:82` use the same path.

Consequently, starting a second fetch waits for the first one to finish. Wrapping
the calls in `Promise.all` cannot restore the concurrency lost while evaluating
its arguments. Other work on that script thread also waits for network I/O.
This is both a concurrency issue and a browser API behavior issue. A promise
return type alone does not make the operation nonblocking.

**Recommended change:** prepare the request on the realm thread, start asynchronous
I/O, return a pending promise immediately, and post completion back to the realm's
event loop. Keep DOM/JavaScript work on its owning thread. Preserve document
lifetime, CORS, cookies, ordering and abort semantics. Queue beacon work without
waiting for a response; keep explicit synchronous XHR separate from async XHR.

### 2. The resource loader waits for and buffers the entire response body

`ResourceLoader.cs:462` awaits response headers, then line 468 calls
`LoadIntoBufferAsync` before returning. Thus even after fixing the binding's wait,
fetch resolution would still be delayed until the body completes. The streaming
probe confirmed this delay. `ExposeResponse` in `FetchBinding.Requests.cs:361`
then obtains a byte array from the buffered content.

**Recommended change:** separate receiving headers from consuming the body. Retain
response ownership through asynchronous body consumption, cancellation and
disposal; settle `fetch()` at the headers. Adapt the existing buffered response
body representation deliberately rather than merely removing `LoadIntoBufferAsync`.
Some consumers genuinely need complete text/image/font bytes, but the network
layer should not force that policy on every caller. This also reduces avoidable
memory retention/copies for larger responses.

### 3. Prefetching caps HTTP/2 at six requests and blocks worker threads

`SubResourcePrefetcher.cs:53` sets a per-host limit of six, explicitly justified
in its comment by HTTP/1.1 connection conventions. At lines 137–139 it schedules
one `Task.Run` per URL and uses synchronous `SemaphoreSlim.Wait`. The fetch
delegate itself blocks, as does `Consume` at line 113.

The probe demonstrated two waves for 12 requests even though the same transport
can multiplex all 12. The gates are per-prefetcher rather than a profile-wide
resource budget. Queued URLs can occupy workers while merely waiting for a gate.

**Recommended change:** use an asynchronous fetch delegate, `WaitAsync` with
cancellation, and a bounded scheduling policy suited to multiplexed requests.
Preserve deduplication, request-context matching, script execution order and
HTTP/1.1 fallback. Separate parallel downloading from ordered application. Do not
replace six with an unlimited value; concurrency still needs memory and origin
budgets and should be measured on real pages.

### 4. Renderer resources still use blocking I/O, including their “async” image path

- `ImageDownloader.cs:89` queues a worker for an asynchronous image, but its worker
  calls the blocking `SubresourceScope.Fetch` at line 146.
- `SubresourceScope.cs:154` calls `transport.Send`, then synchronously reads the
  response stream. Cancellation has to dispose the response underneath that read.
- `HtmlContainerInt.cs:839` loops through font faces; line 976 fetches each remote
  font synchronously. Independent font downloads are not overlapped by this loop.
- Renderer stylesheets use the same blocking scope. HtmlBridge's script/style
  loaders additionally block through `BridgeTransport.GetText`.
- Module loading (`Hosts.Window.cs:606`) and sub-document retrieval
  (`SubDocuments.cs:860`) synchronously wait on resource tasks.

`BrowserNetworkSession.cs:382` now bridges these synchronous callers to HTTP/2 by
running `SendAsync` via `Task.Run` and blocking the caller. This is a compatibility
bridge, not an end-to-end async implementation. With image/prefetch workers it
adds another scheduling step while the original worker remains blocked. HTTP/2
can still multiplex requests issued by different workers, but the approach uses
more threads than awaited I/O. Thread-pool starvation under load is a risk inferred
from these paths, not something measured by this small probe.

**Recommended change:** add asynchronous resource-scope methods, propagate them
through image/style/font loaders, and retain a synchronous boundary only where
required. Prefetch independent resources without changing DOM, module evaluation,
style application or render-thread ownership rules.

### 5. Standalone component fallbacks do not consistently select HTTP/2

The browser injects its profile transport, so these are not the primary browser
path. They still matter to other hosts using the components directly:

- `ScriptExtractionService.cs:56` applies only the User-Agent to its fallback
  client, leaving its HTTP version at the default.
- HTML's `LegacySubresourceClient` likewise does not opt into HTTP/2.
- `ResourceLoader.cs:47` applies the new client defaults, but its manual
  `SendAsync(request, ...)` path at line 490 sends a newly constructed request with
  its own default version/policy. Client defaults do not replace an explicit
  request object's version. Its `GetStringAsync` convenience path is different.
  This behavior is documented in
  [HttpClient.DefaultRequestVersion](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.defaultrequestversion?view=net-10.0).
- Layout retains an older HTTP/1.1 policy helper; it is not the policy selected by
  the browser's Net session.

**Recommended change:** centralize request-level protocol configuration and audit
standalone entry points separately. Do not force version 2 onto synchronous
`HttpClient.Send` fallbacks before converting their transport operation.

## What is already correct

- One long-lived profile network serves navigation and subresources. Pooling and
  connection reuse work; the probe proved multiple HTTP/2 streams on one connection.
- Top-level page fetching uses awaited `SendAsync` and body reads in
  `PageLoader.cs:216`, with cancellation. It does buffer the document before
  parsing; incremental parsing is a separate architectural optimization.
- The transport's async path uses `ConfigureAwait(false)`, and protocol selection
  permits HTTP/1.1 fallback without enabling HTTP/3.
- `BrowserApp.cs:864` moves loading/render work off the UI thread. This remains
  useful for CPU-bound parsing and scripting; indiscriminately removing every
  `Task.Run` would be wrong.
- `RenderingPipeline.cs:101` still enters synchronous script extraction after
  fetching the document. The async method name does not describe every phase.
- Cookies, redirect processing, CORS checks, resource caches and response disposal
  must remain intact when changing the scheduling model.

Do not start by enabling multiple HTTP/2 connections. The controlled server
already accepted 12 concurrent streams on one connection. Microsoft's
[EnableMultipleHttp2Connections documentation](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.enablemultiplehttp2connections?view=net-10.0)
notes that multiple connections to the same server depart from HTTP/2's normal
connection model. There is no evidence here that this switch, connection-window
tuning or a larger thread pool would address the demonstrated bottlenecks.
Microsoft's [thread-pool starvation guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-threadpool-starvation)
describes the symptoms to measure before diagnosing actual starvation.

## Suggested implementation order and acceptance checks

1. Make fetch/async XHR/beacons nonblocking, with safe event-loop completion and
   lifetime/abort behavior. Two delayed requests should overlap, and timers should
   run while requests are pending.
2. Separate headers and body ownership. Fetch should settle after headers while
   body reads remain pending; cancellation/disposal must release the HTTP/2 stream.
3. Convert the prefetch scheduler and common resource-scope methods to awaited
   I/O. Validate bounded concurrency, ordering, deduplication and cancellation of
   both queued and active work.
4. Move renderer and module/frame consumers onto those methods where their host
   contracts permit it, then align standalone fallback clients.
5. Compare representative page load time, peak blocked workers, allocations and
   server concurrency over both HTTP/2 and HTTP/1.1; retain existing cookie/CORS/
   redirect tests. Only then tune concurrency budgets.

This audit recorded the original state. All 5 steps (1, 2, 3, 4 and 5) have now been fully
implemented and verified; see [the implementation report](http2-async-implementation.md)
and its comparative measurements.
No production code was changed during the audit itself. Renderer observations are
source findings; the runtime probe covered the Net transport, prefetcher, renderer loaders
and Broiler.JS fetch/XHR/beacon bindings. Linux, Android and VM-engine runtime behavior
were not measured.
