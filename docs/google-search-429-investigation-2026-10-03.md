# Google Search HTTP 429 investigation

Measurements were taken on 2026-10-03; review and regression validation continued on
2026-10-04. Target: `https://www.google.com/search?q=test`.

Follow-up: the [2026-10-04 Chromium control and capture investigation](google-search-chromium-control-2026-10-04.md)
also reproduced HTTP 429 in fresh automated Chrome over HTTP/3 and confirmed the
installed Npcap/Wireshark tools. It records the limits of that comparison.

## Confirmed result

Google returns an actual HTTP 429 from `/sorry/index`, with an HTML explanation of
unusual traffic and a reCAPTCHA challenge. Broiler does not synthesize the status.
The browser's generic load-error screen hides that explanation because `PageLoader`
rejects non-success statuses. Previously it rejected them before reading the body,
which also prevented the CLI's diagnostic transport from saving the explanation.

The deciding Google signal is **not established**. The refusal body describes an
automated/unusual-traffic classification, but cannot distinguish client capabilities,
request rate, earlier traffic, cookies, or shared-IP reputation. No single missing
binding has been shown to cause this response.

## Reproduction and retained evidence

The initial run with a fresh profile received the EU consent page (HTTP 200), not
search results and not 429. Its form index 0 was inspected and was the “Reject all”
form (`set_eom=true`). The new explicit `--submit-form 0` option submits that fetched
form, with its actual hidden fields and the same temporary profile's cookies.
The window comparison repeats that sequence with its own fresh profile.

```powershell
dotnet build src/Broiler.Browser.Cli/Broiler.Browser.Cli.csproj -c Release
$env:BROILER_TRACE_JS_ENTRY = 'turn=0,gap=0'
dotnet src/Broiler.Browser.Cli/bin/Release/net10.0/Broiler.Cli.dll `
  --analyze 'https://www.google.com/search?q=test' `
  --submit-form 0 --output-dir artifacts/google-search-2026-10-03/verified `
  --verbose --sample-stacks --analysis-timeout 300 --timeout 60 `
  --width 1365 --height 900
Remove-Item Env:BROILER_TRACE_JS_ENTRY
```

Use a new output directory to retain earlier evidence. Check the actual consent
markup before reusing a form index: the option selects a form, not a Google-specific
consent policy. `dotnet-stack` was already installed. All script turns/gaps, every
first-chance exception, phase timing, response archive, HAR, layout and both render
paths were enabled. The run completed in 13.3 seconds with exit 0; analysis exit 0
means the diagnostic run completed, not that Google returned results.

The local, ignored bundle is `artifacts/google-search-2026-10-03/verified/`:

- `report.html`, `report.md`, `report.json`: findings and component versions.
- `network.json`, `network.har`, `network-events.jsonl`: response evidence.
- `resources/0016-www.google.com-sorry-index.html`: the complete 3,335-byte refusal.
- `bridge-phases.json`, `exceptions.log`, `messages.log`, screenshots and resources.
- `../verified-console.log`: verbose phases and every JavaScript turn/gap.

The recorded sequence was:

| Scope | Operation | Final response |
| --- | --- | --- |
| analysis | GET `/search?q=test` | 200 consent document after HTTP redirect |
| analysis | POST `consent.google.com/save` | 200 search challenge after HTTP redirect |
| analysis | POST `/gen_204?cad=sg_trbl&…` | 204 |
| window | GET `/search?q=test` | 200 consent document after HTTP redirect |
| window | POST `consent.google.com/save` | 200 search challenge after HTTP redirect |
| window | POST `/gen_204?cad=sg_trbl&…` | 204 |
| window | GET `/search?q=test&sei=…` | HTTP redirect to `/sorry/index?…`, then 429 |

These are seven **transport operations**, across two profiles; HTTP redirect hops
are grouped within them. The window performed one script navigation before the
refusal. It did not reach the script-navigation budget, nor automatically retry 429.
The final response had no `Retry-After` header. The response body is complete in the
archive, and the window settled with `Error loading page` in 5.5 seconds.

## What the script and timing evidence do, and do not, establish

The challenge ran without an uncaught top-level script failure. First-chance
exceptions inside its obfuscated VM include numeric property reads and deliberately
thrown values; caught exceptions alone do not identify a missing API or a fatal
challenge failure. They remain available for follow-up in `exceptions.log`.

The saved `inline-3` success callback attempts to set `SG_SS` through `document.cookie`.
When that cookie is readable, its `S()` function removes the `sg_ss` URL parameter
and adds `sei`; the URL parameter is used as a fallback. Therefore the observed
`sei`-only navigation is consistent with the cookie branch. It is not evidence that
the signal was absent. The recorder captures authored request headers **before**
the session adds User-Agent, cookies and related fields, so this trace is not proof
of the exact cookie header on the wire.

