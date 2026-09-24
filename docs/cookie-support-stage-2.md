# Cookie support: stage 2 implementation

Completed on 2026-09-24 in the new standalone sibling repository Broiler.Net,
branch `codex/cookie-engine-stage-2`. The stage 2 state itself was never committed: its
files were only marked intent-to-add until the branch's first commit, `3365a88`, which
also contains the stage 3 transport and the audit fixes. No remote was created and no
package was published. Browser and HtmlBridge production references remained unchanged
in this stage. Stage 3 has since been completed (see [its report](cookie-support-stage-3.md)). This report describes the stage 2 state, apart from
the Resolution column of the audit table below.

The package README in the Broiler.Net repository documents the public API,
encoding, quotas, notification semantics and the PSL update procedure.

| Component | Implemented behavior |
| --- | --- |
| CookieParser | Parses individual response fields without comma splitting; preserves octets, handles duplicate attributes, tolerant cookie dates, lifetime precedence/capping, control-character rejection and input limits. |
| CookiePolicy | Domain/host-only/path checks, public suffix rejection, secure origins, SameSite acceptance/retrieval, explicit __Host path and prefix checks, and partition-key validation. These decisions are internal and exercised through CookieStore. |
| CookieStore | Atomic mutation, HTTP/document access separation, HttpOnly protection, insecure overwrite protection, deletion, stable creation order, expiry, deterministic bounded eviction and immutable administrative snapshots. Serialization is part of retrieval. |
| SiteResolver | Canonical hosts, IDNs and IP literals; schemeful sites; private PSL rules, longest matches, wildcards, exceptions and trailing-dot handling. |
| Context/record types | `CookieRequestContext` (HTTP) and `CookieDocumentContext` (non-HTTP) replace the stage 1 `CookieAccessContext`; the host supplies same-site status and partition key. Immutable records include host-only and partition identity. |
| Change notifications | Versioned mutation batches delivered outside locks. Observer failures are rethrown as AggregateException after the change commits, including from read paths that purge expired cookies; the read result is then lost. |

The runtime package targets net10.0 and has **zero NuGet dependencies**. The PSL
is embedded and also packaged with revision/hash metadata and third-party notices.
Its source revision matches the stage 1 baseline. Git attributes mark the list and
its fixture `-text` so checkouts keep the upstream bytes; verified at `3365a88`, where the
committed blobs and a fresh clone match the recorded upstream SHA-256 values.

