using System.Globalization;
using Broiler.Dom.Html;
using Broiler.Layout;
using BDom = Broiler.Dom;

namespace Broiler.Cli.Analysis;

/// <summary>A <c>&lt;script&gt;</c> element of the document as fetched, and what became of it.</summary>
/// <param name="Index">Its position among the document's script elements, from 0.</param>
/// <param name="Kind"><c>classic</c>, <c>module</c>, <c>import map</c>, <c>data block</c> or <c>not a script type</c>.</param>
/// <param name="Source">Its URL, or <c>inline (N bytes)</c>.</param>
/// <param name="Attributes">The attributes that change when and whether it runs: async, defer, nomodule.</param>
/// <param name="Load">For an external script, the outcome of its request: a status, an error, or not requested.</param>
internal sealed record ScriptElement(int Index, string Kind, string Source, string Attributes, string? Load);

/// <summary>A sub-resource element — a stylesheet link, an image, a frame — and the outcome of its request.</summary>
internal sealed record ResourceElement(string Element, string Url, string? Load);

/// <summary>A tag, and how many elements carry it.</summary>
internal sealed record TagCount(string Tag, int Count);

/// <summary>A parse error in the document as fetched, where it is, and what the parser did about it.</summary>
/// <param name="Code">
/// The error's code: the HTML Standard's for the tokenizer's errors (<c>eof-in-tag</c>), Broiler.Dom.Html's
/// for tree construction's (<c>unexpected-end-tag</c>), none for a diagnostic that is not a parse error.
/// </param>
/// <param name="Line">Its line in the document, from 1.</param>
/// <param name="Column">Its column on that line, from 1.</param>
/// <param name="Message">What the markup did, and where Broiler's parser departs from a browser's, what each does.</param>
internal sealed record HtmlParseProblem(string? Code, int? Line, int? Column, string Message);

/// <summary>What the page's markup says about itself.</summary>
internal sealed record HtmlReport
{
    public string? Doctype { get; init; }
    public bool QuirksMode { get; init; }
    public string? DeclaredCharset { get; init; }
    public string? Title { get; init; }
    public string? Language { get; init; }
    public string? Viewport { get; init; }
    public string? BaseHref { get; init; }
    public string? MetaRefresh { get; init; }
    /// <summary>How many parse errors the document as fetched has.</summary>
    public int ParseErrorCount { get; init; }

    /// <summary>The parse errors by code, most frequent first.</summary>
    public IReadOnlyList<TagCount> ParseErrorsByCode { get; init; } = [];

    /// <summary>The first parse errors, in document order.</summary>
    public IReadOnlyList<HtmlParseProblem> ParseErrors { get; init; } = [];
    public int ElementsAsFetched { get; init; }
    public int ElementsAfterScripts { get; init; }
    public int TextLengthAsFetched { get; init; }
    public int TextLengthAfterScripts { get; init; }
    public int MaxDepth { get; init; }
    public IReadOnlyList<TagCount> Tags { get; init; } = [];
    public IReadOnlyList<TagCount> DuplicateIds { get; init; } = [];
    public IReadOnlyList<TagCount> UnknownElements { get; init; } = [];
    public IReadOnlyList<TagCount> ObsoleteElements { get; init; } = [];
    public IReadOnlyList<TagCount> CustomElements { get; init; } = [];
    public IReadOnlyList<ScriptElement> Scripts { get; init; } = [];
    public IReadOnlyList<ResourceElement> Stylesheets { get; init; } = [];
    public IReadOnlyList<ResourceElement> Images { get; init; } = [];
    public IReadOnlyList<ResourceElement> Frames { get; init; } = [];
    public int InlineStyleElements { get; init; }
    public int StyleAttributes { get; init; }
    public int Forms { get; init; }
    public int FormControls { get; init; }
    public IReadOnlyList<TagCount> MediaAndGraphics { get; init; } = [];
}

