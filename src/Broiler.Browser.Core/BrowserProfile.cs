using Broiler.App;
using Broiler.Net.Cookies;
using Broiler.Net.Http;

namespace Broiler.Browser;

/// <summary>
/// One browsing profile: the cookie store, the network session every request of the profile goes
/// through, and where the profile keeps what it persists.
/// </summary>
/// <remarks>
/// <para>
/// <b>One network for everything.</b> <see cref="Network"/> serves the page navigations, the scripts
/// the extractor fetches, the DOM bridge's loaders (module imports, inserted scripts, stylesheets,
/// frames, <c>fetch()</c>, XHR, <c>sendBeacon</c>) and the renderer's images, stylesheets and fonts.
/// It owns the <c>Cookie</c> header and follows redirects itself, deciding per hop which cookies to send
/// and storing every <c>Set-Cookie</c> in <see cref="Cookies"/>. A cookie a navigation receives is
/// therefore what the page's sub-resources carry, and nothing else in the browser keeps a jar of its
/// own. <c>document.cookie</c> reads the same store through <see cref="DocumentCookies"/>, which never
/// exposes an <c>HttpOnly</c> cookie.
/// </para>
/// <para>
/// <b>Owned by the composition root.</b> A head creates one profile for the process (Windows, Linux) or
/// for the application process across activities (Android), hands it to each
/// <see cref="BrowserApp"/>, and disposes it after the last one. There is no process-global profile: a
/// <see cref="BrowserApp"/> built without one creates a private ephemeral profile for itself.
/// </para>
/// <para>
/// <b>Nothing is persisted yet</b> apart from favorites; cookies live as long as the profile.
/// </para>
/// </remarks>
internal sealed class BrowserProfile : IDisposable
{
    /// <summary>
    /// A browser window can stay open for days; recycling a pooled connection periodically keeps it
    /// from pinning a DNS answer for that long.
    /// </summary>
    internal static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A host that never completes the handshake otherwise holds the navigation for the whole request
    /// timeout with nothing to show for it.
    /// </summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private BrowserProfile(string? favoritesPath, HttpMessageHandler? handler)
    {
        Cookies = new CookieStore();

        // One pooled connection handler for the profile rather than one per navigation: the
        // session is the connection pool, and tearing a pool down while its scavenger has armed a
        // zero-byte read-ahead fails that read with SocketError.OperationAborted
        // (docs/browser-connection-pool-aborts.md). The User-Agent is Broiler.Net's product token;
        // a server whose policy rejects an unidentified request answers the navigation itself
        // (mediawiki.org replies 403 Forbidden).
        Network = new BrowserNetworkSession(new BrowserNetworkSessionOptions
        {
            Cookies = Cookies,
            UserAgent = BroilerUserAgent.Value,
            PooledConnectionLifetime = PooledConnectionLifetime,
            ConnectTimeout = ConnectTimeout,
            Handler = handler,
        });
        FavoritesPath = favoritesPath;
    }

    /// <summary>
    /// The profile's cookie store. Administrative: its snapshots include <c>HttpOnly</c> values, so
    /// it is never handed to page script or anything that serves it.
    /// </summary>
    public CookieStore Cookies { get; }

    /// <summary>The profile's network session, over <see cref="Cookies"/>.</summary>
    public BrowserNetworkSession Network { get; }

    /// <summary>The <c>document.cookie</c> view of <see cref="Cookies"/>, for script bindings.</summary>
    public IDocumentCookieAccess DocumentCookies => Network;

    /// <summary>
    /// The file favorites are kept in, or <see langword="null"/> for a profile that keeps them in
    /// memory only.
    /// </summary>
    public string? FavoritesPath { get; }

    /// <summary>The user's profile: favorites in <see cref="FavoritesManager.DefaultFilePath"/>.</summary>
    public static BrowserProfile CreateDefault() => new(FavoritesManager.DefaultFilePath, handler: null);

    /// <summary>
    /// A profile that writes nothing to disk and forgets everything when disposed: its cookies are in
    /// memory and its favorites start empty and are never saved. For tests, and for a
    /// <see cref="BrowserApp"/> constructed without a profile.
    /// </summary>
    public static BrowserProfile CreateEphemeral() => new(favoritesPath: null, handler: null);

    /// <summary>
    /// An ephemeral profile whose network sends through <paramref name="handler"/> instead of the
    /// sockets handler — a test seam. The handler must not manage cookies or follow redirects.
    /// </summary>
    internal static BrowserProfile CreateEphemeral(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new(favoritesPath: null, handler);
    }

    public void Dispose() => Network.Dispose();
}
