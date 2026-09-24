# Connection-pool aborts

A recurring first-chance exception with no Broiler frames in it:

```
System.IO.IOException
  Message=Unable to read data from the transport connection: The I/O operation has been
          aborted because of either a thread exit or an application request.
  Source=System.Net.Sockets
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError,
      CancellationToken)

Inner: System.Net.Sockets.SocketException (SocketError.OperationAborted, WSA 995)
```

On a German-locale machine the inner message reads *"Der E/A-Vorgang wurde wegen eines
Threadendes oder einer Anwendungsanforderung abgebrochen."* — the same WSA 995.

The wording is misleading in both languages. No thread exited. On Windows, closing a socket
handle cancels any overlapped I/O still pending on it, and the cancelled read reports 995.
The "application request" is the close.

## Why the stack has one frame

`SocketsHttpHandler` arms a **zero-byte read** on an HTTP/1.1 connection when it returns to
the pool, so it can notice the server hanging up before the connection is handed to the next
request. Nothing awaits that read. When the pool later disposes the connection, the read
completes as aborted, and the exception is caught and discarded inside the handler.

That is why the stack is a single frame with no async continuation, and why the exception
never reaches Broiler code. **It is visible only because debuggers surface first-chance
exceptions.** A failing *request*, by contrast, is observed on whichever frame awaited it —
`PageLoader.FetchAsync`, `ResourceLoader.LoadTextDirect`, and so on — and the debugger shows
that async stack.

A single frame means the pool. A stack means a request.

## The rule

An `HttpClient` **is** a connection pool. Two consequences, both learned the hard way here:

- **Never create one per unit of work.** A per-navigation, per-page or per-sub-resource
  client reconnects to a host the previous one had a live keep-alive connection to, and
  disposing it closes that connection under its armed read-ahead.
- **Never dispose one you do not own.** Disposing a shared client tears down the pool for
  everybody. `PageLoader` takes `ownsHttpClient: false` by default for exactly this reason,
  and `PageLoaderLifetimeTests` guards it.

Pools live as long as what owns them and outlive every unit of work. The profile's
`BrowserNetworkSession` (`BrowserProfile.Network`) is the model: created once by the
composition root, disposed after the last window that uses it.

## Pools in the browser process

The browser has **one** pool for page traffic: the profile's `BrowserNetworkSession`
(Broiler.Net). Navigation, the scripts the extractor fetches, the DOM bridge's loaders
(stylesheets, `fetch`, XHR, `sendBeacon`, sub-documents, module imports, inserted scripts) and
the renderer's images, stylesheets and fonts all send through it, each with its own budget as a
linked cancellation:

| Traffic | Budget | Notes |
| --- | --- | --- |
| Navigation (`PageLoader`) | 100 s from send to the last byte (the loader's budget; the session `Timeout` ends at the headers) | `PooledConnectionLifetime` 5 min, `ConnectTimeout` 15 s |
| Bridge resources | 5 s | Stylesheets, `fetch`, XHR, sub-documents |
| Images | 5 s | Cancelled per render-tree teardown |
| `<link>` on the render path | 5 s | |
| External `<script>` | 30 s | |
| `@font-face` | 10 s | |

The components' process-wide clients (`ResourceLoader.SharedClient`,
`ImageDownloader.SharedHttpClient`, `StylesheetLoadHandler.SharedHttpClient`,
`ScriptExtractionService.SharedHttpClient`, `HtmlContainerInt.SharedFontHttpClient`) remain only
as the fallback for a host that supplies no transport, and keep no cookies.

None of these sets `PooledConnectionIdleTimeout`, so all inherit the .NET default of
**one minute**.

## Triggers, and which are defects

Timing separates them without instrumentation.