/// <summary>
/// Reads the page's markup twice — as fetched, and as its scripts left it — and reports what a
/// reader looking for a rendering problem in HTML would check first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Quirks mode leads the report for a reason.</b> A missing or legacy doctype switches the whole
/// document into quirks mode, and quirks mode changes box sizing in tables, line heights and the
/// unitless-length rule — the kind of difference that looks like a layout bug in a dozen unrelated
/// places. Broiler decides it with Broiler.Layout's own reading of the doctype
/// (<see cref="DocumentModeContext.IsQuirksHtml"/>), which is what is reported.
/// </para>
/// <para>
/// <b>Before and after.</b> The document as fetched is parsed here with the same parser and the same
/// options the script bridge uses for a navigation, so its element count and its parse diagnostics
/// are the ones the page's scripts started from. The difference to the document they left is what
/// the scripts built — a page whose scripts failed early shows almost none.
/// </para>
/// <para>
/// <b>Parse errors are the parser's own.</b> The document as fetched is parsed with
/// <see cref="HtmlParseOptions.ReportParseErrors"/>, so each error carries the code, line and column
/// Broiler.Dom.Html gives it and says what its tree builder did. Where that departs from a browser —
/// an end tag that matches nothing closes every open element here, and a <c>&lt;div/&gt;</c> is closed
/// at once — the error is a rendering difference in itself, and the message says so.
/// </para>
/// </remarks>
internal static class HtmlInspector
{
    private const int MaxListed = 40;
    private const int MaxParseErrors = 200;