The final run recorded 14 JavaScript turns, 8,684 ms total in script, a longest turn
of 3,651.9 ms, and a longest process-wide gap of 1,584.3 ms. There were **zero**
16,384 ms watchdog crossings. DOM parsing totalled 20.14 ms and document/window API
registration 1,164.78 ms across the two script documents; nested registration
timings overlap and must not be added again. This reproduction does not support the
older 28-second-gap explanation. Turn tracing is process-wide: a gap may span the
independent analysis and window runs rather than one page's lifetime.

Source audits match the loaded package commits: HtmlBridge preview.10 at `3ea17a4f`,
Broiler.Net preview.1 at `5ab213d9`, and Broiler.JS preview.5 at `c2497642`.
The HTTP and navigator User-Agent values agree. Several observable characteristics
remain candidates for compatibility work, not proven causes of this refusal:

- `NavigatorIdentityBinding` reports `navigator.webdriver=true` for the GUI too.
- Broiler.Net selects HTTP/1.1; it does not have a Chromium network stack.
- Navigator reports `en-US`/`en`, while the browser profile does not set HTTP
  `Accept-Language`.

## Diagnostic changes

`PageLoader` now consumes the response body **before** calling
`EnsureSuccessStatusCode`, on both transport and HttpClient paths. HTTP failures
still follow the existing error path; their scripts do not execute. The recorder
can consequently save the server's response, status and headers without changing
the browser into a CAPTCHA client.

The analysis now records the window's transport with a separate `scope`, reports
429 and `Retry-After` explicitly, and links the saved refusal. The window probe
recognizes terminal load errors instead of waiting for its timeout. It does not
compare images when the window failed or navigated to another document.

`network-events.jsonl` flushes request/response/body/archive events as they occur,
so a process crash does not erase the network record. `bridge-phases.json` measures
DOM/binding setup. `checkpoint-before-window.json` preserves an explicitly incomplete
report plus network, resource and bridge timing snapshots before the second run.
`analysis-in-progress.txt` remains when final report writing did not finish.

## Separate reCAPTCHA failure found during investigation

An experimental change that rendered HTTP error documents reached reCAPTCHA and
terminated with Windows stack-overflow exit code `0xC00000FD`. Its evidence is in
`artifacts/google-search-2026-10-03/post-consent-console.log` and the adjacent
`post-consent/resources/` directory. That experiment was not retained.

The stack enters `RunSubDocumentScripts` synchronously from an iframe `src` setter
while the parent reCAPTCHA script is still active. The trace does not establish an
infinite iframe recursion: generated frame size, nested execution and available
thread stack require a separate minimized reproduction. It occurred **after** the
429, so cannot explain why Google sent that status. No HtmlBridge or JS package
was changed in this investigation.

## Interpretation against the published documentation

[Google Search Help](https://support.google.com/websearch/answer/86640?hl=en)
describes this unusual-traffic page in terms of automated traffic, including traffic
from other users of a shared network. It recommends the presented reCAPTCHA; it does
not disclose the classifier or identify the signal in an individual response.
[Google's reCAPTCHA browser requirements](https://support.google.com/recaptcha/answer/6223828?hl=en)
list recent mainstream browsers; Broiler is not listed.

[RFC 6585 §4](https://www.rfc-editor.org/rfc/rfc6585.html#section-4) defines 429,
makes `Retry-After` optional, and leaves user identification and request counting to
the server. A small count in a single trace and an absent header do not rule out a
rate limit. The September notes were corrected accordingly; `gbv=1` was not tested
as a current workaround and is not claimed as one.

The next useful investigations are a same-network mainstream-browser control with
comparable consent state, a transport-level trace of request metadata without cookie
values, and a minimized offline reproduction of the iframe stack failure. None has
been substituted for evidence about Google's private decision in this report.

## Validation

On 2026-10-04, `dotnet test Broiler.Browser.Tests.slnx -c Release --no-restore`
passed all 440 tests (223 Core, 217 CLI). New local tests cover consent form
encoding and cookies across independent profiles, script navigation to 429,
archived error bodies without executing their scripts or retrying, response
headers, journal redaction and flushing, terminal window errors, and incomplete
checkpoint state. They use local fixtures rather than contacting Google.

`dotnet build Broiler.Windows.Browser.slnx -c Release --no-restore` succeeded with
zero warnings and errors. `git diff --check` passed. The Google refusal remains;
these changes improve diagnosis rather than claiming search compatibility.
