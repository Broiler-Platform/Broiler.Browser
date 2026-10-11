# HTTP/2 support

Broiler prefers HTTP/2 and falls back to HTTP/1.1. This applies to the profile network
used by Windows, Linux, Android and the CLI for documents and subresources. HTTP/3
is not enabled: `Broiler.Net.Http.BroilerHttpProtocol` requests version 2.0 with
`RequestVersionOrLower`. HTTPS servers negotiate HTTP/2 through ALPN; ordinary
cleartext HTTP and servers without HTTP/2 continue using HTTP/1.1.

`BrowserNetworkSession` still exposes both `SendAsync` and `Send`. Both now use the
handler's asynchronous network operation, because `SocketsHttpHandler.Send`
rejects HTTP/2. The synchronous API waits for that operation on a worker thread,
away from the calling UI/JavaScript synchronization context. Cookie, redirect,
CORS, timeout and cancellation handling remain in the shared transport.

`PageLoader` records the final response's HTTP version in `PageLoadResult`.
`RenderingPipeline` passes it through `DocumentFetchTiming` to HtmlBridge's
navigation entry. `nextHopProtocol` reports `h2` or `http/1.1` from the response,
and an empty string for inline/local documents or an unobserved fetch. Resource
timing and the CLI network recorder already use the response version.

## Dependency packages

HTTP/2 transport is published in `Broiler.Net`. The asynchronous JavaScript binding
follow-up currently uses a distinct local HtmlBridge package:

| Package | Selected version | Source |
| --- | --- | --- |
| Broiler.Net | 0.1.0-preview.3 | nuget.org |
| Broiler.HtmlBridge.* | 0.1.0-preview.28-http2.2 | local development feed |

The local packages are already built on this machine. For a fresh checkout, build
them from the modified sibling `Broiler.HtmlBridge` checkout before restoring Browser:

```powershell
.\eng\build-http2-packages.ps1
dotnet build src/Broiler.Browser.Windows/Broiler.Browser.Windows.csproj -c Release
```

The helper builds only dependencies selected with a distinct `-http2.N` suffix in
`Directory.Packages.props`; published Net stays on nuget.org. `-BridgePath` overrides
the sibling path. Existing package versions cannot be overwritten: increment the
local suffix after source changes. `NuGet.config` maps HtmlBridge's family to
`artifacts/http2/packages`, and other packages to nuget.org. These HtmlBridge
packages have not been published. When the change is released, select the published
family version and remove the local family mapping.

See [the async implementation report](http2-async-implementation.md) for behavior,
measurements and remaining streaming work.

### A cached local package can hide a correct published package

After switching to published preview.27, compilation initially failed because
`DocumentFetchTiming.RecordResponseVersion` appeared to be missing. The official
nuget.org package contained it. Four global-cache HtmlBridge preview.27 packages
had instead come from an older local build at commit `784cd50`; the published
package identified commit `baed39c`. NuGet reused the cached package ID/version
despite the different source and contents. Repeating the version update did not
replace those assemblies.

The repair backed up only those four cached version directories, then restored
from nuget.org. Obsolete local-source mappings were removed too. The Windows
Release build then passed with zero warnings and errors. Never pack local changes
under the same version as a published package; a unique development suffix avoids
this collision. Clearing every NuGet cache is unnecessary for this problem.

## Validation on 2026-10-10

Validated on Windows 11, .NET 10.0.12:

- Broiler.Net: 1,911 tests passed, including real TLS negotiation against Kestrel
  with HTTP/2 enabled or HTTP/1.1 only; both synchronous and asynchronous calls,
  POST replay through a 307 redirect, cookies, cancellation and context isolation.
- HtmlBridge: 58 resource/performance tests and 4 new navigation protocol tests passed.
- Browser.Core: all 324 tests passed, including protocol propagation to JavaScript.
- CLI: 18 network recorder, window network and HTTP error diagnostics tests passed.
- Windows and CLI Release builds succeeded with no warnings or errors.
- Broiler.Net assurance check passed.
- Live browser CLI navigation to `https://example.com` reported `h2` through
  `performance.getEntriesByType('navigation')[0].nextHopProtocol`.
- A live request through the packaged Broiler network session to Cloudflare's
  `/cdn-cgi/trace` returned HTTP 200, response version 2.0 and `http=http/2`.

Linux and Android runtime behavior has not been exercised on devices in this change.
