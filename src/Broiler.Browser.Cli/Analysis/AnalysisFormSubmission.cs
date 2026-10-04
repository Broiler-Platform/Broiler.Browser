using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.Net.Http;

namespace Broiler.Cli.Analysis;

/// <summary>An explicitly selected, as-fetched form, submitted on the same diagnostic profile.</summary>
internal static class AnalysisFormSubmission
{
    public static PageRequest Build(PageLoadResult response, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (response.StatusCode is < 200 or >= 300)
            throw new InvalidOperationException($"Cannot submit a form from HTTP {response.StatusCode}.");

        var request = new HtmlFormState().TryBuildScriptSubmitRequest(response.Html, index, response.FinalUrl)
            ?? throw new InvalidOperationException($"The fetched document has no submittable form at index {index}.");
        var document = DocumentRequestContext.CreateTopLevel(new Uri(response.FinalUrl));
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeFile)
            || !BrowserApp.MayNavigateTo(document, request.Url))
            throw new InvalidOperationException("The form target is blocked by the browser's navigation policy.");

        return request with { Initiator = document, NavigationType = PageNavigationType.FormSubmission };
    }
}
