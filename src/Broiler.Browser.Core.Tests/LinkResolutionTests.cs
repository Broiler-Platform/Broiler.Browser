namespace Broiler.Browser.Core.Tests;

/// <summary>
/// A link with a URL scheme is absolute; one without is the page's to resolve. .NET's <see cref="System.Uri"/>
/// takes a root-relative <c>/next</c> for a rooted file path on Unix (and <c>//host/a</c> for a UNC one), so on
/// Linux every such link stayed unresolved and was then refused as a page opening a local file. Windows never
/// showed it; the Linux CI legs of the window's click tests did.
/// </summary>
public class LinkResolutionTests
{
    [Theory]
    [InlineData("https://example.test/a", true)]
    [InlineData("http://127.0.0.1:8080/next", true)]
    [InlineData("javascript:void(0)", true)]
    [InlineData("mailto:someone@example.test", true)]
    [InlineData("file:///tmp/page.html", true)]
    [InlineData("/next", false)]
    [InlineData("//example.test/a", false)]
    [InlineData("next", false)]
    [InlineData("../next?x=1", false)]
    [InlineData("#top", false)]
    [InlineData("?q=1", false)]
    [InlineData(":nothing", false)]
    [InlineData("1abc:x", false)]
    public void Only_A_Link_With_A_Scheme_Is_Absolute(string link, bool absolute) =>
        Assert.Equal(absolute, BrowserApp.HasUrlScheme(link));
}
