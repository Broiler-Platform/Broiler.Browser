using System.Globalization;
using System.Net;

namespace Broiler.Traffic;

internal sealed record Packet(DateTimeOffset Time, int Bytes, string Transport,
    string Source, int SourcePort, string Destination, int DestinationPort,
    string Protocol, string? DnsQuery, string? Sni, string? HttpHost,
    string? RequestPath, int? HttpStatus, string? Stream)
{
    // Explicit fields avoid parsing Wireshark's human-readable Info column or saving payloads.
    internal static readonly string[] Fields = ["frame.time_epoch", "frame.len", "ip.src", "ip.dst",
        "ipv6.src", "ipv6.dst", "tcp.srcport", "tcp.dstport", "udp.srcport", "udp.dstport",
        "_ws.col.Protocol", "dns.qry.name", "tls.handshake.extensions_server_name", "http.host",
        "http.request.uri", "http.response.code", "tcp.stream", "udp.stream"];

    internal static Packet? Parse(string line)
    {
        var f = line.Split('\t');
        if (f.Length != Fields.Length ||
            !decimal.TryParse(f[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var epoch) ||
            epoch < 0 || epoch > 253402300799 || !int.TryParse(f[1], out int bytes)) return null;
        // Mixed/tunnel layers cannot be paired reliably with occurrence=f. Leave them out.
        if (f[2].Length > 0 && f[4].Length > 0) return null;
        string source = f[2].Length > 0 ? f[2] : f[4];
        string destination = f[3].Length > 0 ? f[3] : f[5];
        if (!IPAddress.TryParse(source, out var src) || !IPAddress.TryParse(destination, out var dst)) return null;
        bool tcp = f[6].Length > 0;
        if (tcp && f[8].Length > 0) return null;
        if (!int.TryParse(f[tcp ? 6 : 8], out int sp) || !int.TryParse(f[tcp ? 7 : 9], out int dp)) return null;
        string? Nonempty(int n) => f[n].Length == 0 ? null : f[n];
        var uri = Nonempty(14);
        // Only retain the path for classification; never keep search terms or URL query tokens.
        var path = uri is null ? null : uri.Split('?', '#')[0];
        if (uri is not null && Uri.TryCreate(uri, UriKind.Absolute, out var absolute)) path = absolute.AbsolutePath;
        return new(DateTimeOffset.UnixEpoch.AddTicks((long)(epoch * TimeSpan.TicksPerSecond)), bytes,
            tcp ? "TCP" : "UDP", src.ToString(), sp, dst.ToString(), dp, f[10], Nonempty(11),
            Nonempty(12), Nonempty(13), path, int.TryParse(f[15], out int status) ? status : null,
            Nonempty(tcp ? 16 : 17));
    }
}

internal sealed record TrafficHint(string? Host, string Evidence, string UseCase);

internal sealed class TrafficClassifier
{
    private readonly Dictionary<string, (DateTimeOffset Time, string Host, string Evidence)> hosts = [];

    internal TrafficHint Classify(Packet p)
    {
        string? host = NormalizeHost(p.HttpHost ?? p.Sni);
        string evidence = p.HttpHost is not null ? "HTTP Host" : "TLS SNI (may be an ECH public name)";
        string? key = p.Stream is null ? null : p.Transport + ":" + p.Stream;
        if (host is not null && key is not null)
        {
            if (hosts.Count >= 10000) hosts.Clear();
            hosts[key] = (p.Time, host, evidence);
        }
        else if (key is not null && hosts.TryGetValue(key, out var prior) &&
                 p.Time >= prior.Time && p.Time - prior.Time < TimeSpan.FromMinutes(5))
        {
            host = prior.Host;
            evidence = "Earlier " + prior.Evidence + " on this stream";
        }

        if (p.DnsQuery is not null)
            return new(NormalizeHost(p.DnsQuery), "DNS question (not proof of a subsequent connection)", "DNS lookup");
        string useCase;
        if (IsDomain(host, "google.com") && p.RequestPath is "/search") useCase = "Google Search (visible HTTP path)";
        else if (IsDomain(host, "google.com") && p.RequestPath?.StartsWith("/sorry/", StringComparison.Ordinal) == true)
            useCase = "Google unusual-traffic page (visible HTTP path)";
        else if (IsDomain(host, "recaptcha.net") || IsDomain(host, "recaptcha.google.com") ||
                 IsDomain(host, "google.com") && p.RequestPath?.StartsWith("/recaptcha/", StringComparison.Ordinal) == true)
            useCase = "reCAPTCHA service (host/path hint)";
        else if (IsDomain(host, "google.com")) useCase = "Google service (Search vs reCAPTCHA unknown)";
        else if (IsDomain(host, "gstatic.com") || IsDomain(host, "googleapis.com")) useCase = "Google resource/API (host hint)";
        else if (p.Protocol.Contains("QUIC", StringComparison.OrdinalIgnoreCase)) useCase = "QUIC (often HTTP/3)";
        else if (p.Protocol.Contains("TLS", StringComparison.OrdinalIgnoreCase)) useCase = "TLS encrypted traffic";
        else if (p.SourcePort == 443 || p.DestinationPort == 443) useCase = "Port 443 (HTTPS/QUIC candidate)";
        else if (p.Protocol.Contains("HTTP", StringComparison.OrdinalIgnoreCase)) useCase = "HTTP";
        else useCase = p.Transport + " traffic";
        return new(host, host is null ? "No visible hostname" : evidence, useCase);
    }

    internal static bool IsDomain(string? host, string domain) => host is not null &&
        (host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // HTTP Host can include a port. IPv6 literals are not service/domain hints.
        if (value.StartsWith('[')) return null;
        return value.Split(':')[0].TrimEnd('.').ToLowerInvariant();
    }
}
