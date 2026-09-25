using System.Globalization;
using Broiler.CSS;
using Broiler.CSS.Dom;
using Broiler.Layout.Text;

namespace Broiler.Cli.Analysis;

/// <summary>One stylesheet the page used, as the analysis read it.</summary>
/// <param name="Source">Where it came from: a URL, <c>&lt;style&gt; #n</c>, or <c>style attributes</c>.</param>
/// <param name="Text">Its text.</param>
/// <param name="SavedAs">Its file under <c>resources/</c>, when it was archived.</param>
internal sealed record StylesheetSource(string Source, string Text, string? SavedAs);

/// <summary>A problem Broiler.CSS's parser reported, located in its stylesheet.</summary>
internal sealed record CssProblem(
    string Source,
    string Code,
    string Severity,
    string Message,
    int Line,
    int Column,
    string Excerpt);

/// <summary>A property, or a property and value, and how often and where the page used it.</summary>
internal sealed record CssUsage(string Property, string? Value, int Count, string FirstSource);

/// <summary>One <c>@font-face</c> rule.</summary>
internal sealed record FontFaceRule(string Family, string Source, string? Src);

/// <summary>A pseudo-class or pseudo-element the style engine does not model as written, and the selectors that use it.</summary>
/// <param name="Part">The pseudo-class or pseudo-element, with <c>()</c> for a functional one.</param>
/// <param name="Kind"><c>guessed</c>, <c>not modeled</c>, <c>unstyled pseudo-element</c>, <c>invalid</c> or <c>interactive</c>.</param>
/// <param name="Selectors">How many selectors in the page's style rules use it.</param>
/// <param name="Example">The first of them.</param>
/// <param name="FirstSource">Where its rule is: the stylesheet, the line and the column.</param>
internal sealed record CssSelectorGapUsage(string Part, string Kind, int Selectors, string Example, string FirstSource);

/// <summary>A <c>font-family</c> list the rendered text asked for, and what it resolves to.</summary>
/// <param name="Requested">The list, as the computed style gave it.</param>
/// <param name="TextRuns">How many text runs asked for it.</param>
/// <param name="ResolvedFamily">The first family in the list that is available, or null for none.</param>
/// <param name="Resolution"><c>web font</c>, <c>installed</c>, <c>generic</c> or <c>none</c>.</param>
/// <param name="Unavailable">The families ahead of it in the list that were not available.</param>
internal sealed record FontResolution(
    string Requested,
    int TextRuns,
    string? ResolvedFamily,
    string Resolution,
    IReadOnlyList<string> Unavailable);

