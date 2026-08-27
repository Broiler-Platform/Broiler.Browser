namespace Broiler.App.Rendering;

/// <summary>
/// A navigation: the URL to load and, for a form submission that cannot be expressed
/// as a URL, the request to make for it.
/// </summary>
/// <remarks>
/// Navigation used to be a bare URL string, which is all a link or a
/// <c>method="get"</c> form needs — a GET form puts its fields in the query. A
/// <c>method="post"</c> form has nowhere to put them, so it needs a request body, and
/// this is what carries it.
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
}
