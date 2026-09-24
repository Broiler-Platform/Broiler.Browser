namespace Broiler.Cli.Tests;

/// <summary>
/// Where <c>--follow-first-link</c> goes, and where it refuses to.
/// </summary>
/// <remarks>
/// In the capture collection because finding the link parses the page with the renderer, which
/// shares the render path's process-wide caches with any capture running beside it.
/// </remarks>
[Collection(CaptureCollection.Name)]
public sealed class LinkNavigatorTests
{
    [Fact(Timeout = 600000)]
    public void A_Relative_Link_Resolves_Against_The_Landing_Page()
    {
        Assert.Equal(
            "https://example.test/tests/page.html",
            LinkNavigator.ResolveFirstLink(
                "<html><body><a href=\"page.html\">Start</a></body></html>",
                "https://example.test/tests/landing.html"));
    }

    /// <summary>A link within the page — Acid2's <c>#top</c> — is not a navigation.</summary>
    [Fact(Timeout = 600000)]
    public void A_Fragment_Link_Is_Not_Followed()
    {
        Assert.Null(LinkNavigator.ResolveFirstLink(
            "<html><body><a href=\"#top\">Start</a></body></html>",
            "https://example.test/landing.html"));
    }

    [Fact(Timeout = 600000)]
    public void A_Page_Without_Links_Has_Nothing_To_Follow()
    {
        Assert.Null(LinkNavigator.ResolveFirstLink("<html><body>no links</body></html>", "https://example.test/"));
    }

    /// <summary>
    /// A page on the network may not send the capture to a local file, whatever its link says.
    /// </summary>
    /// <remarks>
    /// The file is under the working directory on purpose. The directory check resolves a web page's
    /// path against the working directory's root, so a file on another drive — the temporary directory
    /// on a machine that builds on D: — is refused by that check too, and this test passed with the
    /// rule it is named for taken out. A negative control found that.
    /// </remarks>
    [Fact(Timeout = 600000)]
    public void A_Web_Page_Linking_To_A_Local_File_Is_Not_Followed()
    {
        var local = new Uri(Path.GetFullPath("secret.html")).AbsoluteUri;

        Assert.Null(LinkNavigator.ResolveFirstLink(
            $"<html><body><a href=\"{local}\">Start</a></body></html>",
            "https://example.test/landing.html"));
    }
}
