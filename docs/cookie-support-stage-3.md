# Cookie support: stage 3 implementation

Completed on 2026-09-24. One profile-owned Broiler.Net `BrowserNetworkSession` now carries
navigation and every HTTP loader in HTML, HtmlBridge and Browser. The script-access gates planned
for stage 4 ship in the same change, because a shared cookie jar must not ship while page script
can bypass its boundaries. Everything except the Browser change is merged and published; see
**State of the work** below.

The [implementation plan](cookie-support-implementation-plan.md) defines the stages, the
[stage 1 audit](cookie-support-stage-1.md) the original inventory and contracts, and the
[stage 2 report](cookie-support-stage-2.md) the engine; its audit table now records which findings
stage 3 resolved. Package versions and hashes are in [the baseline snapshot](cookie-support-baseline.json).

| Repository | Where the work is | Delivered | Local package |
| --- | --- | --- | --- |
| Broiler.Net | `D:\Broiler.Net`, `3365a88` | HTTP transport (`Broiler.Net.Http`), origin and site helpers, the stage 2 audit fixes, suite packaging | `Broiler.Net` 0.1.0-preview.1 |
| Broiler.HTML | `D:\wt\broiler-net-stage3\Broiler.HTML`, `0d01e0f` | Images, link stylesheets and `@font-face` fonts through the host's transport | `Broiler.HTML.*` 0.1.0-preview.5 |
| Broiler.HtmlBridge | `D:\wt\broiler-net-stage3\Broiler.HtmlBridge`, `09991a4` | Every bridge loader through the transport, per-document request contexts, and the stage 4 script gates | `Broiler.HtmlBridge.*` 0.1.0-preview.7 |
| Broiler.Browser | `D:\wt\broiler-net-stage3\Broiler.Browser`, `b779ac9` | `BrowserProfile`, navigation through the session, composition in every head | none (application) |

**Broiler.Net: the transport**

