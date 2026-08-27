using System.Net.Http;

namespace Broiler.Layout.Net;

/// <summary>
/// The product token Broiler puts in the HTTP <c>User-Agent</c> request header, and the one
/// <c>navigator.userAgent</c> reports to script — one string, so the engine tells the network and
/// the page the same thing about itself.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HttpClient"/> sends <em>no</em> <c>User-Agent</c> at all unless one is configured, and
/// a missing header is not the same as an unrecognised one: Wikimedia's User-Agent policy rejects a
/// request that carries none with <c>403 Forbidden</c> before it looks at anything else, so
/// <c>https://www.mediawiki.org/wiki/MediaWiki</c> failed instantly for every Broiler entry point —
/// the CLI capture, the browser window, and every sub-resource each of them went on to request. Any
/// non-empty token is accepted there; what the policy will not have is silence.
/// </para>
/// <para>
/// It lives in <c>Broiler.Layout</c> because that is the one assembly every requesting layer already
/// references — <c>Broiler.Cli</c> and <c>Broiler.Browser.Core</c> reach it through
/// <c>Broiler.HtmlBridge.Dom</c>, and the <c>Broiler.HTML</c> submodule's own loaders (stylesheets,
/// images, web fonts) reference it directly. A constant duplicated per requesting assembly is a
/// constant that drifts, and the header drifting away from <c>navigator.userAgent</c> is exactly the
/// inconsistency a site sniffing both would catch.
/// </para>
/// <para>
/// Unrelated to <c>CssUserAgentDefaults</c> despite the shared words: that is the CSS <em>user-agent
/// stylesheet</em>, the engine's built-in presentation defaults. This is the network identity, which
/// is why it sits under a <c>Net</c> namespace.
/// </para>
/// </remarks>
public static class BroilerUserAgent
{
    /// <summary>
    /// The full <c>User-Agent</c> value. The <c>Mozilla/5.0</c> prefix is not a claim to be Gecko —
    /// it is the compatibility token every engine has carried since Netscape, and a server sniffing
    /// for it is the reason to keep it. <c>Broiler/1.0</c> is the part that actually names the
    /// product.
    /// </summary>
    public const string Value = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Broiler/1.0";

    /// <summary>
    /// Makes <see cref="Value"/> the default <c>User-Agent</c> of <paramref name="client"/> and
    /// returns it, so a client can be identified where it is constructed:
    /// <c>BroilerUserAgent.Apply(new HttpClient { Timeout = … })</c>.
    /// </summary>
    /// <remarks>
    /// Added without validation because the value is a compile-time constant that has already been
    /// checked, and because a parse failure here would cost the request the header it is being given
    /// — the failure this exists to prevent. It is a <em>default</em>: a request that carries its own
    /// <c>User-Agent</c> (a <c>fetch()</c> whose init named one) keeps it, which is the precedence
    /// <see cref="HttpClient.DefaultRequestHeaders"/> already defines.
    /// </remarks>
    /// <param name="client">The client to identify.</param>
    /// <returns><paramref name="client"/>, for chaining at a field initialiser.</returns>
    public static HttpClient Apply(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Value);
        return client;
    }
}
