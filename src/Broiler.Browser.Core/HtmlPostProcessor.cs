using System;
using System.Text.RegularExpressions;

namespace Broiler.Browser;

using Regex = System.Text.RegularExpressions.Regex;

/// <summary>
/// Sanitises post-script-execution HTML before it is handed to the
/// rendering surface and assigns synthetic control IDs for UI hosting.
/// </summary>
internal static class HtmlPostProcessor
{
    /// <summary>
    /// Matches all <c>&lt;script …&gt;…&lt;/script&gt;</c> blocks.
    /// </summary>
    private static readonly Regex ScriptTagPattern = new(
        @"<script(?<attrs>[^>]*)>(?<content>[\s\S]*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches all <c>&lt;noscript …&gt;…&lt;/noscript&gt;</c> blocks, including their fallback
    /// content.
    /// </summary>
    private static readonly Regex NoscriptTagPattern = new(
        @"<noscript(?<attrs>[^>]*)>(?<content>[\s\S]*?)</noscript>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches <c>&lt;iframe …&gt;…&lt;/iframe&gt;</c> elements including
    /// their inline fallback content.
    /// </summary>
    private static readonly Regex IframeContentPattern = new(
        @"<iframe(?<attrs>[^>]*)>[\s\S]*?</iframe>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches <c>&lt;object …&gt;…&lt;/object&gt;</c> elements including
    /// their inline fallback content.
    /// </summary>
    private static readonly Regex ObjectContentPattern = new(
        @"<object(?<attrs>[^>]*)>[\s\S]*?</object>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Prefix of the synthetic ids <see cref="StampFormControlIds"/> assigns. Lower
    /// case because the renderer's id lookup folds case.
    /// </summary>
    internal const string SyntheticIdPrefix = "broiler-fc-";

    /// <summary>
    /// Matches an <c>&lt;input&gt;</c> the browser hosts a Broiler.UI control over —
    /// checkbox, radio or file — in either attribute order.
    /// </summary>
    private static readonly Regex HostedInputPattern = new(
        @"<input\b(?<attrs>[^>]*\btype\s*=\s*[""']?(?:checkbox|radio|file)\b[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches a <c>&lt;select&gt;</c> start tag (never the closing tag, which
    /// <c>\b</c> after the name cannot follow a slash into).
    /// </summary>
    private static readonly Regex SelectPattern = new(
        @"<select\b(?<attrs>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IdAttributePattern = new(
        @"\bid\s*=\s*[""']?[^\s""'>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Render-preparation transforms that approximate native browser rendering of replaced or
    /// unsupported elements — stripping already-executed <c>&lt;script&gt;</c>s and emptying
    /// <c>&lt;iframe&gt;</c> fallback.
    /// </summary>
    private static string ApplyReplacedElementPasses(string html)
    {
        html = StripScriptTags(html);
        html = StripNoscriptContent(html);
        html = StripIframeContent(html);
        return html;
    }

    /// <summary>
    /// Production render-preparation for browsing / image capture: only the shared replaced-element
    /// passes.
    /// </summary>
    internal static string ProcessForBrowsing(string html) => ApplyReplacedElementPasses(html);

    /// <summary>
    /// Gives every checkbox, radio, file input and <c>&lt;select&gt;</c> without one a
    /// synthetic <c>id</c>.
    /// </summary>
    internal static string StampFormControlIds(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html ?? string.Empty;

        int next = 0;
        html = HostedInputPattern.Replace(html, match => Stamp(match, "input"));
        return SelectPattern.Replace(html, match => Stamp(match, "select"));

        string Stamp(Match match, string tagName)
        {
            string attrs = match.Groups["attrs"].Value;
            if (IdAttributePattern.IsMatch(attrs))
                return match.Value;

            string id = SyntheticIdPrefix + next++.ToString(System.Globalization.CultureInfo.InvariantCulture);

            string trimmed = attrs.TrimEnd();
            bool selfClosing = trimmed.EndsWith('/');
            if (selfClosing)
                trimmed = trimmed[..^1].TrimEnd();

            string separator = trimmed.Length > 0 ? " " : string.Empty;
            return $"<{tagName}{separator}{trimmed} id=\"{id}\"{(selfClosing ? " /" : string.Empty)}>";
        }
    }

    /// <summary>
    /// Removes all <c>&lt;script&gt;</c> tags.
    /// </summary>
    internal static string StripScriptTags(string html)
    {
        return ScriptTagPattern.Replace(html, string.Empty);
    }

    /// <summary>
    /// Removes every <c>&lt;noscript&gt;</c> element together with its content.
    /// </summary>
    internal static string StripNoscriptContent(string html)
    {
        return NoscriptTagPattern.Replace(html, string.Empty);
    }

    /// <summary>
    /// Replaces the fallback content of every <c>&lt;iframe&gt;</c>
    /// element with an empty body.
    /// </summary>
    internal static string StripIframeContent(string html)
    {
        return IframeContentPattern.Replace(html, m =>
            $"<iframe{m.Groups["attrs"].Value}></iframe>");
    }

    /// <summary>
    /// Replaces the fallback content of every <c>&lt;object&gt;</c>
    /// element with an empty body.
    /// </summary>
    internal static string StripObjectContent(string html)
    {
        string result = html;
        string previous;
        do
        {
            previous = result;
            result = ObjectContentPattern.Replace(result, m =>
                $"<object{m.Groups["attrs"].Value}></object>");
        } while (result != previous);
        return result;
    }
}
