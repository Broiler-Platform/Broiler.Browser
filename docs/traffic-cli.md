# Traffic capture and process attribution

`Broiler.Traffic` is a small Windows/.NET 10 command-line application. It uses the
installed Wireshark `tshark.exe` and Npcap for capture and protocol decoding, then
matches packet endpoints against Windows TCP/UDP owner tables. The application has
no NuGet runtime dependencies and does not modify the browser or its connections.

## Build and run

Prerequisites: Windows, .NET SDK 10 to build, and Wireshark with TShark and Npcap.
The installed tools on the development machine were verified as Wireshark 4.6.9
and Npcap 1.88. Some Npcap installations restrict capture to administrators; if
TShark reports access denied, run the terminal as administrator.

From the repository root in PowerShell:

```powershell
dotnet build src/Broiler.Traffic/Broiler.Traffic.csproj -c Release
$traffic = '.\src\Broiler.Traffic\bin\Release\net10.0\Broiler.Traffic.exe'
& $traffic --list
```

Choose the interface carrying the traffic. Interface numbers are Npcap's numbers,
not Windows interface indexes. On this machine the earlier Google investigation
used a virtual Ethernet adapter, so choosing the physical adapter by name alone
can miss traffic. `--list` also shows the Npcap loopback adapter for local fixtures.

Capture web traffic for one minute, showing processes whose names contain `Broiler`:

```powershell
& $traffic -i 5 -f 'tcp port 443 or udp port 443 or port 53' `
  --process Broiler --seconds 60 --label 'Google search reproduction' `
  --output '.\artifacts\google-traffic.jsonl'
```

Use an existing output directory and a new filename for each run. The program
refuses to overwrite an existing output file. Start capture before reproducing
the action so it can see the handshake and hostname. The example's interface `5`
is illustrative; use the current `--list` result.

Other examples:

```powershell
# Browser launched as `dotnet Broiler.Cli.dll`: select that dotnet process's PID.
& $traffic -i 5 --pid 12345 --seconds 30 --label 'reCAPTCHA demo'

# Include otherwise unmatched packets while investigating attribution gaps.
& $traffic -i 5 --process chrome --include-unattributed --seconds 30

# Hostname filter matches google.com and its subdomains, when visible.
& $traffic -i 5 --host google.com --seconds 30 --json

# Custom Wireshark installation and a narrow capture filter.
& $traffic --tshark 'D:\Tools\Wireshark\tshark.exe' -i 5 `
  -f 'host 192.0.2.10 and (tcp port 443 or udp port 443)'
