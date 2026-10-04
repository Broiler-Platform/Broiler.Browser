# Chromium and packet-capture follow-up

Target: `https://www.google.com/search?q=test`. This extends the
[Broiler HTTP 429 investigation](google-search-429-investigation-2026-10-03.md).

## Actual control result

On 2026-10-04 at 06:37 UTC, an isolated Chrome 154.0.8037.97 session driven by
Playwright 1.59.1 also received **HTTP 429** from Google. It used a new, empty
browser context, headless mode, a 1365×900 viewport, and default browser language
and network settings. No user profile, stored login, UA override, request
interception, or automation-hiding modification was used.

The main-document sequence was:

| Step | Response | Transport |
| --- | --- | --- |
| GET `/search?q=test` | 200 challenge document | HTTP/3, QUIC, IPv6 |
| Script navigation to `/search?q=test&sei=…` | 302 | HTTP/3 |
| Redirect to `/sorry/index?…` | 429 unusual-traffic page | HTTP/3 |

`page.goto()` returned the initial 200 response; the later network events and
final page established the 429. Treating the navigation API's return value alone
as the search outcome would have misclassified this run.

The CDP extra request information shows `SG_SS` in the Cookie header of the
second search request, alongside `AEC` and `__Secure-ENID`; none had a blocked
cookie reason. Values were not written to the structured header record. This
confirms cookie transmission **for Chromium**. It does not prove that Broiler
sent an equivalent value, nor that Google accepted the signal.

Chrome rendered the refusal and loaded two reCAPTCHA documents with HTTP 200.
There was no browser crash or recorded network loading failure. A page error,
`ReferenceError: solveSimpleChallenge is not defined`, occurred after the 429;
the refusal's own body onload attribute references that name. It cannot explain
the earlier server refusal. No CAPTCHA was submitted or solved.

Broiler.Cli was rerun at 06:39 UTC with verbose logging, stack sampling, all
JavaScript turn/gap tracing, 300-second analysis limit, 60-second navigation
limit, and the same viewport. A preliminary run verified that consent form 0
still meant “Alle ablehnen” (`set_eom=true`). The post-consent run again reached
HTTP 429 in the window scope, over HTTP/1.1, archived the complete 3,335-byte
refusal, and completed normally. Both clients' refusal pages reported the
**same public IPv6 address**; the address is intentionally omitted here.

These were not identical consent flows: Broiler received the consent page and
submitted Reject All; fresh Chrome went directly to the challenge without
presenting consent. Chrome reported `navigator.webdriver=true`, a HeadlessChrome
UA, and `de-DE` language. This is an automated-browser control, not a test of a
normal established, manually driven Chrome session.

The refusal is therefore **not exclusive to Broiler or HTTP/1.1**. The result does
not distinguish automation detection, network reputation, request history, or
other signals in Google's private classifier. Chromium also sending `SG_SS`
before a refusal demonstrates that cookie presence alone is insufficient.
Broiler's generic error screen remains a separate response-handling limitation.

## Evidence and reusable diagnostics

Local, Git-ignored evidence is in `artifacts/google-search-2026-10-04-control/`:

- `chromium/events.jsonl`: timestamped CDP request/response events, actual request
  header metadata, cookie names/block reasons, HTTP version, remote endpoint,
  connection reuse, security details, timing, console and page errors.
- `chromium/documents.json`: main-frame and iframe response statuses.
- `chromium/document-2-429.html`: complete 3,542-byte Chromium refusal.
- `chromium/terminal.json`, `terminal.png`: final document and screenshot.
- `chromium/netlog.json`: valid Default-mode NetLog with 10,758 events. The log
  covers this isolated browser process, including its startup/background traffic.
- `broiler-initial/`, `broiler-post-consent/`: full CLI analysis bundles and
  adjacent verbose console logs. The latter has `completed=true`, seven recorded
  transport operations across the analysis/window profiles, and no Retry-After.

The first Chromium 200 body could not be retrieved after its fast navigation:
CDP reported that its resource identifier no longer existed. Its status remains
recorded, but its HTML is unavailable. The later 429 body was successfully saved.
The live collector was attached interactively; the reusable runner subsequently
added explicit per-document `bodyError` and bounded cleanup.

