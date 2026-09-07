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

Pools are process-scoped and never disposed. `BrowserApp.PageHttpClient` is the model.

## Pools in the browser process

| Pool | Timeout | Notes |
| --- | --- | --- |
| `BrowserApp.PageHttpClient` | 100 s default | Navigation. `PooledConnectionLifetime` 5 min, `ConnectTimeout` 15 s |
| `ResourceLoader.SharedClient` | 5 s | Stylesheets, `fetch`, XHR, sub-documents |
| `ImageDownloader.SharedHttpClient` | 5 s | `<img>`, cancelled per render-tree teardown |
| `StylesheetLoadHandler.SharedHttpClient` | 5 s | `<link>` on the render path |
| `ScriptExtractionService.SharedHttpClient` | 30 s | External `<script>` |
| `HtmlContainerInt.SharedFontHttpClient` | 10 s | `@font-face` |

None of them sets `PooledConnectionIdleTimeout`, so all six inherit the .NET default of
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

The CLI never saw any of this. It is a one-shot capture that exits before the pool scavenges
anything; a browser window stays open long enough to watch it happen.
