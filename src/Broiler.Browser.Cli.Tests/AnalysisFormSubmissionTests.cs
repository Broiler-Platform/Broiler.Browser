using Broiler.App.Rendering;
using Broiler.Cli.Analysis;

namespace Broiler.Cli.Tests;

public sealed class AnalysisFormSubmissionTests
{
    private static PageLoadResult Page(string html, int status = 200) => new()
    {
        FinalUrl = "https://consent.example.test/forms/page",
        Html = html,
        StatusCode = status,
    };

    [Fact]
    public void Selected_Form_Uses_Default_Fields_And_The_Final_Document_As_Initiator()
    {
        var response = Page("""
            <form action="/accept" method="post"><input name="choice" value="accept"></form>
            <form action="save" method="post">
              <input type="hidden" name="choice" value="reject">
              <input type="hidden" name="continue" value="https://example.test/search?q=test&amp;x=1">
              <input type="submit" name="button" value="unused">
            </form>
            """);

        var request = AnalysisFormSubmission.Build(response, 1);

        Assert.Equal("https://consent.example.test/forms/save", request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Equal(PageRequest.FormUrlEncoded, request.ContentType);
        Assert.Contains("choice=reject", request.Body);
        Assert.Contains("continue=https%3A%2F%2Fexample.test%2Fsearch%3Fq%3Dtest%26x%3D1", request.Body);
        Assert.DoesNotContain("button", request.Body);
        Assert.Equal(PageNavigationType.FormSubmission, request.NavigationType);
        Assert.Equal(response.FinalUrl, request.Initiator?.DocumentUrl.AbsoluteUri);
    }

    [Fact]
    public void Missing_Form_And_Error_Page_Are_Not_Submitted()
    {
        Assert.Throws<InvalidOperationException>(() => AnalysisFormSubmission.Build(Page("<p>none</p>"), 0));
        Assert.Throws<InvalidOperationException>(() => AnalysisFormSubmission.Build(Page("<form action='/retry'></form>", 429), 0));
    }

    [Theory]
    [InlineData("file:///C:/private.html")]
    [InlineData("javascript:alert(1)")]
    public void Web_Form_Cannot_Navigate_To_A_Local_File_Or_Active_Url(string target)
    {
        Assert.Throws<InvalidOperationException>(() => AnalysisFormSubmission.Build(Page($"<form action='{target}'></form>"), 0));
    }
}