/// <summary>What the page's CSS says about itself.</summary>
internal sealed record CssReport
{
    public IReadOnlyList<CssSheetSummary> Sheets { get; init; } = [];
    public IReadOnlyList<CssProblem> ParseProblems { get; init; } = [];
    public IReadOnlyDictionary<string, int> AtRules { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> UnknownAtRules { get; init; } = [];
    public IReadOnlyList<CssUsage> UnknownProperties { get; init; } = [];
    public IReadOnlyList<CssUsage> RejectedValues { get; init; } = [];
    public IReadOnlyList<CssUsage> RejectedDuringCascade { get; init; } = [];
    public int VendorPrefixedDeclarations { get; init; }
    public IReadOnlyList<CssUsage> VendorPrefixedProperties { get; init; } = [];
    public int ImportantDeclarations { get; init; }
    public int CustomProperties { get; init; }
    public int StyleAttributes { get; init; }
    public IReadOnlyList<FontFaceRule> FontFaces { get; init; } = [];
    public IReadOnlyList<FontResolution> Fonts { get; init; } = [];

    /// <summary>The selectors the style engine does not model as written, the likeliest to mislead first.</summary>
    public IReadOnlyList<CssSelectorGapUsage> SelectorGaps { get; init; } = [];

    /// <summary>
    /// The properties Broiler's layout engine ignored while it styled the rendered boxes, with example
    /// values; the count is of reports, one per box.
    /// </summary>
    public IReadOnlyList<CssUsage> NotAppliedByLayout { get; init; } = [];

    /// <summary>The features Broiler's layout engine laid out as something simpler, with what it did.</summary>
    public IReadOnlyList<CssUsage> LayoutFallbacks { get; init; } = [];
}

/// <summary>One stylesheet's size and shape, for the report's table.</summary>
internal sealed record CssSheetSummary(string Source, string? SavedAs, int Bytes, int Rules, int Declarations, int Problems);

/// <summary>
/// Reads the page's CSS with Broiler.CSS's own parser and style engine, and reports what they would
/// drop, reject or not recognise.
/// </summary>
/// <remarks>
/// <para>
/// <b>The engine is the judge, not a list in this file.</b> Whether a property is known is asked of
/// Broiler.CSS's <c>@supports</c> evaluation (<see cref="CssStyleEngine.EvaluatesSupportsCondition"/>)
/// with <c>initial</c>, which every property accepts; whether a value is acceptable is asked of the
/// validator the cascade itself uses (<see cref="CssDeclarationValidator"/>). So the report says what
/// this build of the engine does with the page, and it changes when the engine does.
/// </para>
/// <para>
/// <b>Known is not rendered.</b> Broiler.CSS models the property set a browser supports; whether
/// Broiler's layout draws a known property is a separate question the layout engine does not yet
/// answer to a caller. A property this report does not list may still have no effect on the image.
/// </para>
/// <para>
/// <b>Each stylesheet is parsed on its own.</b> The renderer concatenates sheets before it keeps
/// their diagnostics, which leaves a diagnostic's offset pointing into a text no file holds; parsed
/// separately, each problem gets a line and a column in the file it is in.
/// </para>
/// <para>
/// <b>Selectors are judged by the matcher's own account of itself.</b> Every selector of every style
/// rule is put to <see cref="CssSelectorMatcher.DescribeGaps"/>, which names each pseudo-class it
/// guesses at (it then matches every element), answers "no" for, or does not know, and each
/// pseudo-element the cascade does not style.
/// </para>
/// </remarks>
internal static class CssInspector
{
    private const int MaxProblems = 200;
    private const int MaxUsages = 50;
    private const int MaxSelectorGaps = 200;

    /// <summary>
    /// Properties, without a vendor prefix, that change nothing in a still image: how a page answers a
    /// pointer, a selection, scrolling or the passing of time. A longhand of one counts too.
    /// </summary>
    private static readonly string[] NoEffectInAStillImage =
    [
        "cursor", "pointer-events", "user-select", "user-modify", "user-drag", "touch-action", "caret-color", "caret",
        "resize", "speak", "interactivity", "interpolate-size", "tap-highlight-color", "scroll-behavior",
        "scroll-snap-type", "scroll-snap-align", "scroll-snap-stop", "scroll-padding", "scroll-margin",
        "overscroll-behavior", "transition", "view-transition-name",
    ];

    /// <summary>At-rules the CSS specifications define. Anything else is a typo or a proprietary extension.</summary>
    private static readonly HashSet<string> StandardAtRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "charset", "import", "namespace", "media", "supports", "font-face", "keyframes", "page", "layer",
        "container", "property", "counter-style", "font-feature-values", "font-palette-values", "scope",
        "starting-style", "custom-media", "view-transition", "position-try", "document", "viewport",
        "-webkit-keyframes", "-moz-keyframes", "-o-keyframes", "-ms-viewport", "-moz-document",
    };

