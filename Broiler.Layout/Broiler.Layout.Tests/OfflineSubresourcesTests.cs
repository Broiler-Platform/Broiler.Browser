using Broiler.Layout.Engine;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the two properties <see cref="OfflineSubresources"/> is only useful if it has: that it
/// decides nothing until a host installs a policy, and that when one is installed the engine asks
/// it about the URL rather than about anything of its own.
/// </summary>
/// <remarks>
/// The first is the load-bearing one. Every host but the conformance runner leaves this unset, and
/// the load sites that consult it (<c>CssBoxImage</c>, <c>CssBox.Background</c>) sit on the render
/// path of a real browser — so "unset means the engine loads exactly what it always did" is the
/// property that keeps this lever from being a rendering change.
/// </remarks>
public sealed class OfflineSubresourcesTests : IDisposable
{
    private readonly Func<string?, bool>? _previous = OfflineSubresources.FetchPolicy;

    public void Dispose() => OfflineSubresources.FetchPolicy = _previous;

    [Theory]
    [InlineData("http://example.com/x.png")]
    [InlineData("https://software.hixie.ch/delayed-file?pause=8")]
    [InlineData("support/pattern.png")]
    [InlineData("")]
    [InlineData(null)]
    public void DeniesNothingWithNoPolicyInstalled(string? url)
    {
        OfflineSubresources.FetchPolicy = null;
        Assert.False(OfflineSubresources.Denies(url), url ?? "(null)");
    }

    [Fact(Timeout = 600000)]
    public void DeniesExactlyWhatThePolicyDeclines()
    {
        OfflineSubresources.FetchPolicy = url => url != "http://example.com/x.png";

        Assert.True(OfflineSubresources.Denies("http://example.com/x.png"));
        Assert.False(OfflineSubresources.Denies("http://example.com/y.png"));

        // The URL is what is asked about, nulls included: a box with no src still reaches the
        // load site, and a policy that wants to answer for it must be the one that decides.
        OfflineSubresources.FetchPolicy = url => url is not null;
        Assert.True(OfflineSubresources.Denies(null));
    }
}