    /// <summary>The HTML Living Standard's elements. Anything else without a hyphen is unknown to a browser.</summary>
    private static readonly HashSet<string> StandardElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "address", "area", "article", "aside", "audio", "b", "base", "bdi", "bdo", "blockquote",
        "body", "br", "button", "canvas", "caption", "cite", "code", "col", "colgroup", "data", "datalist",
        "dd", "del", "details", "dfn", "dialog", "div", "dl", "dt", "em", "embed", "fencedframe", "fieldset",
        "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hgroup",
        "hr", "html", "i", "iframe", "img", "input", "ins", "kbd", "label", "legend", "li", "link", "main",
        "map", "mark", "menu", "meta", "meter", "nav", "noscript", "object", "ol", "optgroup", "option",
        "output", "p", "param", "picture", "pre", "progress", "q", "rp", "rt", "ruby", "s", "samp", "script",
        "search", "section", "select", "selectedcontent", "slot", "small", "source", "span", "strong", "style",
        "sub", "summary", "sup", "table", "tbody", "td", "template", "textarea", "tfoot", "th", "thead", "time",
        "title", "tr", "track", "u", "ul", "var", "video", "wbr", "svg", "math",
    };

    /// <summary>Elements the standard lists as obsolete; browsers render most of them, but not all alike.</summary>
    private static readonly HashSet<string> ObsoleteElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "acronym", "applet", "basefont", "bgsound", "big", "blink", "center", "dir", "font", "frame",
        "frameset", "image", "isindex", "keygen", "listing", "marquee", "menuitem", "multicol", "nextid",
        "nobr", "noembed", "noframes", "plaintext", "rb", "rtc", "spacer", "strike", "tt", "xmp",
    };

    private static readonly string[] MediaAndGraphicsTags = ["canvas", "svg", "video", "audio", "picture", "math", "object", "embed"];

    /// <summary>Inspects the page's markup.</summary>
    /// <param name="fetchedHtml">The document as it arrived.</param>
    /// <param name="afterScripts">A copy of the document as its scripts left it, or null when none ran.</param>
    /// <param name="network">The page's requests, to say what became of each sub-resource element.</param>
    /// <param name="documentUrl">The document's URL, to resolve relative references against.</param>
    public static HtmlReport Inspect(
        string fetchedHtml,
        BDom.DomDocument? afterScripts,
        IReadOnlyList<NetworkEntry> network,
        string documentUrl)
    {
        var parsed = HtmlDocumentParser.ParseDocument(
            fetchedHtml,
            document: null,
            new HtmlParseOptions(AllowDeclarativeShadowRoots: true) { ReportParseErrors = true });
        var fetched = parsed.Document;
        var final = afterScripts ?? fetched;

        var fetchedElements = LayoutInspector.Descendants(fetched).ToArray();
        var finalElements = afterScripts is null ? fetchedElements : LayoutInspector.Descendants(afterScripts).ToArray();
        var baseUri = BaseUrl(documentUrl, FirstElement(fetchedElements, "base")?.GetAttribute("href"));

        var head = FirstElement(fetchedElements, "html");
        var doctype = fetched.ChildNodes.OfType<BDom.DomDocumentType>().FirstOrDefault();

        return new HtmlReport
        {
            Doctype = doctype is null ? null : DescribeDoctype(doctype),
            QuirksMode = DocumentModeContext.IsQuirksHtml(fetchedHtml),
            DeclaredCharset = DeclaredCharset(fetchedElements),
            Title = FirstElement(finalElements, "title")?.TextContent?.Trim() is { Length: > 0 } title ? title : parsed.Title,
            Language = head?.GetAttribute("lang"),
            Viewport = Meta(fetchedElements, "viewport"),
            BaseHref = FirstElement(fetchedElements, "base")?.GetAttribute("href"),
            MetaRefresh = fetchedElements
                .FirstOrDefault(static e => IsTag(e, "meta")
                    && string.Equals(e.GetAttribute("http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase))
                ?.GetAttribute("content"),
            ParseErrorCount = parsed.Diagnostics.Count,
            ParseErrorsByCode = Top(parsed.Diagnostics.Select(static d => d.Code ?? "(no code)"), MaxListed),
            ParseErrors = [.. parsed.Diagnostics
                .Take(MaxParseErrors)
                .Select(static d => new HtmlParseProblem(d.Code, d.Line, d.Column, d.Message))],
            ElementsAsFetched = fetchedElements.Length,
            ElementsAfterScripts = finalElements.Length,
            TextLengthAsFetched = BodyText(fetchedElements),
            TextLengthAfterScripts = BodyText(finalElements),
            MaxDepth = MaxDepth(final),
            Tags = Top(finalElements.Select(static e => e.LocalName.ToLowerInvariant()), 30),
            DuplicateIds = [.. finalElements
                .Where(static e => e.Id is { Length: > 0 })
                .GroupBy(static e => e.Id!, StringComparer.Ordinal)
                .Where(static g => g.Count() > 1)
                .OrderByDescending(static g => g.Count())
                .Take(MaxListed)
                .Select(static g => new TagCount(g.Key, g.Count()))],
            UnknownElements = Top(finalElements
                .Where(static e => !InForeignContent(e))
                .Select(static e => e.LocalName.ToLowerInvariant())
                .Where(static tag => !tag.Contains('-') && !StandardElements.Contains(tag) && !ObsoleteElements.Contains(tag)), MaxListed),
            ObsoleteElements = Top(finalElements
                .Select(static e => e.LocalName.ToLowerInvariant())
                .Where(static tag => ObsoleteElements.Contains(tag)), MaxListed),
            CustomElements = Top(finalElements
                .Where(static e => !InForeignContent(e))
                .Select(static e => e.LocalName.ToLowerInvariant())
                .Where(static tag => tag.Contains('-')), MaxListed),
            Scripts = Scripts(fetchedElements, network, baseUri),
            Stylesheets = [.. fetchedElements
                .Where(static e => IsTag(e, "link") && HasToken(e.GetAttribute("rel"), "stylesheet"))
                .Select(e => Resource(e, e.GetAttribute("href"), network, baseUri))],
            Images = [.. finalElements
                .Where(static e => IsTag(e, "img"))
                .Take(200)
                .Select(e => e.GetAttribute("src") is { Length: > 0 } src
                    ? Resource(e, src, network, baseUri)
                    : Resource(e, FirstCandidate(e.GetAttribute("srcset")), network, baseUri))],
            Frames = [.. finalElements
                .Where(static e => e.LocalName.ToLowerInvariant() is "iframe" or "frame" or "object" or "embed")
                .Select(e => Resource(e, e.GetAttribute("src") ?? e.GetAttribute("data"), network, baseUri))],
            InlineStyleElements = finalElements.Count(static e => IsTag(e, "style")),
            StyleAttributes = finalElements.Count(static e => e.HasAttribute("style")),
            Forms = finalElements.Count(static e => IsTag(e, "form")),
            FormControls = finalElements.Count(static e => e.LocalName.ToLowerInvariant() is "input" or "select" or "textarea" or "button"),
            MediaAndGraphics = [.. MediaAndGraphicsTags
                .Select(tag => new TagCount(tag, finalElements.Count(e => IsTag(e, tag))))
                .Where(static t => t.Count > 0)],
        };
    }

    /// <summary>The values of every <c>style</c> attribute in <paramref name="document"/>.</summary>
    public static IReadOnlyList<string> StyleAttributes(BDom.DomDocument document) =>
        [.. LayoutInspector.Descendants(document)
            .Select(static e => e.GetAttribute("style"))
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(static s => s!)];

    /// <summary>The text of every <c>&lt;style&gt;</c> element in <paramref name="document"/>, in document order.</summary>
    public static IReadOnlyList<string> StyleElements(BDom.DomDocument document) =>
        [.. LayoutInspector.Descendants(document)
            .Where(static e => IsTag(e, "style"))
            .Select(static e => e.TextContent ?? string.Empty)];

    /// <summary>Parses <paramref name="html"/> the way the script bridge parses a navigation.</summary>
    public static BDom.DomDocument Parse(string html) =>
        HtmlDocumentParser.ParseDocument(html, document: null, new HtmlParseOptions(AllowDeclarativeShadowRoots: true)).Document;

    private static IReadOnlyList<ScriptElement> Scripts(
        IReadOnlyList<BDom.DomElement> elements,
        IReadOnlyList<NetworkEntry> network,
        Uri? baseUri)
    {
        var scripts = new List<ScriptElement>();
        var index = 0;
        foreach (var element in elements.Where(static e => IsTag(e, "script")))
        {
            var type = element.GetAttribute("type")?.Trim().ToLowerInvariant() ?? string.Empty;
            var kind = type switch
            {
                "" or "text/javascript" or "application/javascript" or "text/ecmascript" or "application/ecmascript"
                    or "application/x-javascript" or "text/jscript" or "text/livescript" => "classic",
                "module" => "module",
                "importmap" => "import map",
                "speculationrules" or "application/json" or "application/ld+json" => "data block",
                _ => $"not run (type=\"{type}\")",
            };

            var attributes = string.Join(' ', new[] { "async", "defer", "nomodule" }.Where(element.HasAttribute));
            var src = element.GetAttribute("src");
            string source;
            string? load = null;
            if (src is not null)
            {
                source = Resolve(src, baseUri);
                load = LoadOutcome(source, network);
            }
            else
            {
                source = $"inline ({(element.TextContent ?? string.Empty).Length} chars)";
            }

            scripts.Add(new ScriptElement(index++, kind, source, attributes, load));
        }

        return scripts;
    }

    private static ResourceElement Resource(BDom.DomElement element, string? reference, IReadOnlyList<NetworkEntry> network, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return new ResourceElement(LayoutInspector.Describe(element), "(none)", "no URL");

        var url = Resolve(reference, baseUri);
        return new ResourceElement(LayoutInspector.Describe(element), url, LoadOutcome(url, network));
    }

    /// <summary>
    /// A <c>srcset</c>'s first candidate URL, which is enough to say whether any was fetched. Only a
    /// srcset is split: in a <c>src</c> or an <c>href</c>, a comma or a space is part of the URL —
    /// <c>css?family=Open+Sans:400,700</c>, a CDN's <c>c_fill,w_300</c>.
    /// </summary>
    internal static string? FirstCandidate(string? srcset)
    {
        if (string.IsNullOrWhiteSpace(srcset))
            return null;

        var trimmed = srcset.TrimStart();
        var end = trimmed.IndexOfAny([' ', '\t', '\n', '\r', '\f']);
        var url = end < 0 ? trimmed : trimmed[..end];
        // A URL followed directly by the comma that ends its candidate: HTML's srcset parsing drops it.
        return url.TrimEnd(',');
    }

    /// <summary>
    /// The document's base URL: its first <c>&lt;base href&gt;</c> resolved against its own URL, as
    /// HTML §2.4.1 has it, or its own URL without one.
    /// </summary>
    internal static Uri? BaseUrl(string documentUrl, string? baseHref)
    {
        if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var document))
            return null;

        return string.IsNullOrWhiteSpace(baseHref) ? document
            : Uri.TryCreate(Resolve(baseHref, document), UriKind.Absolute, out var declared) ? declared
            : document;
    }

    /// <summary>
    /// Whether a load outcome is one where the resource did not arrive: not found, failed, an HTTP
    /// error, never asked for, or no answer. Inline <c>data:</c> URLs and local files that exist did.
    /// </summary>
    internal static bool DidNotLoad(string? load) =>
        load is { } outcome
        && (outcome.Contains("NOT FOUND", StringComparison.Ordinal)
            || outcome.StartsWith("failed", StringComparison.Ordinal)
            || outcome.Contains("HTTP error", StringComparison.Ordinal)
            || outcome is "not requested" or "no response");

    /// <summary>What became of the request for <paramref name="url"/>, from the network log.</summary>
    internal static string LoadOutcome(string url, IReadOnlyList<NetworkEntry> network)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return "inline data: URL";

        var entry = network.LastOrDefault(e => string.Equals(e.Url, url, StringComparison.Ordinal))
            ?? network.LastOrDefault(e => e.Redirects.Contains(url, StringComparer.Ordinal));
        if (entry is null)
        {
            // A file: reference is read from disk by whoever needs it, never over the network, so the
            // log cannot say what became of it; the disk can. Never a UNC path: asking whether one
            // exists is a network round trip per reference, and the page cannot have meant one.
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile && !uri.IsUnc)
                return File.Exists(uri.LocalPath) ? "local file" : "local file NOT FOUND";

            return "not requested";
        }

        if (entry.Error is { } error)
            return $"failed: {error}";

        return entry.Status is { } status
            ? string.Create(CultureInfo.InvariantCulture, $"{status}{(entry.Status >= 400 ? " (HTTP error)" : string.Empty)}, {entry.BodyBytes:N0} bytes")
            : "no response";
    }

    /// <summary>
    /// Resolves a reference the way a browser does: absolute only when it names a scheme.
    /// </summary>
    /// <remarks>
    /// <see cref="Uri.TryCreate(string?, UriKind, out Uri?)"/> with <see cref="UriKind.Absolute"/> is
    /// not that test. It reads a protocol-relative <c>//host/path</c> as a UNC file path on Windows,
    /// and a root-relative <c>/path</c> as a file on Unix — so a page's <c>//upload.example.org/a.png</c>
    /// would resolve to <c>file://upload.example.org/a.png</c> instead of to its HTTPS URL.
    /// </remarks>
    internal static string Resolve(string reference, Uri? baseUri)
    {
        var trimmed = reference.Trim();
        if (HasScheme(trimmed) && Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return absolute.AbsoluteUri;

        return baseUri is not null && Uri.TryCreate(baseUri, trimmed, out var resolved) ? resolved.AbsoluteUri : trimmed;
    }

    /// <summary>Whether <paramref name="reference"/> starts with a URL scheme and its colon.</summary>
    private static bool HasScheme(string reference)
    {
        var colon = reference.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(reference[0]))
            return false;

        for (var i = 1; i < colon; i++)
        {
            var c = reference[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                return false;
        }

        return true;
    }

    private static string DescribeDoctype(BDom.DomDocumentType doctype)
    {
        var text = $"<!DOCTYPE {doctype.Name}";
        if (!string.IsNullOrEmpty(doctype.PublicId))
            text += $" PUBLIC \"{doctype.PublicId}\"";
        if (!string.IsNullOrEmpty(doctype.SystemId))
            text += $" \"{doctype.SystemId}\"";
        return text + ">";
    }

    private static string? DeclaredCharset(IEnumerable<BDom.DomElement> elements)
    {
        foreach (var meta in elements.Where(static e => IsTag(e, "meta")))
        {
            if (meta.GetAttribute("charset") is { Length: > 0 } charset)
                return charset;

            if (string.Equals(meta.GetAttribute("http-equiv"), "content-type", StringComparison.OrdinalIgnoreCase)
                && meta.GetAttribute("content") is { } content
                && content.IndexOf("charset=", StringComparison.OrdinalIgnoreCase) is var at and >= 0)
            {
                return content[(at + "charset=".Length)..].Trim().Trim('"', '\'', ';');
            }
        }

        return null;
    }

    private static string? Meta(IEnumerable<BDom.DomElement> elements, string name) =>
        elements.FirstOrDefault(e => IsTag(e, "meta") && string.Equals(e.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase))
            ?.GetAttribute("content");

    private static int BodyText(IEnumerable<BDom.DomElement> elements) =>
        FirstElement(elements, "body")?.TextContent?.Length ?? 0;

    private static int MaxDepth(BDom.DomNode root)
    {
        var max = 0;
        var stack = new Stack<(BDom.DomNode Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            max = Math.Max(max, depth);
            foreach (var child in node.ChildNodes)
            {
                if (child is BDom.DomElement)
                    stack.Push((child, depth + 1));
            }
        }

        return max;
    }

    private static IReadOnlyList<TagCount> Top(IEnumerable<string> tags, int count) =>
        [.. tags
            .GroupBy(static t => t, StringComparer.Ordinal)
            .Select(static g => new TagCount(g.Key, g.Count()))
            .OrderByDescending(static t => t.Count)
            .ThenBy(static t => t.Tag, StringComparer.Ordinal)
            .Take(count)];

    private static BDom.DomElement? FirstElement(IEnumerable<BDom.DomElement> elements, string tag) =>
        elements.FirstOrDefault(e => IsTag(e, tag));

    private static bool IsTag(BDom.DomElement element, string tag) =>
        string.Equals(element.LocalName, tag, StringComparison.OrdinalIgnoreCase);

    private static bool HasToken(string? value, string token) =>
        value is not null && value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Contains(token, StringComparer.OrdinalIgnoreCase);

    /// <summary>Inside <c>&lt;svg&gt;</c> or <c>&lt;math&gt;</c>, where the HTML element list does not apply.</summary>
    private static bool InForeignContent(BDom.DomElement element)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (current.LocalName.ToLowerInvariant() is "svg" or "math")
                return true;
        }

        return false;
    }
}