`scripts/chromium-network-diagnostics.cjs` provides the repeatable control. It
uses an already installed Playwright package and Chrome executable and requires
a new output directory. This host's installed paths are shown below; substitute
local paths elsewhere. The cached Playwright Chromium revision 1217 is incomplete,
so this command deliberately uses installed Chrome.

```powershell
node scripts/chromium-network-diagnostics.cjs `
  --url 'https://www.google.com/search?q=test' `
  --output artifacts/google-search-chromium-next `
  --playwright 'D:\Broiler\tests\wpt\node_modules\playwright' `
  --executable 'C:\Program Files\Google\Chrome\Application\chrome.exe'
```

If consent appears, inspect its actual controls before supplying the optional
`--consent-button 'EXACT VISIBLE LABEL'`. The collector does not solve CAPTCHAs
or retry an HTTP failure. It waits for one second without document-request
activity, with a ten-second cap; that is a bounded heuristic, not a guarantee
that all future timers have completed. Initial and latest main-document statuses
are recorded separately. Cleanup closes the test page and limits body draining
to five seconds, preserving partial diagnostics on failure.

Structured URLs redact query values except the single sample `q=test`; header
records retain cookie names without values and allowlisted diagnostic fields.
**The complete bundle is not redacted**: page bodies, snapshot text, screenshots,
console messages and the default NetLog can contain IP addresses or challenge
tokens. Evidence remains local under ignored artifacts.

The runner was validated against loopback fixtures: delayed script navigation
200→302→429, cookie transmission and structured-record redaction, saved refusal
HTML/screenshot, repeated-query redaction, and cancellation of an unending
response body. All passed; the latter closed in 7 ms and recorded a body error.
These checks made no Google requests. JavaScript syntax and `git diff --check`
also passed. No Broiler runtime code changed in this follow-up.

## What the installed pcap tools add

This host has running **Npcap 1.88** and **Wireshark/dumpcap/tshark 4.6.9** under
`C:\Program Files\Wireshark`. `dumpcap -D` successfully enumerated nine devices;
actual packet capture access has not been tested and no pcap was collected.
At inspection time, the IPv4/IPv6 default routes used
`vEthernet (Neuer virtueller Switch)`, Windows ifIndex 11, dumpcap interface 5,
not the physical Ethernet interface. Recheck routing before capturing.

A bounded capture could compare endpoints, connection counts, TCP retransmissions
and resets, handshake timing, visible ClientHello properties, and offered ALPN.
Include both IPv4/IPv6 and TCP/UDP 443: Chromium actually used QUIC here. Encryption
and features such as encrypted ClientHello limit what is visible. Endpoint filters
can include other processes using the same Google address and can miss changed
DNS answers, so an unfiltered capture is a poor first diagnostic.

Pcap alone cannot reveal encrypted HTTPS status codes, headers, cookies or bodies.
Chromium session secrets can enable decryption. Microsoft's documented Windows
SChannel limitation means ordinary .NET SslStream key logging is not an equivalent
option for Broiler; QuicConnection has separate support. For exact Broiler request
metadata, instrumentation after session/header construction is the more direct
next step. An interception proxy would change the Google-facing TLS connection
and confound a transport-fingerprint comparison.

Further useful controls are a fresh, manually driven Chrome session with its own
NetLog, and Broiler metadata tracing after cookies/headers are attached. The present
evidence does not justify changing Broiler's UA, protocol or webdriver flag as a
claimed fix for Google's refusal.

## Primary documentation

- [Chromium NetLog capture and startup options](https://www.chromium.org/for-testers/providing-network-details/)
- [Playwright network monitoring](https://playwright.dev/docs/network)
- [Playwright browser modes and branded Chrome](https://playwright.dev/docs/browsers)
- [Chrome DevTools Protocol network events](https://chromedevtools.github.io/devtools-protocol/tot/Network/)
- [Wireshark TLS decryption](https://wiki.wireshark.org/TLS/)
- [Dumpcap capture filters and limits](https://www.wireshark.org/docs/man-pages/dumpcap.html)
- [Microsoft SSLKEYLOGFILE support and Windows limitation](https://devblogs.microsoft.com/dotnet/dotnet-9-networking-improvements/#sslkeylogfile-support)
- [Windows SslStream key logging request](https://github.com/dotnet/runtime/issues/94843)
- [Google's unusual-traffic explanation](https://support.google.com/websearch/answer/86640?hl=en)
