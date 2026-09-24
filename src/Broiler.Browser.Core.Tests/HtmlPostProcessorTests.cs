using Broiler.Browser;
using Xunit;

namespace Broiler.Browser.Core.Tests;

public class HtmlPostProcessorTests
{
    [Fact]
    public void ProcessForBrowsing_StripsScriptTags()
    {
        const string html = "<div>Hello<script>alert('xss');</script> World</div>";
        var result = HtmlPostProcessor.ProcessForBrowsing(html);
        Assert.Equal("<div>Hello World</div>", result);
    }

    [Fact]
    public void ProcessForBrowsing_StripsNoscriptTagsAndContent()
    {
        const string html = "<p>Main content<noscript><p>JavaScript required</p></noscript></p>";
        var result = HtmlPostProcessor.ProcessForBrowsing(html);
        Assert.Equal("<p>Main content</p>", result);
    }

    [Fact]
    public void ProcessForBrowsing_EmptiesIframeFallbackContent()
    {
        const string html = """<iframe src="frame.html">Your browser does not support iframes</iframe>""";
        var result = HtmlPostProcessor.ProcessForBrowsing(html);
        Assert.Equal("""<iframe src="frame.html"></iframe>""", result);
    }

    [Fact]
    public void StampFormControlIds_AssignsUniqueSequentialSyntheticIds()
    {
        const string html = """
            <form>
                <input type="checkbox" name="c1" />
                <input type="radio" name="r1" value="a">
                <input type="file" name="f1">
                <select name="s1"><option>1</option></select>
            </form>
            """;

        var result = HtmlPostProcessor.StampFormControlIds(html);

        Assert.Contains($"""id="{HtmlPostProcessor.SyntheticIdPrefix}0" /""", result);
        Assert.Contains($"""id="{HtmlPostProcessor.SyntheticIdPrefix}1">""", result);
        Assert.Contains($"""id="{HtmlPostProcessor.SyntheticIdPrefix}2">""", result);
        Assert.Contains($"""id="{HtmlPostProcessor.SyntheticIdPrefix}3">""", result);
    }

    [Fact]
    public void StampFormControlIds_PreservesExistingIds()
    {
        const string html = """<input type="checkbox" id="my-custom-id" name="c1">""";
        var result = HtmlPostProcessor.StampFormControlIds(html);
        Assert.Equal(html, result);
        Assert.DoesNotContain(HtmlPostProcessor.SyntheticIdPrefix, result);
    }

    [Fact]
    public void StampFormControlIds_HandlesNullOrEmpty()
    {
        Assert.Equal(string.Empty, HtmlPostProcessor.StampFormControlIds(string.Empty));
        Assert.Equal(string.Empty, HtmlPostProcessor.StampFormControlIds(null!));
    }

    [Fact]
    public void StripObjectContent_EmptiesObjectFallback()
    {
        const string html = """<object data="test.swf"><p>Fallback</p></object>""";
        var result = HtmlPostProcessor.StripObjectContent(html);
        Assert.Equal("""<object data="test.swf"></object>""", result);
    }
}