    /// <summary>Inspects <paramref name="sheets"/>.</summary>
    /// <param name="sheets">Every stylesheet the page used.</param>
    /// <param name="styleAttributes">The values of the page's <c>style</c> attributes.</param>
    /// <param name="rejectedDuringCascade">What the style engine dropped while it cascaded, as it reported it.</param>
    /// <param name="requestedFonts">The font-family lists the rendered text asked for, with their text-run counts.</param>
    public static CssReport Inspect(
        IReadOnlyList<StylesheetSource> sheets,
        IReadOnlyList<string> styleAttributes,
        IReadOnlyList<(string Property, string Value)> rejectedDuringCascade,
        IReadOnlyDictionary<string, int> requestedFonts)
    {
        var parser = new CssParser();
        var summaries = new List<CssSheetSummary>();
        var problems = new List<CssProblem>();
        var atRules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var properties = new Dictionary<string, (int Count, string Source)>(StringComparer.OrdinalIgnoreCase);
        var rejected = new Dictionary<(string, string), (int Count, string Source)>();
        var fontFaces = new List<FontFaceRule>();
        var important = 0;
        var custom = 0;
        var vendorPrefixed = 0;
        var gaps = new Dictionary<(string Part, CssSelectorGapKind Kind), (int Selectors, string Example, string Source)>();

        foreach (var sheet in sheets)
        {
            var parsed = parser.ParseStyleSheet(sheet.Text);
            var declarations = 0;
            var rules = 0;
            Walk(parsed.Rules, sheet.Source);

            foreach (var diagnostic in parsed.Diagnostics)
            {
                if (problems.Count < MaxProblems)
                    problems.Add(Locate(sheet, diagnostic, offsetShift: 0));
            }

            summaries.Add(new CssSheetSummary(
                sheet.Source,
                sheet.SavedAs,
                System.Text.Encoding.UTF8.GetByteCount(sheet.Text),
                rules,
                declarations,
                parsed.Diagnostics.Count));

            void Walk(IReadOnlyList<CssRule> list, string source)
            {
                foreach (var rule in list)
                {
                    rules++;
                    switch (rule)
                    {
                        case CssStyleRule style:
                            SelectorGaps(style);
                            Declarations(style.Declarations, source);
                            break;
                        case CssAtRule at:
                            atRules[at.Name] = atRules.GetValueOrDefault(at.Name) + 1;
                            if (at.Declarations is { } block)
                            {
                                if (string.Equals(at.Name, "font-face", StringComparison.OrdinalIgnoreCase))
                                {
                                    fontFaces.Add(new FontFaceRule(
                                        Unquote(block.GetPropertyValue("font-family") ?? "(no font-family)"),
                                        source,
                                        block.GetPropertyValue("src")));
                                }
                                else
                                {
                                    Declarations(block, source);
                                }
                            }

                            Walk(at.Rules, source);
                            break;
                    }
                }
            }

            void SelectorGaps(CssStyleRule style)
            {
                foreach (var selector in style.Selectors.Selectors)
                {
                    foreach (var gap in CssSelectorMatcher.DescribeGaps(selector.Text))
                    {
                        var key = (PartName(gap.Text), gap.Kind);
                        gaps[key] = gaps.TryGetValue(key, out var seen)
                            ? (seen.Selectors + 1, seen.Example, seen.Source)
                            : (1, selector.Text, $"{sheet.Source} {LineAndColumn(sheet.Text, style.Range.Start)}");
                    }
                }
            }

            void Declarations(CssDeclarationBlock block, string source)
            {
                foreach (var declaration in block.Declarations)
                {
                    declarations++;
                    Count(declaration.Name, declaration.Value.Text, declaration.Important, source);
                }
            }
        }

        // A style attribute is a declaration block; parsing it as the block of a rule is how its
        // problems come out with a location, which the declaration-block parser keeps to itself.
        var attributeProblems = 0;
        for (var i = 0; i < styleAttributes.Count; i++)
        {
            const string Prefix = "*{";
            var source = $"style attribute #{i + 1}";
            var parsed = parser.ParseStyleSheet(Prefix + styleAttributes[i] + "}");
            foreach (var rule in parsed.Rules.OfType<CssStyleRule>())
            {
                foreach (var declaration in rule.Declarations.Declarations)
                    Count(declaration.Name, declaration.Value.Text, declaration.Important, "style attributes");
            }

            foreach (var diagnostic in parsed.Diagnostics)
            {
                attributeProblems++;
                if (problems.Count < MaxProblems)
                    problems.Add(Locate(new StylesheetSource(source, styleAttributes[i], null), diagnostic, Prefix.Length));
            }
        }

        if (styleAttributes.Count > 0)
            summaries.Add(new CssSheetSummary("style attributes", null, styleAttributes.Sum(static s => s.Length), styleAttributes.Count, 0, attributeProblems));

        var unknown = new List<CssUsage>();
        var vendor = new List<CssUsage>();
        foreach (var (name, (count, source)) in properties)
        {
            if (name.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (name.StartsWith('-'))
            {
                vendor.Add(new CssUsage(name, null, count, source));
                continue;
            }

            if (!IsKnownProperty(name))
                unknown.Add(new CssUsage(name, null, count, source));
        }

        var cascade = rejectedDuringCascade
            .GroupBy(static r => (r.Property.ToLowerInvariant(), r.Value))
            .Select(static g => new CssUsage(g.Key.Item1, g.Key.Value, g.Count(), "cascade"))
            .OrderByDescending(static u => u.Count)
            .Take(MaxUsages)
            .ToArray();

        return new CssReport
        {
            Sheets = summaries,
            ParseProblems = problems,
            AtRules = atRules.OrderByDescending(static p => p.Value).ToDictionary(static p => p.Key, static p => p.Value),
            UnknownAtRules = [.. atRules.Keys.Where(static name => !StandardAtRules.Contains(name)).Order(StringComparer.Ordinal)],
            UnknownProperties = [.. unknown.OrderByDescending(static u => u.Count).Take(MaxUsages)],
            RejectedValues = [.. rejected
                .Select(static p => new CssUsage(p.Key.Item1, p.Key.Item2, p.Value.Count, p.Value.Source))
                .OrderByDescending(static u => u.Count)
                .Take(MaxUsages)],
            RejectedDuringCascade = cascade,
            VendorPrefixedDeclarations = vendorPrefixed,
            VendorPrefixedProperties = [.. vendor.OrderByDescending(static u => u.Count).Take(MaxUsages)],
            ImportantDeclarations = important,
            CustomProperties = custom,
            StyleAttributes = styleAttributes.Count,
            FontFaces = fontFaces,
            Fonts = ResolveFonts(requestedFonts, fontFaces),
            SelectorGaps = [.. gaps
                .OrderBy(static g => Rank(g.Key.Kind))
                .ThenByDescending(static g => g.Value.Selectors)
                .ThenBy(static g => g.Key.Part, StringComparer.Ordinal)
                .Take(MaxSelectorGaps)
                .Select(static g => new CssSelectorGapUsage(g.Key.Part, KindName(g.Key.Kind), g.Value.Selectors, g.Value.Example, g.Value.Source))],
        };

        void Count(string name, string value, bool isImportant, string source)
        {
            if (isImportant)
                important++;

            if (name.StartsWith("--", StringComparison.Ordinal))
            {
                custom++;
                return;
            }

            if (name.StartsWith('-'))
                vendorPrefixed++;

            properties[name] = properties.TryGetValue(name, out var seen)
                ? (seen.Count + 1, seen.Source)
                : (1, source);

            // A var() or a CSS-wide keyword is resolved later, so it cannot be judged here.
            if (value.Contains("var(", StringComparison.OrdinalIgnoreCase) || IsCssWideKeyword(value))
                return;

            if (!IsAcceptable(name, value))
            {
                var key = (name.ToLowerInvariant(), value.Trim());
                rejected[key] = rejected.TryGetValue(key, out var hit) ? (hit.Count + 1, hit.Source) : (1, source);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="property"/> can change a still image. Ignoring <c>cursor</c> or a
    /// <c>transition</c> is invisible in a screenshot; ignoring <c>backdrop-filter</c> is not.
    /// </summary>
    internal static bool AffectsAStillImage(string property)
    {
        var name = property.ToLowerInvariant();
        if (name.StartsWith('-') && name.IndexOf('-', 1) is > 1 and var dash)
            name = name[(dash + 1)..];

        return !NoEffectInAStillImage.Any(neutral =>
            name == neutral || name.StartsWith(neutral + "-", StringComparison.Ordinal));
    }

    /// <summary>A selector part's name for grouping: the arguments of a functional one become <c>()</c>.</summary>
    private static string PartName(string text)
    {
        var open = text.IndexOf('(', StringComparison.Ordinal);
        return open < 0 ? text : text[..open] + "()";
    }

    /// <summary>How a kind of selector gap reads in a report.</summary>
    internal static string KindName(CssSelectorGapKind kind) => kind switch
    {
        CssSelectorGapKind.Guessed => "guessed",
        CssSelectorGapKind.NotModeled => "not modeled",
        CssSelectorGapKind.UnstyledPseudoElement => "unstyled pseudo-element",
        CssSelectorGapKind.Invalid => "invalid",
        _ => "interactive",
    };

    // The order a reader needs them in: a guess changes what renders everywhere its rule reaches, a
    // missed match and an unstyled pseudo-element change it where they are used, an invalid
    // pseudo-class differs from a browser only in a selector list, and nothing is interactive in a
    // screenshot for either.
    private static int Rank(CssSelectorGapKind kind) => kind switch
    {
        CssSelectorGapKind.Guessed => 0,
        CssSelectorGapKind.NotModeled => 1,
        CssSelectorGapKind.UnstyledPseudoElement => 2,
        CssSelectorGapKind.Invalid => 3,
        _ => 4,
    };

    /// <summary>Whether Broiler.CSS knows <paramref name="property"/>, asked through its own <c>@supports</c>.</summary>
    internal static bool IsKnownProperty(string property)
    {
        try
        {
            return CssStyleEngine.EvaluatesSupportsCondition($"({property}: initial)");
        }
        catch (Exception)
        {
            // A name the condition parser cannot read is not one the engine knows.
            return false;
        }
    }

    private static bool IsAcceptable(string property, string value)
    {
        try
        {
            return CssDeclarationValidator.IsAcceptableDeclarationValue(property, value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsCssWideKeyword(string value) =>
        value.Trim().ToLowerInvariant() is "inherit" or "initial" or "unset" or "revert" or "revert-layer";

    /// <summary>
    /// Resolves each font-family list the text asked for the way a browser does: the first family
    /// that is available wins, where available means declared by an <c>@font-face</c> rule on the page,
    /// installed on this machine, or a generic family this machine has a face for.
    /// </summary>
    /// <remarks>
    /// This is the answer a browser would give, not a trace of Broiler's own font lookup, which does
    /// not report what it picked. When the first family in a list is unavailable, the text is set in
    /// something else, and that is the finding: a different face wraps differently and moves
    /// everything after it.
    /// </remarks>
    internal static IReadOnlyList<FontResolution> ResolveFonts(
        IReadOnlyDictionary<string, int> requested,
        IReadOnlyList<FontFaceRule> fontFaces)
    {
        var declared = new HashSet<string>(fontFaces.Select(static f => f.Family), StringComparer.OrdinalIgnoreCase);
        var results = new List<FontResolution>();

        foreach (var (list, runs) in requested)
        {
            var families = list
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unquote)
                .Where(static f => f.Length > 0)
                .ToArray();

            var unavailable = new List<string>();
            string? resolved = null;
            var how = "none";
            foreach (var family in families)
            {
                if (declared.Contains(family))
                {
                    (resolved, how) = (family, "web font");
                    break;
                }

                if (SafeResolve(family))
                {
                    (resolved, how) = (family, IsGeneric(family) ? "generic" : "installed");
                    break;
                }

                unavailable.Add(family);
            }

            results.Add(new FontResolution(list, runs, resolved, how, unavailable));
        }

        return [.. results.OrderByDescending(static r => r.Unavailable.Count > 0).ThenByDescending(static r => r.TextRuns)];

        static bool SafeResolve(string family)
        {
            try
            {
                return SystemFontIndex.TryResolve(family, bold: false, italic: false, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private static bool IsGeneric(string family) =>
        family.ToLowerInvariant() is "serif" or "sans-serif" or "monospace" or "cursive" or "fantasy" or "system-ui"
            or "ui-sans-serif" or "ui-serif" or "ui-monospace" or "ui-rounded" or "math" or "emoji" or "fangsong";

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && (trimmed[0] is '"' or '\'') && trimmed[^1] == trimmed[0]
            ? trimmed[1..^1]
            : trimmed;
    }

    /// <summary><c>line:column</c> of <paramref name="offset"/> in <paramref name="text"/>, both from 1.</summary>
    private static string LineAndColumn(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{line}:{offset - lineStart + 1}");
    }

    private static CssProblem Locate(StylesheetSource sheet, CssDiagnostic diagnostic, int offsetShift)
    {
        var offset = Math.Clamp(diagnostic.Range.Start - offsetShift, 0, sheet.Text.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            if (sheet.Text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        var excerptEnd = Math.Min(sheet.Text.Length, offset + Math.Max(diagnostic.Range.Length, 40));
        var excerpt = sheet.Text[offset..excerptEnd];
        return new CssProblem(
            sheet.Source,
            diagnostic.Code,
            diagnostic.Severity.ToString(),
            diagnostic.Message,
            line,
            offset - lineStart + 1,
            AnalysisConsole.OneLine(excerpt, 80));
    }

    /// <summary>Formats a usage for a table: <c>property</c> or <c>property: value</c>.</summary>
    internal static string Describe(CssUsage usage) => usage.Value is null
        ? usage.Property
        : string.Create(CultureInfo.InvariantCulture, $"{usage.Property}: {AnalysisConsole.OneLine(usage.Value, 80)}");
}
