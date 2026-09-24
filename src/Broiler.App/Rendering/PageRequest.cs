using Broiler.Net.Cookies;
using Broiler.Net.Http;

namespace Broiler.App.Rendering;

/// <summary>
/// A navigation: the URL to load and, for a form submission that cannot be expressed
/// as a URL, the request to make for it.
/// </summary>
/// <remarks>
/// <para>
/// Navigation used to be a bare URL string, which is all a link or a
/// <c>method="get"</c> form needs — a GET form puts its fields in the query. A
/// <c>method="post"</c> form has nowhere to put them, so it needs a request body, and
/// this is what carries it.
/// </para>
/// <para>
/// <b>Who asked, and how.</b> <see cref="Initiator"/> and <see cref="NavigationType"/> say where the
/// navigation came from, because the profile's transport decides from them which cookies the request
/// may carry: a navigation a cross-site document started does not get the target's
/// <c>SameSite=Strict</c> cookies, while one the user typed does. They are filled in by the browser
/// from its own state — the document on screen, the bridge's record of which document's script asked —
/// and never from anything a page can write.
/// </para>
/// </remarks>
/// <param name="Url">Target URL. May lack a scheme; the loader normalises it.</param>
/// <param name="Method">HTTP method. <c>GET</c> unless a form submits otherwise.</param>
/// <param name="ContentType">Body media type, when <paramref name="Body"/> is set.</param>
/// <param name="Body">Encoded request body, or <c>null</c> for a request without one.</param>
public sealed record PageRequest(
    string Url,
    string Method = PageRequest.Get,
    string? ContentType = null,
    string? Body = null)
{
    /// <summary>
    /// Body bytes, for an encoding that is not text — a <c>multipart/form-data</c>
    /// submission carrying a file. Takes precedence over <see cref="Body"/>.
    /// </summary>
    /// <remarks>
    /// A file part is arbitrary binary, and round-tripping it through a UTF-8 string
    /// would corrupt anything that is not valid UTF-8, so multipart is built as bytes
    /// and travels as bytes.
    /// </remarks>
    public byte[]? BinaryBody { get; init; }

    /// <summary>
    /// The document that started this navigation, or <see langword="null"/> when the browser's own
    /// UI did: the address bar, a favorite, the initial URL.
    /// </summary>
    /// <remarks>
    /// It becomes the client of the top-level navigation request
    /// (<see cref="RequestContext.TopLevelNavigation"/>), which is what the request's same-site status
    /// is computed from. A history entry keeps it, so going back to a page a cross-site document
    /// linked to is still that document's navigation.
    /// </remarks>
    public DocumentRequestContext? Initiator { get; init; }

    /// <summary>How this navigation was started. <see cref="PageNavigationType.Typed"/> unless set.</summary>
    public PageNavigationType NavigationType { get; init; }

    /// <summary>
    /// The same-site status the transport reported for the document this request loaded
    /// (<see cref="TransportResponse.SameSite"/>), or <see langword="null"/> before it has loaded.
    /// </summary>
    /// <remarks>
    /// Kept on the history entry for one reason: a reload from the browser's UI is same-site exactly
    /// when the navigation that loaded the reloaded document was (RFC 6265bis, section 5.2), and it
    /// has no initiator of its own to decide that from.
    /// </remarks>
    public SameSiteStatus? RecordedSameSite { get; init; }

    /// <summary>Whether this request has anything to send.</summary>
    public bool HasBody => BinaryBody is not null || Body is not null;

    public const string Get = "GET";

    public const string Post = "POST";

    /// <summary>The media type a form submits with unless it asks for another.</summary>
    public const string FormUrlEncoded = "application/x-www-form-urlencoded";

    /// <summary>A plain navigation to <paramref name="url"/>.</summary>
    public static PageRequest ForUrl(string url) => new(url);

    /// <summary>Whether this is a request the browser can repeat safely (back, forward, reload).</summary>
    public bool IsRepeatable => string.Equals(Method, Get, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The request that names the document <paramref name="result"/> loaded for this one: what a
    /// history entry holds, so reload and back/forward re-issue the document actually shown.
    /// </summary>
    /// <remarks>
    /// It is at the final URL, not the one first asked for, and it records the response's same-site
    /// status for a later reload. A redirect that turned the request into a <c>GET</c> — a
    /// <c>303</c> after a form <c>POST</c>, the usual post/redirect/get — leaves a plain <c>GET</c>,
    /// so reloading the result does not submit the form again; a request that is still a
    /// <c>POST</c> keeps its body. Who started it is kept either way.
    /// </remarks>
    public PageRequest ForLoadedDocument(PageLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        PageRequest loaded = string.Equals(result.Method, Method, StringComparison.OrdinalIgnoreCase)
            ? this with { Url = result.FinalUrl }
            : new PageRequest(result.FinalUrl, result.Method)
            {
                Initiator = Initiator,
                NavigationType = NavigationType,
            };

        return loaded with { RecordedSameSite = result.SameSite };
    }
}

/// <summary>
/// How a navigation was started. The transport cares about who started it
/// (<see cref="PageRequest.Initiator"/>); this says how, which decides the rest: a UI reload replays
/// the reloaded document's recorded same-site status, and the browser keeps or rewrites history by it.
/// </summary>
public enum PageNavigationType
{
    /// <summary>A URL typed into the address bar, or given on the command line.</summary>
    Typed,

    /// <summary>A favorite.</summary>
    Bookmark,

    /// <summary>A link the user followed in the page.</summary>
    Link,

    /// <summary>A form the user submitted, or a page's <c>form.submit()</c>.</summary>
    FormSubmission,

    /// <summary>A page's script: <c>location</c> assignment, <c>replace</c> or <c>reload()</c>.</summary>
    Script,

    /// <summary>A <c>&lt;meta http-equiv="refresh"&gt;</c>.</summary>
    MetaRefresh,

    /// <summary>The browser's reload command (the button, F5).</summary>
    Reload,

    /// <summary>The browser's back or forward command.</summary>
    BackForward,
}
