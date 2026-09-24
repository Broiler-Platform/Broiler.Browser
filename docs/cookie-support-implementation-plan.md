# Full cookie support implementation plan

Status: stages 1–3 complete on 2026-09-24, with most of stage 4 delivered as part of stage 3. Broiler.Net 0.1.0-preview.1, Broiler.HTML 0.1.0-preview.6 and Broiler.HtmlBridge 0.1.0-preview.7 are merged and published on nuget.org; the Browser change (PR #147 on `claude/broiler-net-stage-3`) pins them and is validated against them. The [stage 1 audit and architecture](cookie-support-stage-1.md) records the original request inventory, package/source alignment, dependency graph, contract ownership, and behavior matrix. The [stage 2 implementation report](cookie-support-stage-2.md) records the Broiler.Net cookie engine, its validation and the 2026-09-24 audit, with each finding's resolution. The [stage 3 implementation report](cookie-support-stage-3.md) records the transport, the loader migration, the current request inventory, deviations, limitations and verification. Exact package and standards revisions are in [the baseline snapshot](cookie-support-baseline.json); the stage 3 report lists the published packages and the commits they were built from.

The target is one profile-owned cookie service used by navigation, JavaScript, and every network resource loader. Completion means tested cookie behavior across all supported browser request paths, durable storage, isolation, and user controls. Sharing a .NET CookieContainer alone does not supply the browsing-context information needed for this target.

**1. Establish the scope and ownership**

The current integration points are:

| Repository | Existing code | Required change |
| --- | --- | --- |
| Browser | `BrowserApp.CreatePageHttpClient`, `PageLoader` | Inject the profile network session; make redirect processing visible. |
| Browser | `RenderingPipeline`, `PageRequest`, script-engine construction | Carry navigation origin, top-level site, request purpose, and profile through loading and execution. |
| HtmlBridge | `Registration.cs`, `WindowDocumentMiscBinding.SetCookie` | Replace the document-local string with access to the shared service. |
| HtmlBridge | `ResourceLoader`, `ScriptExtractionService` | Replace independent static clients with injected transport. |
| HtmlBridge | `FetchBinding`, XHR, `BeaconBinding`, nested documents | Supply credential modes and the correct document/frame context. |
| HTML | `ImageDownloader`, `StylesheetLoadHandler`, `HtmlContainerInt` font loading | Route image, stylesheet, and font requests through the same policy-aware transport. |

Audit module imports, dynamic scripts, workers, media, preloads, and any WebSocket handshake implementation before closing the inventory. Classify each as integrated, non-networking, or an unsupported browser capability; an unexamined path is not a pass. The sibling source checkouts were inspected, but Browser consumes NuGet packages: verify package/source alignment before implementation.

Put reusable contracts and cookie algorithms in a dependency-neutral networking library below HTML and HtmlBridge. Name: `Broiler.Net`, confirmed in stage 1 (see its package name note). Keep profile directories, persistence configuration, and settings UI in Browser. Lower layers must not reference Browser.

Pin a compatibility baseline and record intentional deviations. Use the [6265bis-22 user-agent algorithms](https://datatracker.ietf.org/doc/html/draft-ietf-httpbis-rfc6265bis-22) for all cookie parsing, storage and retrieval, with versioned conformance fixtures; Fetch and HTML supply request and document context only (see the stage 1 reference layering). The IESG approved -22 on 2025-12-02 and it has been in RFC Editor final review since 2026-08-13, with no RFC number yet: re-pin to the RFC when it is published and diff it against -22. Recheck the [successor cookies draft](https://datatracker.ietf.org/doc/html/draft-ietf-httpbis-layered-cookies) at each stage rather than silently changing behavior midway.

**Exit:** an inventory of request paths, a dependency graph, and a behavior matrix covering supported APIs and known prerequisites.

Completed: see the stage 1 audit linked above. The confirmed design uses a new BCL-only Broiler.Net package; implementation begins in stage 2. Unsupported network APIs and engine limitations are tracked explicitly rather than counted as cookie support.

**2. Build the cookie engine and deterministic tests**

Proposed components: `CookieRecord`, `CookieStore`, `CookieParser`, `CookiePolicy`, `SiteResolver`, and an injected clock. Separate parsing, acceptance, storage, retrieval, and serialization so each can be tested without networking.

Support host-only/domain matching, default paths, path matching, expiry, Max-Age precedence, deletion, replacement, creation order, Secure, HttpOnly, SameSite, and cookie prefixes. Apply public-suffix checks, canonical host handling, secure-cookie overwrite protection, input limits, expiry limits, and bounded eviction. Preserve individual Set-Cookie fields; never split them on commas. Follow the pinned [cookie specification](https://datatracker.ietf.org/doc/html/draft-ietf-httpbis-rfc6265bis-22) for the precise algorithms and replacement identity.

Bundle a versioned [Public Suffix List](https://publicsuffix.org/list/) and update it through a repeatable release process. Exercise private suffixes, wildcard/exception rules, IDNs, IP literals, localhost, and trailing dots. Do not calculate a site by taking the last two hostname labels.

Design records with partition metadata from the beginning. Use thread-safe mutation and snapshots; deliver notifications outside locks. Keep hot reads in memory. Define size/count budgets and deterministic eviction as documented implementation policy.

**Exit:** table-driven algorithm tests, malformed-input/fuzz tests, and concurrency tests pass with a fake clock.

Completed: the standalone Broiler.Net package implements the parser, policy, store, site resolver and pinned PSL. Its 233 component tests pass, and an isolated local-package consumer check succeeds. Acceptance and retrieval policy are internal and tested through `CookieStore`; serialization is part of retrieval. The source is not yet committed. See the stage 2 report for behavior, limits, remaining integration work and the audit findings assigned to stage 3.

**3. Introduce the profile network session and migrate every loader**

Create `BrowserProfile` owning a cookie store, policy, persistence service, and long-lived network session. Normal windows sharing a profile share cookies; separate profiles do not. Avoid a process-global cookie jar; today each of the six HTTP clients keeps a hidden one (see the stage 1 request inventory).

Introduce immutable request context containing the actual target URL, initiator origin, site-for-cookies, top-level site, ancestor context, navigation type, method, credentials mode, partition key, and redirect history. Distinguish document URL/origin from the resource-resolution base URL: an HTML base element must not change cookie ownership. Handle opaque/sandboxed and inherited document origins explicitly.

Disable automatic handler cookies and automatic redirects on the managed path, replacing each client's hidden jar with the profile session in the same change: navigation flows such as the [Google consent flow](google-search-post-consent-challenge.md) rely on H1's jar today. For each hop, select cookies immediately before sending, process response cookies before following a redirect, then recompute context and headers. Preserve correct method/body behavior for 301/302/303/307/308, bound redirects, and strip credentials where required. Return the final URL for GET navigations, Fetch responses and frame documents; all three currently keep the requested URL.

Migrate the inventory from step 1 while preserving cancellation, timeouts, User-Agent, connection pooling, resource tracing, and offline-test policy. `BroilerUserAgent` and `BroilerHttpProtocol` move from `Broiler.Layout.Net` into `Broiler.Net.Http`; Layout keeps its copy until its next release. HTTP error responses must still reach cookie processing before callers throw for status. Reevaluate cookies on retries. Keep parallel requests deterministic with respect to completed mutations without promising an ordering the network does not provide.

Audit shared resource caches and prefetching. Partition or bypass authenticated cache entries as needed to prevent reuse across profiles or top-level partitions; cookie changes must not replay response headers or resurrect deleted cookies from cached metadata.

**Exit:** local servers demonstrate that a cookie received during navigation reaches protected scripts, stylesheets, images, fonts, and nested documents through their real loading paths. No active loader maintains a competing automatic jar.

Completed: Broiler.Net's `BrowserNetworkSession` is the profile's transport. `BrowserProfile` owns it in every Browser head, and navigation, the script extractor, every HtmlBridge loader and HTML's image, stylesheet and font loaders send through it with per-document request contexts and final URLs. The six handler jars are gone; the fallback clients left for hosts without a transport send and store no cookies. The exit test runs a full `BrowserApp` against loopback servers: the redirected navigation's cookies reach the page's scripts, module import, stylesheets, font, image and iframe, and a second site's Lax cookie is withheld. The stage 4 script gates shipped in the same change. Two review rounds were fixed or classified. Persistence (stage 6) and user cookie settings (stage 5) are not part of it. The packages are published; the Browser change awaits merge. See the stage 3 report for the inventory, deviations and known limitations.

**4. Connect JavaScript and enforce request boundaries**

Replace the stub with `GetDocumentCookies(CookieDocumentContext)` and `SetDocumentCookie(text, CookieDocumentContext)`. Bind each document and frame separately while using the profile store. Preserve JavaScript string conversion and use the document's effective context for access. Test non-HTTP documents and sandboxed/opaque origins against the [HTML document.cookie rules](https://html.spec.whatwg.org/multipage/dom.html#dom-document-cookie).

Implement Fetch `omit`, `same-origin`, and `include` for both sending cookies and accepting response cookies. Wire XHR `withCredentials` and each resource type's credentials rules, including script/module crossorigin behavior and beacon requests. Reject the Fetch forbidden request headers, including `Cookie` and `Cookie2`; filter `Set-Cookie` and `Set-Cookie2` from script-visible response headers. Implement the credentialed CORS/preflight and response-filtering behavior needed by these paths. The hidden bridge jar already exposes these paths today, so these gates close an existing leak. Cookie acceptance and JavaScript response access are separate decisions: do not simply discard all cookies whenever CORS hides a response. Use the [Fetch Standard](https://fetch.spec.whatwg.org/) as the contract.

Check that scripts cannot read or overwrite HttpOnly cookies and cannot bypass policy through Request/Headers objects, XHR, frames, or Cookie Store. Bind both supported JavaScript engines through the same host API.

**Exit:** a cookie set in JavaScript reaches the next eligible HTTP request; a server cookie becomes visible to eligible script access; protected cookies remain inaccessible. Cross-origin negative tests pass as well as same-origin login tests.

**5. Complete site policy and partitioned cookies**

Implement schemeful SameSite decisions using full navigation and frame context, including redirect chains and safe/unsafe methods. Same-site status follows 6265bis-22 §5.2 (site for cookies over the ancestor chain against the request URL), is cross-site when any URL in the redirect chain is cross-site, and applies the §5.2 reload rule; the pinned WPT SameSite tests are the conformance oracle, not Fetch's same-site mode. Explicitly document the chosen default-SameSite compatibility behavior. Add setting/retrieval tests for nested cross-site frames and reload/navigation variants.

Implement Partitioned cookies using the pinned [CHIPS](https://github.com/privacycg/CHIPS) spec draft: the partition key is (top-level schemeful site, cross-site-ancestor bit), used in storage and retrieval; Partitioned requires Secure; partitioned and unpartitioned entries can coexist. Apply deletion to partitions as well as domains. Budget partitioned cookies per embedded registrable domain within a partition; CHIPS rejects a cross-party per-partition cap as a side channel.

Provide allow, block-all, and block-unpartitioned-third-party modes, with site exceptions. Proposed initial default: allow standards-eligible cookies for compatibility, with an explicit user-selectable third-party restriction. This is a product default, not a standards requirement. Apply policy consistently to HTTP and script access, and make navigator.cookieEnabled reflect the documented global setting without implying every third-party cookie is allowed.

**Exit:** the same embedded origin gets separate partitioned state under two top-level sites and in an A→B→A frame chain; the pinned WPT partitioned-cookies redirect tests pass; blocked cookies cannot reenter through another API or resource loader.

**6. Add persistence, ephemeral isolation, and user controls**

Use a schema-versioned SQLite store behind `ICookiePersistence`, with in-memory reads and transactional background writes. Persist eligible unexpired persistent cookies. Keep session cookies memory-only by default; make any later session-restoration policy explicit. Load before the profile's first request, flush changes incrementally, and support clean shutdown, recovery, migration, and a defined single-writer/multi-process ownership model.

Use application-private profile directories and platform protection for sensitive stored values where available. Define behavior for unavailable key storage without silently promising encryption on every OS. Cookie values must not appear in ordinary logs or resource traces.

Add an isolated ephemeral profile/session entry point whose cookies never reach the persistent backend. Test teardown and cancellation so late responses cannot recreate cleared or disposed state. This establishes cookie isolation; a complete private-browsing feature also needs separate history, storage, and cache work.

Add a cookie/site-data view, deletion of individual cookies, clear-by-site, clear-all, clear-on-exit, and per-site policy controls. Display domain, path, expiry, flags, and partition information. Make deletion durable and invalidate pending writes; define how in-flight responses interact with a user clearing data. Include the cookie portion of Clear-Site-Data processing with standards-based scope tests.

**Exit:** restart preserves persistent cookies and drops session cookies; isolated profiles cannot observe each other; clear operations remain effective after restart and racing requests.

**7. Finish modern cookie APIs and explicit prerequisites**

Implement the [Cookie Store API](https://cookiestore.spec.whatwg.org/) on supported secure Window contexts: asynchronous read/write/delete methods and correctly scheduled change events backed by the same store and access checks. Cover changes across documents, expiry, deletion, and HttpOnly filtering.

Inventory service-worker support before exposing worker cookie subscriptions. If the required worker lifecycle is missing, track it as a prerequisite and keep that API unexposed until implemented. Likewise, third-party storage-access grants require the relevant permissions and frame infrastructure; implement and test them before claiming that API's compatibility. Reserve their policy hooks now.

Distinguish two release claims: complete cookie behavior on supported browser surfaces, and full modern cookie API coverage. The latter requires closing these prerequisites, not marking skipped tests as support.

**Exit:** exposed APIs have functional semantics, engine parity, and isolation tests; every remaining platform prerequisite has a named owner and completion criterion.

**8. Validate integration and release in dependency order**

Build a deterministic multi-origin HTTP/HTTPS test fixture, including subdomains, two registrable sites, and redirect chains. Exercise real transports and browser loading paths, with fake time for lifetime tests. Add tests as each stage lands rather than postponing verification until this stage.

The release matrix must cover:

| Area | Required evidence |
| --- | --- |
| Sessions | Login, navigation, script access, protected subresources, logout, and restart. |
| Isolation | Domains, paths, frames, profiles, ephemeral sessions, top-level partitions. |
| Policy | Secure/HttpOnly, SameSite variants, third-party settings, credentials modes, CORS. |
| Transport | Redirect response cookies, multiple Set-Cookie fields, error responses, retries, cache/prefetch behavior. |
| Durability | Expiry, eviction, concurrent mutation, interrupted writes, migrations, deletion races. |
| Platforms | Windows, Linux, Android; both supported JavaScript engines. |

Run applicable Web Platform Tests for cookies, document.cookie, credentialed Fetch/XHR, partitioning, and Cookie Store. Record pinned test revisions, passes, failures, and unsupported prerequisites separately. Compare selected edge cases against current reference browsers; use public-site consent/login flows only as supplementary smoke tests.

Deliver staged changes: shared contracts and engine; transport and loader migration; DOM/Fetch integration; SameSite/partition policies; persistence and controls; modern APIs; conformance and package integration. Each stage includes its relevant tests and must not enable a partially enforced path by default. Publish versioned dependency packages in graph order, then update Browser references together. Preserve unrelated working-tree changes.

The largest dependencies are complete request-context propagation, cross-origin enforcement, and access to lower-level resource loaders. Resolve these before polishing settings UI. Do not call the feature complete while any supported browser path bypasses the cookie policy service.