`BrowserNetworkSession` implements `IBrowserRequestTransport` (`SendAsync`, plus a synchronous
`Send` for renderer loaders that uses the handler's synchronous path) and `IDocumentCookieAccess`
(document.cookie). It wraps the profile's `CookieStore` and a pooled `SocketsHttpHandler` with
automatic cookies and redirects disabled. It is thread-safe; one is shared per profile. It owns:

- **Per-hop cookies.** Every hop, redirects included, gets its own cookie context. Every
  `Set-Cookie` field is stored individually with its Latin-1 octets, for any status and before a
  CORS failure is raised.
- **Credentials.** Navigations always include credentials; `omit`, `same-origin` and `include`
  gate both sending and storing. `CookiesEnabled = false` sends, stores and exposes nothing.
- **Redirects.** 301/302/303/307/308 in follow, error and manual modes; at most 20; http(s)
  targets outside Fetch's bad ports only; fragment inheritance; POST-to-GET rewrites that drop the
  body and its content headers; `Authorization` dropped on cross-origin hops; the body buffered once
  so 307/308 resend it. `UrlList` and `FinalUrl` record the chain; a navigation that follows a
  manual redirect passes it back as `RedirectChain`.
- **CORS.** Tainting moves from basic to cors or opaque at the first cross-origin hop and never
  back; `same-origin` mode fails on any cross-origin hop; every cors hop gets the CORS check and,
  when needed, its own uncached preflight; `Origin` becomes `null` once a cross-origin URL
  redirects to another origin (Fetch's tainted origin).
- **Same-site and CHIPS.** The site for cookies comes from the document's inclusive ancestor chain.
  A hop is same-site only when every URL in its redirect chain is, and for a nested navigation also
  with the container. A UI reload replays the status recorded for the history entry. The partition
  key is the top-level site plus the cross-site-ancestor bit. document.cookie derives both the same way.
- **Request headers.** The session owns `Cookie`, `Cookie2`, `Host`, `Origin`, `Content-Length` and
  connection-level fields such as `Transfer-Encoding` and `Connection`, and drops caller values for
  them. It refuses values containing NUL, CR, LF or characters above U+00FF, and drops what a no-cors
  request may not carry. `FetchHeaders` gives bindings Fetch's header tables.
- **Responses.** `TransportResponse.Headers` is privileged and includes `Set-Cookie`; pages get
  `GetScriptVisibleHeaders()` and `ScriptVisibleStatusCode`, filtered by tainting and credentials.
  Network, CORS and redirect failures throw `TransportException`; `Timeout` (100 s) covers every hop
  up to the final response headers.
- **Localhost pinning.** `localhost` and `*.localhost` connect only to ::1 and 127.0.0.1, never
  through DNS or a proxy, which is what makes them secure cookie origins.

`BroilerUserAgent` and `BroilerHttpProtocol` moved from `Broiler.Layout.Net` into
`Broiler.Net.Http`; `Origin` and `SiteMatching` joined `Broiler.Net.Sites`. 19 of the
21 stage 2 audit findings are resolved (see the stage 2 table). The package now has a license
(`Apache-2.0 AND MPL-2.0`), the suite's packaging props, icon, XML documentation, symbols and a CI
workflow for Windows and Linux. It still has zero NuGet dependencies, and the embedded PSL is still
revision `8af9819`.

**Broiler.HTML: renderer loaders (R14–R17)**

A host sets `HtmlContainer.RequestTransport` and `HtmlContainer.DocumentContext`. The container then
sends its subresources as Fetch subresource requests of that document:

| Load | Request and checks |
| --- | --- |
| `<img>`, CSS images | `image`, per the element's `crossorigin` attribute |
| `<link rel="stylesheet">` | `style`, per `crossorigin`. The response must be `text/css`; a quirks-mode document may use a same-origin or CORS response of another type, never one sent with `nosniff`. Relative `url()` references are rebased on the response's final URL by a small CSS tokenizer (comments and strings skipped, quotes kept). |
| `@font-face` | `font`, always CORS with same-origin credentials; relative sources now honour `<base href>` |

Local files load only for a `file:` document, or for a container with neither a document context nor
a transport; a web page never reads a UNC share. Non-2xx responses and transport errors are load
failures, and the budgets are unchanged (5 s images and stylesheets, 10 s fonts). Transport loads skip
the shared `%TEMP%\HtmlRenderer` image cache. An in-memory per-container cache replaces it: it survives
reparses of the same document, is dropped when the transport or document context changes, and re-runs
the caller's acceptance checks and size limit on every hit. `HtmlContainer.ShareSubresourceCacheWith`
lets the browser's per-frame containers of one page share it. A render tree's loads are cancelled when
it is replaced, cleared or disposed. The container never derives a document identity from `BaseUrl`, so
a transport without a document context loads nothing from the network. Hosts without a transport use
process-wide `LegacySubresourceClient` instances that send the Broiler User-Agent but no cookies. The
repository gained its first .NET test project, `tests/Broiler.HTML.Tests`, and a CI step that runs it.

**Broiler.HtmlBridge: bridge loaders and the stage 4 gates (R03–R13)**

Plumbing:

- `Broiler.HtmlBridge.Core` references Broiler.Net. `ExtractAll(html, pageUrl, deliveredPolicy,
  ScriptFetchContext)` fetches classic, async and deferred scripts, module roots and the script
  prefetcher as the page's document; a prefetch records its request when queued and is used only by
  a consumer making the same request.
- `DomBridgeSessionOptions.Network`, `.Cookies` and `.DocumentContextFactory` route link sheets,
  `@import` (in link sheets, `<style>`, render projections and computed style), the speculative
  stylesheet preload (now with `crossorigin`), inserted classic scripts, module imports, frames and
  fetch/XHR/beacon through the transport, each with its owning document's context. The bridge's CSP
  checks go in as `HopPolicy`, so redirects are checked too. Requests end with their document.
- Frames load as nested navigations (`NestedNavigation(container, initiator)`), take their location
  and base from the final URL, and get their own `DocumentRequestContext`: opaque when sandboxed
  without `allow-same-origin` and for `data:`, the creator's for `srcdoc` and `about:blank`.
- The bridge's own stylesheet loader applies HTML's `text/css` rule, including the quirks exception.
- Without a transport, the fallback clients send the Broiler.Net User-Agent and no cookies.

Gates that ship with it:

- **fetch().** Mode, credentials and redirect come from the Request and init (defaults cors,
  same-origin, follow). Methods are normalized and forbidden ones refused; forbidden headers are
  dropped, invalid ones refused, and no-cors keeps only safelisted headers. The response's status,
  headers, `url`, `redirected` and `type` follow the tainting; opaque bodies are empty; network,
  CORS and timeout failures reject with TypeError.
- **XHR and sendBeacon.** `withCredentials` maps to include, otherwise same-origin; `setRequestHeader`
  applies the same header rules; `Set-Cookie` is hidden from both header getters. `sendBeacon` uses
  the native core with credentials include, no-cors unless the body type is not CORS-safelisted.
  Replacing `window.fetch` intercepts neither XHR nor beacons.
- **document.cookie.** Each document and frame reads and writes the profile store through
  `IDocumentCookieAccess`. HttpOnly cookies can be neither read nor overwritten. Cookie-averse
  documents read the empty string. An opaque origin, or a script of another origin reaching the
  document, gets SecurityError. `navigator.cookieEnabled` reflects the profile. Without injected
  access, the bridge uses a store private to it.
- **Frame identity.** A frame's microtasks, promise reactions, `await`s, timers, module roots and
  classic-script `import()` run and request as the frame, and are dropped once the frame has
  navigated away. Frame module roots start from a job of the frame's window; nothing waits for them.
- **Cross-origin access.** Origins come from request contexts, never from anything page script can
  assign. Cross-origin frames are withheld from `contentDocument`, `contentWindow`, `window.frames` and
  `<object>`; `MessageEvent.source` from one can only be posted to; message origins come from the
  sender's request context; a cross-origin sheet without CORS applies, but its `cssRules`,
  `insertRule` and `deleteRule` throw SecurityError.
- **Local files.** `LocalFileAccess` reads files for scripts, modules, stylesheets and workers only
  for a `file:` document or a tool with no document. Frames and objects refuse `file:` unless their
  embedder is a `file:` document.
- **Navigation initiators.** `NavigationRequest.Initiator` names the document behind script
  navigations, `form.submit()` and meta refresh (`MetaRefreshDiscovery.Find(..., initiator)`).

Behaviour other hosts will notice: a fetch network failure now rejects with TypeError (XHR fires
`error`, not `load` with status 0). Invalid enum values, forbidden methods and GET or HEAD with a body
are TypeErrors. Non-standard methods keep their case. `sendBeacon` throws for invalid or non-HTTP URLs.
A frame's module scripts are checked against the frame's CSP, not the page's.

**Broiler.Browser: profile and navigation (R01–R02)**

- `BrowserProfile` (internal) owns a `CookieStore` and one `BrowserNetworkSession` (Broiler.Net
  User-Agent, 5-minute pooled connection lifetime, 15 s connect timeout). `CreateDefault()` keeps
  favorites in `%APPDATA%`; `CreateEphemeral()` keeps everything in memory. Windows (`Program.cs`,
  `BrowserWindow`), Linux (`LinuxBrowserRunner`) and Android (one lazy profile per process) each create
  one. A `BrowserApp` built without a profile gets a private ephemeral one. The static
  `BrowserApp.PageHttpClient` jar (H1) is gone.
- `PageLoader(IBrowserRequestTransport)` sends every page as a top-level navigation and returns
  `PageLoadResult`: final URL, HTML, status, final method, headers without `Set-Cookie`, the CSP header,
  the redirect chain and the SameSite status. Error statuses throw only after their cookies are stored.
  A 100 s budget runs from send to the last body byte and raises `TimeoutException`; a user stop stays
  a cancellation. `file:` pages load locally. `PageLoader(HttpClient)` remains for other hosts.
- `PageRequest.Initiator`, `.NavigationType` (`PageNavigationType`) and `.RecordedSameSite` feed the
  request context. `ForLoadedDocument` builds history entries: a POST redirected by 303 is stored as a
  GET, a POST answered in place keeps its body, and each entry records its SameSite status.
- `RenderingPipeline` takes the transport and returns `LoadedPage`. The page's document context is
  built from the final URL. `ExtractAll` receives the fetch context. The final container, every
  intermediate painted frame and the re-parse after a script step get the transport and context and
  share one subresource cache. The bridge gets the network, the cookie access, and a factory that
  returns the pipeline's context for the page.
- Links and forms carry the on-screen document as initiator. A page's own navigations never open a
  `file:` URL unless the page is itself a `file:` document; UNC paths only from the user. Loop guards
  count every redirect-chain URL, and a page may return once to a URL its load was redirected away
  from (cookie challenges). Favorites compare canonical URLs. A session timeout shows the error page.
- References: `Broiler.HTML.Graphics`/`Image` 0.1.0-preview.6, `Broiler.HtmlBridge.Dom`/`Scripting`
  (and the VM-only `Scripting.Vm`) 0.1.0-preview.7, `Broiler.Net` 0.1.0-preview.1.

**Request inventory after stage 3**

This replaces the classification in the stage 1 table. **integrated**: sent through the profile
session with the request's own context; **still limited**: integrated with a named gap; **local**: no
HTTP request; **unsupported**: the browser capability is missing.

| ID | Surface | After stage 3 | Route and remaining gap |
| --- | --- | --- | --- |
| R01 | Address bar, favorites, links, back/forward/reload, GET forms | integrated | `PageLoader` top-level navigation; initiator for links and forms, none for browser UI; UI reload replays the recorded SameSite status; history and address bar show the final URL. |
| R02 | POST/multipart forms, meta refresh, script navigation | integrated | Initiator from `NavigationRequest.Initiator`, falling back to the current page; the session applies method rewrites. A frame's script still cannot navigate the top window through `parent.location` (pre-existing). |
| R03 | External classic, async, deferred scripts and module roots | integrated | `ExtractAll` with `ScriptFetchContext`; `crossorigin` via `CorsSettings.Parse`; modules same-origin credentials, include for `use-credentials`. The header CSP is not applied (see deviations). |
| R04 | Static imports and `import()` | integrated for module graphs; still limited for `import()` from classic scripts | Imports inherit the importer's document and credentials. The module map is shared, so a module two documents import is fetched once, for whichever asked first. The top document's classic `import()` is unchanged (resolved against the base of the last root the engine ran). A frame's classic `import()` is the frame's request but always uses same-origin credentials. |
| R05 | Inserted classic scripts | integrated; inserted module scripts unsupported | `ScriptInsertionRunner` captures the request, `crossorigin` and CSP when the script is inserted. Inserted module scripts are still marked started without a fetch. |
| R06 | Script speculation and preload hints | integrated for the prefetcher; preload hints unsupported | The prefetcher records each request when queued. Scanner script, `modulepreload` and `preload as=script` candidates are still not requested. |
| R07 | Bridge link sheets, `@import`, CSSOM loads | integrated | Owning document's context; `@import` attributed to the importing document; `text/css` rule; cross-origin non-CORS sheets are unreadable through CSSOM. |
| R08 | Bridge speculative stylesheet scan | integrated | Request built with the link's `crossorigin`, CSP and quirks mode; reused only when the consumer's request matches. Unconsumed `preload as=style` hints are still fetched, under the same rules. |
| R09 | fetch(), including Request input | still limited | Modes, credentials, headers, CORS, final URL and tainting as above. Request and response bodies are strings (Blob bytes only for beacons); a Request's `headers` still lists forbidden headers the page set, though they are dropped before sending. |
| R10 | XMLHttpRequest | integrated | `withCredentials`, header rules, `responseURL`, filtered response headers. |
| R11 | navigator.sendBeacon | still limited | Credentialed, mode by body type, Blob bytes. Still sent synchronously; `RequestContext` has no keepalive flag, so a beacon cannot outlive its document. |
| R12 | iframe/object/embed documents | integrated | Nested navigation with container and initiator; final URL; child context with sandbox and origin; `file:` refused for web embedders. A `data:` URL longer than `System.Uri` accepts becomes `data:,`. |
| R13 | Scripts, fetch and cookies inside frames | still limited | Frame document for scripts, modules, fetch, document.cookie, jobs and timers. All documents share one realm, and attribution relies on Broiler.JS internals (see limitations). |
| R14 | Renderer `<img>` and CSS images | integrated | HTML image loader through the transport; `crossorigin`; per-container cache instead of the shared temp cache. |
| R15 | Layout image prefetch | integrated | Same image loader and cache as R14. |
| R16 | Renderer stylesheets | integrated | `text/css` rule, `nosniff`, final-URL rebasing. `@import` still comes only from the bridge (R07). |
| R17 | CSS `@font-face` | still limited | CORS with same-origin credentials, `<base href>` honoured. Remote fonts still register in the adapter's font registry, which stage 1 found process-global; there is no per-container or per-profile font scope. |
| R18 | `file:`, inline/`srcdoc`, decoded `data:` | local | Cookie-averse contexts. Web documents no longer read `file:` or UNC resources in HTML, the bridge or navigation. |
| R19 | Dedicated Worker / `importScripts` | local | Worker scripts are read from disk only for `file:` documents; a network worker loader is still missing. |
| R20 | VM document-free module resolution | local | Unchanged. |
| R21 | FontFace.load / document.fonts | unsupported | Stub, unchanged. |
| R22 | audio/video/MediaSource | unsupported | Unchanged. |
| R23 | WebSocket / EventSource | unsupported | Unchanged. |
| R24 | ServiceWorker / SharedWorker / module workers | unsupported | Unchanged. |
| R25 | Cookie Store / Storage Access | unsupported | Stage 7. |

The six hidden jars are gone. H1 was removed. H2–H6 survive only as fallback clients for hosts that
pass no transport, with `UseCookies = false`; the browser always passes one.

**The contract as built**

The stage 1 contract names were design targets. Stage 3 built them as follows:

| Stage 1 target | As built |
| --- | --- |
| `BrowserNetworkSession` / `IBrowserRequestTransport` | Same names. `SendAsync` plus a synchronous `Send`; options in `BrowserNetworkSessionOptions`. The result is `TransportResponse`, which wraps the `HttpResponseMessage` (`Message`) instead of a separate body type: `StatusCode`, `FinalUrl`, `UrlList`, `Redirected`, `Tainting`, `Credentials`, `SameSite`, privileged `Headers`, `ScriptVisibleStatusCode`, `GetScriptVisibleHeaders()`. Failures are `TransportException` with a `TransportError`. |
| `DocumentRequestContext` | Same name. It holds only `DocumentUrl`, `Origin` and `Parent` (`TopLevel`, `IsTopLevel`, `IsCookieAverse`) and is built with `CreateTopLevel` and `CreateChild`. The session derives the site for cookies and the partition key per request instead of storing them. It has no sandbox flag and no equality. |
| `RequestContext` | Same name, a record: `Destination`, `Client` (the requesting or initiating document), `Container`, `Mode`, `Credentials`, `Redirect`, `IsUserReload`, `ReloadWasSameSite`, `RedirectChain`, `HopPolicy`; factories `TopLevelNavigation`, `NestedNavigation`, `Subresource`, `Fetch`. The method comes from the `HttpRequestMessage`, the navigation kind is Browser's `PageNavigationType`, and the lifecycle generation is the caller's `CancellationToken`. |
| `CookieAccessContext` | `CookieRequestContext` and `CookieDocumentContext` (stage 2). |
| `ICookieService` | Stage 2, unchanged. Script bindings get `IDocumentCookieAccess` (`CookiesEnabled`, `TryGetCookie`, `TrySetCookie` over a `DocumentRequestContext`), implemented by `DocumentCookieAccess` and the session, rather than the stage 4 plan's `GetDocumentCookies`/`SetDocumentCookie` on the store. `false` means SecurityError. |
| `ISiteResolver` / `CookiePolicy` | Stage 2; `Origin` (tuple and opaque origins, `blob:`) and `SiteMatching` are new. |
| `ICookiePersistence` | Not built (stage 6). |
| Clock / change notifications | Observer failures are one `CookieObserverException` after the mutation commits; reads never publish; `Snapshot(out long revision)`. The session passes observer failures to `CookieObserverError` and never fails a request because of one. |
| (new) | `FetchHeaders`, `CorsSettings`/`CorsSetting`, `RequestDestination`, `RequestMode`, `CredentialsMode`, `RedirectMode`, `ResponseTainting`, and `BroilerUserAgent`/`BroilerHttpProtocol` in `Broiler.Net.Http`. |

Lower-level API additions: HTML `HtmlContainer.RequestTransport`, `.DocumentContext` and
`.ShareSubresourceCacheWith`. HtmlBridge `ScriptFetchContext`, the `ExtractAll` overload,
`ModuleRoot.CrossOrigin`, `PreloadCandidate.CrossOrigin`, `DomBridgeSessionOptions.Network`/`Cookies`/
`DocumentContextFactory`, `NavigationRequest.Initiator` and the `MetaRefreshDiscovery.Find` overload.
Browser `PageLoader(IBrowserRequestTransport)`, `IPageLoader.LoadAsync`, `PageLoadResult`, `LoadedPage`,
`PageNavigationType`, the `PageRequest` additions, the third `RenderingPipeline` constructor parameter
and `FavoritesManager(string?)`. All are additive; old overloads and constructors remain. HtmlBridge
Core's public signatures now expose Broiler.Net types.

**Deliberate deviations**

- **Sandboxed frames are cross-site.** An opaque-origin document below the top makes the site for
  cookies opaque. This deviates from 6265bis-22 §5.2.1 step 4.1 and follows Chromium and the pinned
  WPT `cookies/samesite/sandbox-iframe-*` tests. A sandboxed top-level document counts with its URL's origin.
- **`__Host-` needs an explicit `Path=/`.** Unchanged from stage 2: the last Path attribute must be
  literally `/`, stricter than 6265bis-22 §5.7 step 21.3 (WPT `__host.explicit-path`, layered-cookies).
- **Redirect status without `Location`.** The session applies the redirect mode before it reads
  `Location`, in Fetch's order: error mode fails and manual mode returns an opaque redirect for any
  301/302/303/307/308, and only follow mode returns a redirect without `Location` as the final
  response. The stage 3 design had applied the modes only to redirects that carried a `Location`.
- **`Referer` and `Sec-*` stop at the first origin.** Fetch recomputes them per hop; the session
  cannot, so host-supplied values are dropped once a redirect leaves the first URL's origin. The
  session never sets `Referer` itself (no referrer policy).
- **No HTTP cache and no preflight cache.** Every request reaches the network, and every CORS hop
  that needs a preflight sends its own. HTML's in-memory per-container cache is the only reuse.
- **CSP only through `HopPolicy`.** The bridge applies its own CSP checks (policies the markup
  declares, frames' inherited policies) as `HopPolicy`, so redirects are covered. The page's
  `Content-Security-Policy` header is carried in `PageLoadResult.ContentSecurityPolicy` but enforced
  nowhere: HtmlBridge's source matching lacks CSP3 host sources (bare hosts, `*.` wildcards, ports,
  paths), and enforcing the header dropped scripts that real headers allow (first review). It will be
  enforced, for the script engine too, once host sources match. Mixed-content blocking is not
  implemented either.
- **Localhost.** `localhost` and `*.localhost` are secure cookie origins only because the session
  pins them to loopback. As decided in stage 1, `about:blank` and `srcdoc` documents use their
  creator's URL for cookies.

**Known limitations**

These were reported by the implementers and the fix rounds, or found while writing this report.

Broiler.Net:

- Request paths still come from `Uri.AbsolutePath`, so a `Path=/%7Euser` cookie is never sent (open audit row).
- There is no third-party cookie policy hook yet (stage 5) and no persistence (stage 6): every
  cookie ends with the process, as before.
- Consumer-reported API gaps, none blocking: no sandbox flag on `DocumentRequestContext`, so the
  bridge tracks sandboxed documents itself; `CreateChild` needs a `System.Uri`; no keepalive flag;
  no equality on `DocumentRequestContext`, so Browser's factory matches on `DocumentUrl`;
  `TransportResponse` does not expose the final hop's method, which Browser reads from
  `Message.RequestMessage`; the session's timeout surfaces as `TaskCanceledException`, like a user
  cancel, so Browser checks its own navigation token.
- The CI workflow has never run, because the repository has no remote; the tests ran on Windows only.

Broiler.HTML:

- Remote fonts still enter the adapter's font registry; there is no per-container or per-profile font scope.
- The cookie-less path for hosts without a transport keeps the shared temp image cache, has no
  `text/css` check and follows redirects through `HttpClient`, as before.
- `eng/pack.ps1` fails on Windows PowerShell 5.1, so the local packages were built with `dotnet pack` per project.

Broiler.HtmlBridge:

- All documents share one realm. A page node that leaks to another origin's script still exposes
  the page's DOM; only its `document.cookie` is gated.
- An unsandboxed `data:` frame is still judged by its creator's origin, so the page can read it;
  HTML makes it cross-origin. It was kept because 16 existing tests depend on it, and it awaits a
  decision. The HtmlBridge README and `docs/html-control.md` describe this exception as it is.
- Frame job attribution depends on Broiler.JS internals. The engine has no public way to add a host
  job to a context's microtask queue (worked around with a promise reaction under a non-pump
  context), or to evaluate a module root on the calling thread (the protected `LoadModuleAsync` is
  used). A job posted from another thread, or while no script runs, waits for the host's next
  checkpoint. The document-free `Execute(scripts)` path keeps its old `queueMicrotask` routing.
- A frame module's static imports are fetched synchronously within the script fetch budget.
- Without a transport, `fetch()` has no CORS or redirect-mode enforcement: the fallback follows
  redirects and exposes every response as basic, without cookies. The first review's split finding on
  this was classified as not a defect under the stage 3 spec and deferred.
- A host that passes `Network` without `Cookies` gets a document.cookie store private to the bridge.
- No deterministic test shows that disposing the bridge cancels in-flight requests; the code does.

Broiler.Browser:

- The header CSP is not enforced (see deviations).
- `PageLoader(HttpClient)` keeps whatever cookie behaviour its client has; the browser never uses it.
- The exit test's cross-site check uses the second site's image and script, not a cross-site frame;
  HtmlBridge's tests cover cross-site frames.
- Not touched: CI, the stale `.slnx` files and the test project's `UI.Panel.Standard` preview.4 pin.
  The Linux solution was not built (the Linux head project was). Android built once during
  integration (0 errors, 35 pre-existing NU1603 warnings) but not after the fix rounds.

**Verification**

Final rebuild after the second fix round, in dependency order, with every build at 0 warnings:

| Repository | Configuration | Passed | Skipped | Failed | Before stage 3 |
| --- | --- | --- | --- | --- | --- |
| Broiler.Net | Release | 657 | 0 | 0 | 233 (stage 2) |
| Broiler.HTML | Release | 41 | 0 | 0 | no .NET tests |
| Broiler.HtmlBridge | Release | 1390 | 22 | 0 | 1290 (2026-09-23) |
| Broiler.HtmlBridge | Release-VM | 1445 | 22 | 0 | 1345 (2026-09-23) |
| Browser Core.Tests | Release | 205 | 0 | 0 | |
| Browser Core.Tests | Release-VM | 205 | 0 | 0 | |

`Broiler.Windows.Browser.slnx` builds in Release and Release-VM, and the Linux head project in
Release. HtmlBridge's engine-neutrality scripts pass with every count at budget. After the repack, no
source file was newer than its package, and the new code was spot-checked inside the packed DLLs.
Every dotnet command ran with `RestoreConfigFile` and `NUGET_PACKAGES` pointing at the task-local
feeds and package cache.

The exit test, `ProfileCookieExitTests`, runs a full `BrowserApp` on an ephemeral profile against
loopback servers. The navigation is redirected (`/enter` → `/site/page`), and the page's response
sets an HttpOnly `sid` and a plain `visible` cookie. Every later request to the page's site carries
exactly both: the classic script and module root (extractor), the static import (bridge module
loader), the link stylesheet (renderer and bridge), the `@font-face` font and the image (renderer) and
the iframe (bridge nested navigation). Each is requested at the path the final URL resolves to. The
second site's image and script (on `localhost`) carry its `SameSite=None` cookie but not its Lax one,
and the page's script reads only `visible`. This meets the plan's stage 3 exit: a navigation cookie
reaches protected scripts, stylesheets, images, fonts and nested documents through their real
loaders, and no loader keeps its own jar. Each of these mutations made it fail: containers without
a transport, `ExtractAll` without a fetch context, the bridge without network, the bridge without
cookie access, and reporting the requested URL instead of the final one. It runs over HTTP on
loopback, which counts as secure only because of localhost pinning; it is not an HTTPS or WPT result.
Fetch, XHR, beacon, document.cookie and frame cases are covered by HtmlBridge's loopback tests
(`ProfileNetworkTests`, `ScriptNetworkGateTests`, `FrameIdentityTests`, `CrossOriginAccessTests`,
`StyleSheetResponseTypeTests`), navigation cases by `PageLoaderNavigationTests` and
`NavigationRobustnessTests`.

Reviews:

- **Broiler.Net transport:** reviewed and checked with runtime probes before the package was built.
  That round is not itemized in the integration record, so this report gives no finding count for it.
- **First review** of the integration, in five lenses (isolation bypasses, script gates, browser
  navigation, HTML loaders, tests and packaging): 20 findings, each checked by two independent
  verifiers. 16 were upheld by both (one a duplicate), 3 split, and 1 rejected by both (a claimed CI
  failure of HTML's tests on Ubuntu). Of the resulting 18, 17 were fixed with regression tests (the
  loopback port retry only with a unit test of the bind step), and 1, the no-transport fetch
  fallback, was classified as not a defect under the stage 3 spec.
- **Gap round:** two gaps the fixer found were closed with 16 tests: the bridge's own stylesheet
  loader had no `text/css` check, and a frame's classic-script `import()` was requested as the page.
  Disabling the type check, the frame attribution or the quirks detection made the corresponding
  tests fail. Line endings were restored, so `git diff --stat`
  equals `git diff --ignore-cr-at-eol --stat` in all three worktrees.
- **Second review:** the fix report lists 19 items (13 HtmlBridge, 3 HTML, 3 Browser; its summary
  counts 18 findings), including the engine-neutrality gate the gap round left red. All are fixed.
  Each has a regression test except the neutrality gate, which its check script covers, and a
  doc-comment fix. The three items with split verdicts proved real on re-check. For HtmlBridge fixes
  6–12, removing each fix made its test fail.

Not done in this stage: no WPT run, no runtime validation on Linux or Android, no CI run in any repository.

**State of the work**

Merged and published on 2026-09-24, except the Browser change:

| Package | On nuget.org | Built from |
| --- | --- | --- |
| `Broiler.Net` | 0.1.0-preview.1 | Broiler.Net `main` `5ab213d`: PR #1 (stages 2 and 3) and PR #2 (the publish workflow) |
| `Broiler.HTML.*` | 0.1.0-preview.6 | Broiler.HTML `main` `55f64b3` (PR #233) |
| `Broiler.HtmlBridge.*` | 0.1.0-preview.7 | Broiler.HtmlBridge `main` `edd3b4d` (PR #5) |

- `Broiler.HTML` 0.1.0-preview.5 on nuget.org was published from the pre-integration `main`
  (`9b65488`) and has none of this work; Browser therefore pins 0.1.0-preview.6.
- HtmlBridge's Linux CI found two root-relative URL checks (`/frame.html` as a frame `src`,
  `import "/lib.js"`) that `System.Uri` turned into `file:` paths on Unix; `5b7b622` fixed both before
  the merge, so the published 0.1.0-preview.7 includes it and the local build of that number did not.
- **Browser:** branch `claude/broiler-net-stage-3`, PR #147: `580f7a4` carries the user's previously
  uncommitted nuget.org migration and `HtmlPostProcessor` files, `b779ac9` is stage 3, `d6efbbc` adds
  these documents, and a follow-up moves the HTML pins to 0.1.0-preview.6. Validated against the
  published packages with an empty package cache and nuget.org as the only source: the Windows
  solution builds in Release and Release-VM, and Core.Tests passes 205 of 205 in each, the exit test
  included. Its CI was red for an unrelated reason, three solutions and `ci.yml` still referencing the
  removed submodule projects (MSB3202), until PR #148 repaired CI on `main`; that is merged into the
  branch.
- The main Browser checkout still holds the carried-over changes and these documents uncommitted;
  drop those copies before pulling `main` after the merge.
- The local validation feeds (`D:\local-packages\broiler-net-stage3`) are obsolete. Their HTML
  0.1.0-preview.5 and HtmlBridge 0.1.0-preview.7 builds differ from the published packages of the same
  numbers, so nothing should restore from them.

**Next**

Stage 4 is largely done. Shipped here: document.cookie for every document and frame with HttpOnly
protection, cookie-averse documents and SecurityError for opaque origins; Fetch `omit`,
`same-origin` and `include` for sending and storing; XHR `withCredentials`; beacon credentials;
script and module `crossorigin` credentials; forbidden request headers; `Set-Cookie` filtering;
credentialed CORS and preflight; the Request, Headers, XHR and frame bypass checks. The stage 4 exit
holds at component level: `ScriptNetworkGateTests` shows a cookie set by script reaching the next
request, a server cookie visible to script, HttpOnly cookies protected, and cross-site frame and
cross-origin negatives.

Still open for stage 4:

- engine parity: Release-VM runs the same suite, but the VM build delegates DOM work to
  `ScriptEngine`, so there is no independent VM provider test yet;
- the two Broiler.JS engine gaps behind frame attribution;
- the pinned WPT `cookies`, `fetch`, `xhr` and document.cookie suites;
- the fetch/XHR body, Request `headers` and beacon keepalive gaps;
- the no-transport fetch fallback;
- the `data:` frame decision and the shared-realm exposure;
- header CSP with CSP3 host sources, mixed content and referrer policy.

Stage 5: the transport already derives same-site status from the ancestor and redirect chains,
applies the reload rule and builds CHIPS partition keys. Still to do: the allow, block-all and
block-unpartitioned-third-party settings with site exceptions (which need a `CookiePolicy` hook),
`navigator.cookieEnabled` against those settings, browser-level A→B→A frame tests, and the pinned WPT
`samesite` and `partitioned-cookies` runs.

Later stages: `ICookiePersistence` with SQLite, profile directories, clearing and restart behaviour
(6); Cookie Store and storage access prerequisites (7); the HTTPS multi-origin fixture,
Windows/Linux/Android matrix and dependency-ordered release (8).

