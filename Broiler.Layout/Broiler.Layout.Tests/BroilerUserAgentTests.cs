using System.Net.Http;
using Broiler.Layout.Net;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The one string every Broiler loader identifies itself with, and the helper that puts it on a
/// client.
/// </summary>
/// <remarks>
/// <para>
/// The bug this exists for is an <em>absent</em> header rather than a wrong one: <c>HttpClient</c>
/// sends no <c>User-Agent</c> at all by default, and Wikimedia's policy answers a request carrying
/// none with <c>403 Forbidden</c> before anything else about it matters. So the cases worth pinning
/// are that the value is not empty and that <see cref="BroilerUserAgent.Apply"/> actually lands it
/// on the wire — not the exact wording, which sites are free to disagree about.
/// </para>
/// <para>
/// Asserted through a handler rather than off <c>DefaultRequestHeaders</c>, because
/// <c>User-Agent</c> is a <em>typed</em> header: <c>TryGetValues</c> hands back the three product
/// tokens (<c>Mozilla/5.0</c>, <c>(Windows NT 10.0; Win64; x64)</c>, <c>Broiler/1.0</c>) that .NET
/// re-joins with spaces on the way out. Comparing against the collection would test that split, not
/// the header a server reads.
/// </para>
/// </remarks>
public sealed class BroilerUserAgentTests
{
    [Fact(Timeout = 600000)]
    public void The_Value_Is_A_Non_Empty_Token_Naming_The_Product()
    {
        Assert.False(string.IsNullOrWhiteSpace(BroilerUserAgent.Value));
        Assert.Contains("Broiler", BroilerUserAgent.Value);
    }

    [Fact(Timeout = 600000)]
    public void Apply_Returns_The_Client_It_Identified()
    {
        using var client = new HttpClient();

        Assert.Same(client, BroilerUserAgent.Apply(client));
    }

    [Fact(Timeout = 600000)]
    public async Task An_Applied_Client_Puts_The_Value_On_The_Wire()
    {
        Assert.Equal(BroilerUserAgent.Value, await SentUserAgentAsync(chosen: null));
    }

    /// <summary>
    /// A default, not an override. A caller that has already chosen a <c>User-Agent</c> for a
    /// request keeps it — which is what <c>fetch()</c> with an explicit header in its init relies on.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task A_Request_That_Names_Its_Own_User_Agent_Keeps_It()
    {
        Assert.Equal("Chosen/9", await SentUserAgentAsync(chosen: "Chosen/9"));
    }

    /// <summary>
    /// The <c>User-Agent</c> an identified client actually sends for one request, joined as the
    /// header is written. Empty string when none was sent, so the original defect — no header at
    /// all — reads as a failed comparison rather than an absent one.
    /// </summary>
    private static async Task<string> SentUserAgentAsync(string? chosen)
    {
        using var handler = new CapturingHandler();
        using var client = BroilerUserAgent.Apply(new HttpClient(handler));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/");
        if (chosen is not null)
            request.Headers.TryAddWithoutValidation("User-Agent", chosen);

        using var response = await client.SendAsync(request);
        return handler.SeenUserAgent;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        internal string SeenUserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            SeenUserAgent = request.Headers.TryGetValues("User-Agent", out var values)
                ? string.Join(" ", values)
                : string.Empty;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