| Cadence | Cause | Defect? |
| --- | --- | --- |
| Exactly every minute while idle | `PooledConnectionIdleTimeout` — the pool evicting an idle connection | No |
| ~5 min after a navigation | `PooledConnectionLifetime` on the page client | No, deliberate |
| ~5 s into a slow sub-resource | The short `HttpClient.Timeout`s cancelling a request | No |
| Immediately after resuming from a breakpoint | Same, tripped by the pause itself | No |
| On render-tree rebuild with images in flight | `ImageDownloader.Dispose` cancelling them | No |
| Once per `@font-face`, or once per navigation | A pool built and disposed per unit of work | **Yes** |

Only the last row is worth fixing. The others are the pool doing its job; the abort is how
Windows reports a socket closed under a pending read, not a failure.

**The once-a-minute case in particular cannot be turned off.** There is no public switch for
the read-ahead. Raising `PooledConnectionIdleTimeout` only moves it; `Timeout.InfiniteTimeSpan`
trades a cosmetic exception for connections pinned open forever. Filter it in the debugger
instead — in Visual Studio, Debug → Windows → Exception Settings.

## Diagnosing an occurrence

To confirm which pool: temporarily set `PooledConnectionIdleTimeout` on the pool you suspect.
If the cadence follows the new value, that is the one.

To see the frames: turn **off** Just My Code before breaking on `SocketException`. The frames
above the throw name the connection pool rather than any Broiler type.

To attribute it to a host, with no edits to the six clients, attach an `EventListener` at
startup:

```csharp
sealed class HttpTrace : System.Diagnostics.Tracing.EventListener
{
    protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource src)
    {
        if (src.Name is "System.Net.Http" or "System.Net.Sockets")
            EnableEvents(src, System.Diagnostics.Tracing.EventLevel.Verbose,
                System.Diagnostics.Tracing.EventKeywords.All);
    }

    protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
    {
        if (e.EventName is not ("ConnectionEstablished" or "ConnectionClosed" or "RequestStart"
            or "RequestStop" or "RequestFailed" or "ConnectStart" or "ConnectStop"
            or "ConnectFailed"))
            return;
        var args = string.Join(", ", e.PayloadNames?.Zip(e.Payload!, (n, v) => $"{n}={v}") ?? []);
        Console.Error.WriteLine(
            $"[{DateTime.Now:HH:mm:ss.fff}] [T{Environment.CurrentManagedThreadId}] {e.EventName} {args}");
    }
}
```

The last `ConnectionClosed` before the exception names the scheme, host and port. A
`RequestStop` immediately before it means a pool teardown; no preceding request means an idle
eviction.

`Verbose` on `System.Net.Http` is noisy. It is a diagnostic, not something to leave in.

## History

Two rounds of the same diagnosis, both reached from this exception with no Broiler frames.

1. **Navigation.** The rendering pipeline built a `PageLoader` per navigation and its `using`
   disposed the client at the end of the load, so every page tore down the pool the next page
   would have reused. Fixed by making the pool process-scoped
   (`BrowserApp.CreatePageHttpClient`) and by giving `PageLoader` the `ownsHttpClient` flag,
   defaulting to `false`. Guarded by `PageLoaderLifetimeTests`.

2. **Fonts and images.** `HtmlContainerInt.TryLoadRemoteFont` built and disposed a client per
   `@font-face`, which round one had missed. Fixed alongside two unrelated races in
   `ImageDownloader.Dispose`: the `CancellationTokenSource` was disposed while an in-flight
   `HttpClient.Send` still held its token, and the callback dictionary was cleared outside the
   lock its other accessors take.

3. **One profile network.** The process-scoped `BrowserApp.PageHttpClient` was replaced by the
   profile's `BrowserNetworkSession`, which every loader now shares. `PageLoader` never owns a
   transport it is given; the `ownsHttpClient` flag still governs a loader built over a plain
   `HttpClient`, and `PageLoaderLifetimeTests` still guards it.

The CLI never saw any of this. It is a one-shot capture that exits before the pool scavenges
anything; a browser window stays open long enough to watch it happen.