```

`--help` lists all options. Repeated `--process` values are alternatives, as are
repeated `--pid` values. If both kinds are present, the same owner must satisfy
both. Process names are case-insensitive substrings, without the `.exe` extension.
Matching all `chrome` processes includes network-service subprocesses; a single
browser PID may not own its network sockets. `--include-unattributed` includes
packets with no candidate owner, not packets assigned to a different process.

The default is 60 seconds; `--seconds` accepts 1–86400. Ctrl+C stops the capture and
its child processes. Exit 0 means TShark completed, 1 means a CLI/setup failure,
130 means interrupted or the duration-plus-15-second watchdog expired; other
TShark errors retain its exit code. TShark's diagnostics and final counts go to
stderr; the readable table or `--json` records go to stdout.

## What attribution means

- **TCP:** full local/remote address and port tuple matched in a sampled Windows
  table, in either packet direction. Loopback can show both endpoint owners.
- **UDP, including QUIC:** local address/port owner candidates. Windows' UDP table
  does not identify the remote peer. Shared ports can produce multiple candidates;
  the program preserves them instead of picking one.
- **Unknown:** a socket may have closed between samples, traffic may belong to a
  VM/VPN/proxy, or capture delivery may be too late. No PID is invented. IPv6
  link-local addresses are left unattributed because the packet fields lack an
  interface scope ID. Dual-stack IPv4 traffic represented only by an IPv6 wildcard
  socket can also remain unknown.

Tables are sampled approximately every 100 ms and retained for five seconds. The
closest snapshot within 500 ms of the packet's capture time is used. Its timestamp
is included in JSON. These are observations near capture time, not OS events
proving packet ownership: short connections, PID/port reuse, or rapid process exits
can defeat attribution. A process name can be null when the PID is inaccessible.
PID 0 is treated as unavailable. This tool does not map packets to browser tabs or
call stacks. ETW or application instrumentation is needed for stronger correlation.

## Use-case hints and the Google 429 investigation

The CLI reports visible DNS question names, TLS SNI (including when TShark extracts
it from QUIC), and plaintext HTTP Host. A hostname observed on a TCP/UDP stream is
carried to later packets for up to five minutes, with its evidence labelled
`Earlier ... on this stream`. At most 10,000 hostname entries are retained; reaching
that bound clears the cache. There is no speculative DNS-IP ownership mapping or
active reverse-DNS lookup.

DNS names describe a lookup, not a proven subsequent connection. TLS SNI can be an
ECH public/cover name; encrypted handshakes, captures started late, multiplexing,
connection coalescing and QUIC migration limit hostname interpretation. `--host`
is an output filter: it omits packets before the hostname is visible and traffic
whose hostname cannot be determined. It does not retrospectively print packets.

`www.google.com` alone is labelled **Google service (Search vs reCAPTCHA unknown)**.
A visible HTTP `/search` path supports a Search label; `/sorry/` supports an
unusual-traffic label. reCAPTCHA-specific hosts/paths support a reCAPTCHA service
hint. Other labels identify DNS, TLS, QUIC, or port 443 candidates. `--label` is
your own experiment label, saved separately as `userLabel`, not inferred evidence.

**Ordinary HTTPS capture does not reveal the HTTP 429, URL path, cookies, or Google's
reason for refusing a request.** `httpStatus` is populated only when TShark can
actually decode HTTP/1.x. HTTP/2 and HTTP/3 status extraction and explicit TLS key
configuration are outside this small tool. Pair its packet timestamps with
Broiler's existing request diagnostics to investigate the Search refusal. The
program neither changes TLS connections nor acts as an interception proxy.

## Output and capture scope

JSON Lines contain UTC capture time, frame length, transport/protocol, source and
destination endpoints, TShark stream ID, observed hostname and evidence, use-case
hint, user label, visible HTTP status, snapshot time, and candidate owners with
PID/name/packet side/matching evidence. Frame length includes captured link-layer
overhead; it is not application-byte accounting.

Only `-f`/`--filter` reduces capture at the Npcap level. Process/PID/hostname filters
run after decoding. Capture is non-promiscuous and supports a single interface per
run. Non-TCP/UDP, mixed IP tunnel layers, and packets without usable transport
endpoints are counted as skipped. Nested layers of the same IP version are not
fully supported either. At high traffic rates, console/file output or dissection
can lag and drop packets; this is a diagnostic CLI, not a lossless traffic meter.

Output contains metadata, not payloads, cookie values, or URL query strings. IPs,
DNS names, SNI, HTTP Host and process names remain identifiable metadata. TShark may
create temporary packet files internally; forced interruption can leave those
behind. No persistent pcap is requested by this CLI. Existing Wireshark preferences
still apply to decoding.

## Validation

```powershell
dotnet test Broiler.Traffic.Tests.slnx -c Release
pwsh -NoProfile -File scripts/test-traffic-capture.ps1
pwsh -NoProfile -File scripts/update-solutions.ps1 -Verify
```

The unit suite covers native row decoding, IPv4/IPv6 socket ownership, inbound and
outbound TCP, shared UDP ports, wildcard safeguards, stale timestamps, filtering,
and service-hint limitations. The optional Windows/Npcap smoke test opens only
local loopback fixtures, captures their exact ports, and checks IPv4/IPv6 TCP/UDP
PID attribution plus a fixture HTTP 429, label and query omission. It does not
contact Google. Evidence is written to a fresh ignored `artifacts/traffic-smoke-*`
directory.

## References

- [TShark capture and field-output options](https://www.wireshark.org/docs/man-pages/tshark.html)
- [Windows GetExtendedTcpTable](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable)
- [Windows IPv6 TCP owner row](https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcp6row_owner_pid)
- [Windows IPv6 UDP owner row](https://learn.microsoft.com/en-us/windows/win32/api/udpmib/ns-udpmib-mib_udp6row_owner_pid)
