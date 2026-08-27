using System.Net;
using System.Net.Http;

namespace Broiler.Layout.Net;

/// <summary>
/// The HTTP version Broiler's loaders negotiate, and the ALPN token that names it to script —
/// one pair, so <c>PerformanceNavigationTiming.nextHopProtocol</c> reports the protocol the
/// request was actually made over rather than a guess about it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HttpClient"/> defaults to HTTP/1.1 and Broiler has never configured it otherwise, so
/// <see cref="Version"/> only writes down what was already true. It is pinned rather than assumed
/// because the value is now <em>reported</em>: a later change to the request version that forgot
/// this file would leave the engine telling a page one protocol while speaking another, which is
/// precisely the inconsistency <see cref="BroilerUserAgent"/> exists to prevent for the
/// <c>User-Agent</c>.
/// </para>
/// <para>
/// <see cref="NextHopProtocol"/> is the ALPN identifier for that version, as the Resource Timing
/// specification requires — <c>http/1.1</c>, not <c>HTTP/1.1</c>. A resource that was not fetched
/// over a network at all (a <c>file:</c> document, HTML handed to the bridge as a string) has no
/// next hop, and the spec's answer there is the empty string, not this token.
/// </para>
/// </remarks>
public static class BroilerHttpProtocol
{
    /// <summary>The HTTP version Broiler's loaders request.</summary>
    public static readonly Version Version = HttpVersion.Version11;

    /// <summary>
    /// The ALPN token for <see cref="Version"/>, as <c>nextHopProtocol</c> reports it.
    /// </summary>
    public const string NextHopProtocol = "http/1.1";

    /// <summary>
    /// Pins <paramref name="client"/> to <see cref="Version"/> and returns it, so a client can be
    /// configured where it is constructed alongside
    /// <see cref="BroilerUserAgent.Apply(HttpClient)"/>.
    /// </summary>
    /// <param name="client">The client to pin.</param>
    /// <returns><paramref name="client"/>, for chaining at a field initialiser.</returns>
    public static HttpClient Apply(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestVersion = Version;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        return client;
    }
}