One edge case was checked against the pinned WPT
[explicit __Host path fixture](https://github.com/web-platform-tests/wpt/blob/e35344a20f922751f5ea8cc678cfd79d65056d5c/cookies/prefix/__host.explicit-path.https.window.js):
a missing, empty or valueless Path must not qualify just because it defaults to `/`.
The engine requires the last Path attribute to be literally `/`, and both access
methods have regression cases. It also rejects a relative Path and a later duplicate
(`Path=/; Path=`); WPT does not cover those, and the choice follows Chromium. This is
stricter than 6265bis-22 §5.7 step 21.3, which accepts all of these cases when the
default path is `/`; step 1 permits ignoring the cookie. The test implementation is
original; the upstream PSL list and PSL test fixture are the only vendored upstream data.

**Validation completed on Windows / .NET SDK 10.0.401**

- `dotnet test Broiler.Net.slnx -c Release`: **233 passed, 0 failed, 0 skipped**.
- Coverage includes all 78 pinned upstream PSL cases, 10,000 seeded malformed
  fields, 2,000 model-based store steps, 2,000 concurrent replacements and 1,000
  mixed concurrent partition/expiry/clear operations.
- Deterministic-time tests cover expiry/deletion, ordering and quotas; notification
  tests check reentrancy, lock release, revisions and observer failures.
- The PSL update script was run with the pinned revision and expected hashes;
  the embedded data and fixture hashes are checked by the test suite.
- Release build and local NuGet pack succeeded with warnings treated as errors.
- A separate executable restored only the local package, with a fresh package
  cache, and verified its public cookie API, prefix checks and embedded PSL.
- Package metadata inspection found zero runtime package dependencies.

The unpublished package is `artifacts/packages/Broiler.Net.0.1.0-preview.1.nupkg`
in the Broiler.Net repository, and the consumer check is under `artifacts/consumer`.
Both are ignored by Git, so this evidence exists only on the machine that produced
it. Rebuild the package before using it in stage 3.

**Boundaries for the next stage**

The cookie engine is implemented; the browser does not yet use it. The caller
currently supplies same-site status, navigation details and partition identity.
Full derivation across documents, ancestors and redirect chains, Fetch credentials,
CORS/header filtering, third-party settings, all six HTTP loaders, cache isolation,
persistence and browser UI remain with their planned stages. The HTTP transport
contracts have not been implemented prematurely.

No browser WPT run or Linux/Android runtime validation was performed in this stage.
The component test pass is not a browser-conformance claim. The next task is stage 3:
profile-owned transport and request-context propagation, with script-access safety
gates from stage 4 kept in place before enabling a shared cookie jar in production.

**Audit (2026-09-24)**

The four cookie documents were checked against the source they cite: the Browser
working tree, HtmlBridge `f316987`, HTML `9b65488`, the Broiler.Net working tree and
the restored packages. The engine was checked against 6265bis-22, the pinned CHIPS
and WPT revisions, and the suite's packaging conventions. Each reported problem was
verified by three adversarial reviewers using separate evidence (source at the pinned
commits, runtime probes against the built DLL, spec text). The documentation
corrections are applied in these documents. Engine findings upheld by at least two
of the three reviewers:

| Severity | Area | Finding | Resolution |
| --- | --- | --- | --- |
| medium | Partition quota | The 180-cookie per-partition cap spans all embedded domains: one third party can evict another's partitioned cookies and detect its activity, a design the pinned CHIPS explainer rejects. Per-party limits multiply across subdomains, and a test asserts the eviction. | Fixed in stage 3: each embedded site has its own bucket per partition (180 cookies, 10240 name+value octets); subdomains share it; no cross-party cap. |
| medium | Domain quota | Per-domain quotas key on the exact Domain string and global eviction ignores Secure, so one site's subdomains can flush other sites' cookies; an HTTP attacker can evict a Secure cookie and then overlay it. | Fixed in stage 3 for the site quota: buckets key on the registrable domain, and a bucket evicts insecure cookies first; tests cover subdomain spraying and the overlay attack. The 6000-cookie global overflow still evicts the least recently used cookie whether Secure or not, as 6265bis-22 §5.7 prescribes. |
| medium | Package | No license: no LICENSE file or PackageLicenseExpression; the sibling packages are Apache-2.0. | Fixed in stage 3: `LICENSE`, expression `Apache-2.0 AND MPL-2.0` (MPL-2.0 for the bundled PSL). |
| medium | Retrieval cost | Each retrieval scans the whole store under one global lock and re-canonicalizes the request host per cookie (about 2 ms per request at the 6000-cookie cap, no parallelism). | Fixed in stage 3: retrieval visits only the request host and its parent domains through a domain index. |
| medium | Default path | Default-path has no length limit, so one site can pin hundreds of MiB (about 690 MiB demonstrated) despite the count limits. | Fixed in stage 3: a resulting path over 1024 octets, explicit or defaulted, is rejected (`RejectedSize`). |
| low | Host parsing | `TryGetHttpHost`, `IsSecure` and `GetSite` throw `UriFormatException` for hosts `Uri.TryCreate` accepts (for example a ZWJ); the exception escapes `CookieStore`. | Fixed in stage 3: such URLs are not HTTP hosts, and the host APIs no longer throw. |
| low | Host parsing | Trailing-dot IPv4 hosts (`10.0.0.1.`) are treated as DNS names: different IPs share a site, Domain cookies cross IPs, and `127.0.0.1.` is not loopback. | Fixed in stage 3: a host ending in a number must be IPv4 and becomes a dotted quad; the session connects to that address. |
| low | Host parsing | ASCII hosts that the URL Standard and `System.Uri` accept (leading/trailing hyphen, labels over 63 characters, invalid `xn--` labels) are rejected, so their cookies silently fail. | Fixed in stage 3: plain ASCII labels are only lowercased, following the URL Standard host parser. `xn--` and Unicode labels still go through `IdnMapping`, which rejects invalid ones, as the URL Standard does. |
| low | Secure origins | `IsSecure` trusts IPv4-mapped loopback and every `*.localhost` name unconditionally, beyond the Secure Contexts rules. | Fixed in stage 3: IPv4-mapped loopback is not secure. `*.localhost` stays secure because `BrowserNetworkSession` pins those names to loopback; another transport must do the same. |
| low | Path matching | Matching uses `Uri.AbsolutePath`, which decodes escaped unreserved characters, so a `Path=/%7Euser` cookie is stored but never sent (§5.1.4 compares literally). | Open: documented as a known limitation in the package README, with a workaround for hosts. |
| low | Third-party policy | Retrieval always includes unpartitioned cookies in third-party contexts, and `CookiePolicy` has no hook for block-unpartitioned-third-party mode or site exceptions. | Open: stage 5. |
| low | Observers | `ReceiveResponseCookies` stops processing the remaining Set-Cookie fields when an observer throws. | Fixed in stage 3: every field is stored first, then one `CookieObserverException` is thrown; reads never publish. |
| low | Snapshots | `Snapshot()` does not report its revision, so out-of-order change batches cannot be reconciled with a baseline. | Fixed in stage 3: `Snapshot(out long revision)` and `Revision`. |
| low | Prefixes | `__Http-` and `__Host-Http-` (layered-cookies-02, pinned WPT) are neither enforced nor tracked as extension work. | Fixed in stage 3: enforced as a documented extension (Secure, HttpOnly, HTTP source). |
| low | Prefixes | The `__Host-` explicit-Path deviation from 6265bis-22 step 21.3 is not documented in the package README (recorded above). | Fixed in stage 3: documented in the README. |
| low | Parser | `CookieParser.Parse` throws for a valid `now` with a positive UTC offset near `DateTimeOffset.MaxValue`; the store is unaffected. | Fixed in stage 3, with a regression test. |
| low | Tests | The 64 KiB field-bound test cannot fail: its input also exceeds the 4096-octet name/value limit. | Fixed in stage 3: the test now reaches the bound only through an ignored attribute. |
| low | Tests | Untested spec steps: deletion by past Expires alone, the reverse domain direction of the secure-overlay check, nameless-cookie replacement, cross-site non-HTTP read of SameSite=None, read-path observer failure. | Fixed in stage 3: each step has a test. |
| low | PSL updater | The update script accepts unmerged fork commits served under the upstream URL, and its hash check is circular because the operator hashes the same download. | Fixed in stage 3: revisions not on upstream main are refused (GitHub compare API), and the hashes come from the operator's review of the commit, not from the updater's download. |
| low | Package | Package metadata diverges from the suite's packaging props (authors, repository, XML docs, symbols, icon), so the siblings' pack verification would reject it. | Fixed in stage 3: the vendored `eng/Broiler.Packaging.props`, icon, XML docs, symbols and `eng/pack.ps1`. The nuspec names no repository URL or commit until one exists. |
| low | Repository | Nothing is committed (unborn branch, intent-to-add files), and the consumer smoke test exists only in the Git-ignored artifacts folder. | Fixed after stage 3: committed as `3365a88` (no remote yet). Open: the consumer smoke test is still only in the ignored artifacts folder; CI checks the packed files instead. |

The Resolution column was added after stage 3 (2026-09-24), checked against the Broiler.Net working
tree, its README and its tests: 19 findings are fixed and 2 remain open. The
[stage 3 report](cookie-support-stage-3.md) covers the rest of stage 3.
