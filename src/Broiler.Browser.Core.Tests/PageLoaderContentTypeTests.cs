using System.Net;
using System.Net.Http;
using Broiler.App.Rendering;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// A form submission must reach the origin under the media type the form chose, and under that
/// one alone.
/// </summary>
/// <remarks>
/// <see cref="StringContent"/>'s constructor sets <c>text/plain; charset=utf-8</c>, and
/// <c>TryAddWithoutValidation</c> appends rather than replaces, so the loader used to post the
/// two-valued <c>Content-Type: text/plain; charset=utf-8, application/x-www-form-urlencoded</c>.
/// A server reading the first value sees <c>text/plain</c> and never decodes the body: google's
/// cookie-consent dialog answered <c>POST https://consent.google.de/save</c> with
/// <c>400 Bad Request</c>, so accepting or declining cookies stranded the search behind the
/// consent page.
/// </remarks>
public class PageLoaderContentTypeTests
{
    /// <summary>Captures the request instead of sending it, so the tests touch no socket.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        internal HttpRequestMessage? Request { get; private set; }

        internal string? Body { get; private set; }

        internal byte[]? Bytes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;

            // The loader disposes the content once the send returns, so the body has to be
            // taken here rather than off the captured request.
            if (request.Content is { } content)
            {
                Bytes = await content.ReadAsByteArrayAsync(cancellationToken);
                Body = await content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>ok</body></html>"),
                RequestMessage = request,
            };
        }
    }

    private static async Task<CapturingHandler> SubmitAsync(PageRequest request)
    {
        CapturingHandler handler = new();
        using HttpClient client = new(handler);
        using PageLoader loader = new(client);

        _ = await loader.FetchAsync(request);
        return handler;
    }

    private static string[] ContentTypeValues(CapturingHandler handler) =>
        handler.Request!.Content!.Headers.GetValues("Content-Type").ToArray();

    [Fact(Timeout = 600000)]
    public async Task UrlEncodedForm_PostsThatMediaTypeAndNoOther()
    {
        // The consent dialog's own body, shortened.
        const string Body = "bl=boq_identityfrontenduiserver&gl=DE&continue=https%3A%2F%2Fwww.google.de%2F";

        var handler = await SubmitAsync(new PageRequest(
            "https://consent.google.de/save",
            PageRequest.Post,
            PageRequest.FormUrlEncoded,
            Body));

        // The regression is a second value here — the origin reads the first and sees text/plain.
        Assert.Equal(
            ["application/x-www-form-urlencoded"],
            ContentTypeValues(handler));
        Assert.Equal(Body, handler.Body);
    }

    [Fact(Timeout = 600000)]
    public async Task ContentTypeIsAbsentFromTheDefaultedCase()
    {
        // No media type on the request: the loader falls back to the form default, and that
        // fallback must replace StringContent's text/plain just the same.
        var handler = await SubmitAsync(new PageRequest(
            "https://consent.google.de/save",
            PageRequest.Post,
            ContentType: null,
            Body: "q=test"));

        Assert.Equal(["application/x-www-form-urlencoded"], ContentTypeValues(handler));
    }

    [Fact(Timeout = 600000)]
    public async Task TextPlainForm_KeepsTheMediaTypeTheFormChose()
    {
        var handler = await SubmitAsync(new PageRequest(
            "https://example.com/submit",
            PageRequest.Post,
            "text/plain",
            "q=test\r\n"));

        Assert.Equal(["text/plain"], ContentTypeValues(handler));
    }

    [Fact(Timeout = 600000)]
    public async Task MultipartForm_KeepsItsBoundaryParameterAndItsBytes()
    {
        byte[] body = [0x2D, 0x2D, 0x78, 0x79, 0x7A, 0xFF, 0xFE, 0x0D, 0x0A];

        var handler = await SubmitAsync(new PageRequest(
            "https://example.com/upload",
            PageRequest.Post,
            "multipart/form-data; boundary=xyz")
        {
            BinaryBody = body,
        });

        Assert.Equal(["multipart/form-data; boundary=xyz"], ContentTypeValues(handler));

        // A file part is arbitrary binary; it must not be re-encoded as text.
        Assert.Equal(body, handler.Bytes);
    }

    [Fact(Timeout = 600000)]
    public async Task AnUnparseableMediaTypeStillGoesOutRatherThanFailingTheRequest()
    {
        var handler = await SubmitAsync(new PageRequest(
            "https://example.com/submit",
            PageRequest.Post,
            "not a media type",
            "q=test"));

        Assert.Equal(["not a media type"], ContentTypeValues(handler));
    }

    [Fact(Timeout = 600000)]
    public async Task AGetNavigationSendsNoBodyAndNoContentType()
    {
        var handler = await SubmitAsync(PageRequest.ForUrl("https://www.google.de/search?q=test"));

        Assert.Null(handler.Request!.Content);
    }
}
