using System.Globalization;
using System.Text;

namespace Broiler.Layout.Engine;

/// <summary>
/// The source an <c>&lt;img&gt;</c>'s <c>srcset</c> selected, and the pixel density it was selected
/// at. HTML §4.8.4.3 calls that density the image's <b>current pixel density</b>, and it is what
/// divides the decoded bitmap's size to give the element's natural size — a <c>2x</c> candidate is
/// laid out at half the pixels it decodes to.
/// </summary>
internal readonly record struct ResponsiveImageSelection(string Url, double Density);

/// <summary>
/// HTML §4.8.4.3 <b>select an image source</b>, and the two attribute parsers it is built on:
/// <b>parse a srcset attribute</b> and <b>parse a sizes attribute</b>.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed the engine read <c>src</c> and nothing else, so a responsive image —
/// <c>&lt;img srcset="a.png 2x"&gt;</c>, or any <c>&lt;picture&gt;</c> — had no source at all and
/// painted the missing-image border. Two of the WPT run's worst pixel mismatches are exactly that
/// page: <c>the-img-element/current-pixel-density/basic</c> renders fifteen images of which
/// fourteen carry only a <c>srcset</c>, and <c>sizes/implicit-sizes-ignores-width</c> renders two.
/// </para>
/// <para>
/// The density is as much of the algorithm's output as the URL. A <c>w</c> descriptor is not a size
/// — it states how many pixels the candidate has to spend on a slot whose CSS width comes from
/// <c>sizes</c>, so <c>&lt;img srcset="x.png 100w" sizes="400px"&gt;</c> lays a 100-pixel-wide
/// bitmap out 400px wide (density 0.25). Dropping the density and using the bitmap's own size would
/// be a different image on the page, not a rounding difference.
/// </para>
/// </remarks>
internal static class ResponsiveImageSourceSet
{
    /// <summary>
    /// The display density the selection targets. A rendered page is 1 CSS pixel per device pixel
    /// here — the layout engine has no HiDPI scale of its own, and the reference browser the pixel
    /// suite compares against is driven at <c>deviceScaleFactor: 1</c>.
    /// </summary>
    private const double DevicePixelRatio = 1.0;

    /// <summary>The source size an absent or wholly unparseable <c>sizes</c> falls back to.</summary>
    private const string DefaultSourceSize = "100vw";

    /// <summary>
    /// Runs <b>select an image source</b> for <paramref name="box"/>, an <c>&lt;img&gt;</c>.
    /// Returns <see langword="null"/> when the element is not responsive — no <c>srcset</c> on it
    /// and no <c>&lt;picture&gt;</c> <c>&lt;source&gt;</c> that applies — in which case the caller
    /// keeps reading <c>src</c> exactly as it did before.
    /// </summary>
    internal static ResponsiveImageSelection? Select(CssBox box)
    {
        // `srcset` and `sizes` are author text with no grammar the parser can rely on, and this runs
        // inside the layout of one box: an exception here would abort the layout of the subtree
        // around it, so a page with one malformed attribute would render as a blank area rather
        // than as the same page with one image missing. Falling back to `src` is the failure mode
        // the element already has when it has no candidate at all.
        try
        {
            return SelectCore(box);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    private static ResponsiveImageSelection? SelectCore(CssBox box)
    {
        if (box?.HtmlTag is null)
            return null;

        var (srcset, sizes, fromSourceElement) = ResolveSourceSet(box);
        if (string.IsNullOrWhiteSpace(srcset))
            return null;

        var candidates = ParseSrcset(srcset);
        if (candidates.Count == 0)
            return null;

        bool anyWidthDescriptor = false;
        foreach (var candidate in candidates)
        {
            if (candidate.Width is not null)
            {
                anyWidthDescriptor = true;
                break;
            }
        }

        // HTML "update the source set": `sizes` is consulted only when a candidate states a width,
        // because it exists to turn that width into a density. `sizes="400px"` on an all-`x` srcset
        // is inert, which is why `implicit-sizes-ignores-width` can assert that a `width` content
        // attribute does not stand in for it.
        double sourceSize = anyWidthDescriptor ? ResolveSourceSize(box, sizes) : 0;

        var densities = new List<(string Url, double Density)>(candidates.Count + 1);
        foreach (var candidate in candidates)
        {
            // A `w` descriptor divided by the slot's CSS width is the density the candidate would
            // be displayed at; `sizes="0px"` therefore yields an infinite density and a zero-sized
            // image, which is what the reference browser renders for it.
            double density = candidate.Width is { } width
                ? (sourceSize > 0 ? width / sourceSize : double.PositiveInfinity)
                : candidate.Density ?? 1.0;
            densities.Add((candidate.Url, density));
        }

        // The `src` of an element whose own srcset is all-`x` is a 1x candidate too, so
        // `<img src=a srcset="b 2x">` still has something to show at 1x. It joins neither an srcset
        // that states widths — that one describes the whole set — nor a `<picture>` whose
        // `<source>` supplied the set, where `src` is the fallback for when no source matched at
        // all and this one did.
        if (!anyWidthDescriptor && !fromSourceElement
            && box.GetAttribute("src") is { Length: > 0 } src
            && !ContainsDensity(densities, 1.0))
        {
            densities.Add((src, 1.0));
        }

        return SelectByDensity(densities);
    }

    private static bool ContainsDensity(List<(string Url, double Density)> densities, double density)
    {
        foreach (var entry in densities)
        {
            if (entry.Density == density)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The smallest-density candidate that still covers the display density, or — when every
    /// candidate is below it — the densest one. Ties keep the earlier candidate, which is the
    /// document order the selection is defined to be stable in.
    /// </summary>
    private static ResponsiveImageSelection? SelectByDensity(List<(string Url, double Density)> densities)
    {
        (string Url, double Density)? best = null;
        foreach (var entry in densities)
        {
            if (entry.Density < DevicePixelRatio || double.IsNaN(entry.Density))
                continue;
            if (best is null || entry.Density < best.Value.Density)
                best = entry;
        }

        if (best is null)
        {
            foreach (var entry in densities)
            {
                if (double.IsNaN(entry.Density))
                    continue;
                if (best is null || entry.Density > best.Value.Density)
                    best = entry;
            }
        }

        return best is { } chosen && !string.IsNullOrWhiteSpace(chosen.Url)
            ? new ResponsiveImageSelection(chosen.Url, chosen.Density)
            : null;
    }

    // ── <picture> ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>srcset</c>/<c>sizes</c> pair that applies to this <c>&lt;img&gt;</c>: the first
    /// preceding <c>&lt;source&gt;</c> sibling inside a <c>&lt;picture&gt;</c> whose <c>media</c>
    /// and <c>type</c> both apply, else the element's own attributes (HTML §4.8.1, "the picture
    /// element" — the <c>&lt;img&gt;</c> is the last fallback in that list).
    /// </summary>
    private static (string Srcset, string Sizes, bool FromSourceElement) ResolveSourceSet(CssBox box)
    {
        var element = box.SourceElement;
        if (element?.ParentNode is Dom.DomElement parent
            && parent.LocalName.Equals("picture", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var child in parent.ChildNodes)
            {
                if (ReferenceEquals(child, element))
                    break;
                if (child is not Dom.DomElement source
                    || !source.LocalName.Equals("source", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sourceSrcset = source.GetAttribute("srcset");
                if (string.IsNullOrWhiteSpace(sourceSrcset))
                    continue;

                var type = source.GetAttribute("type");
                if (!string.IsNullOrWhiteSpace(type) && !IsSupportedImageType(type))
                    continue;

                var media = source.GetAttribute("media");
                if (!string.IsNullOrWhiteSpace(media) && !MatchesMedia(media))
                    continue;

                return (sourceSrcset, source.GetAttribute("sizes") ?? string.Empty, true);
            }
        }

        return (box.GetAttribute("srcset"), box.GetAttribute("sizes"), false);
    }

    /// <summary>
    /// Whether a <c>&lt;source type&gt;</c> names an image format the decoder can read. An unknown
    /// type has to disqualify the source — that is the whole point of stating it — so this is a
    /// list of what is decodable rather than a list of what is not.
    /// </summary>
    private static bool IsSupportedImageType(string type)
    {
        var mime = type.Trim();
        int semicolon = mime.IndexOf(';');
        if (semicolon >= 0)
            mime = mime[..semicolon].TrimEnd();

        return mime.ToLowerInvariant() switch
        {
            "image/png" or "image/jpeg" or "image/jpg" or "image/gif" or "image/bmp"
                or "image/svg+xml" or "image/webp" or "image/x-icon" or "image/vnd.microsoft.icon" => true,
            _ => false,
        };
    }

    // ── parse a srcset attribute ─────────────────────────────────────────────────────────────

    private readonly record struct SrcsetCandidate(string Url, double? Width, double? Density);

    /// <summary>
    /// HTML "parse a srcset attribute". A candidate is a URL that contains no leading or trailing
    /// comma, followed by an optional descriptor; commas separate candidates but may also be glued
    /// to the end of a URL, which is what makes this a hand-written scan rather than a split.
    /// </summary>
    private static List<SrcsetCandidate> ParseSrcset(string value)
    {
        var candidates = new List<SrcsetCandidate>();
        int position = 0;

        while (position < value.Length)
        {
            // Leading whitespace and commas belong to the separator, not to the URL.
            while (position < value.Length && (IsHtmlWhitespace(value[position]) || value[position] == ','))
                position++;
            if (position >= value.Length)
                break;

            int urlStart = position;
            while (position < value.Length && !IsHtmlWhitespace(value[position]))
                position++;
            var url = value[urlStart..position];

            string descriptors;
            if (url.EndsWith(','))
            {
                // A comma glued to the URL ends the candidate outright: it has no descriptor.
                url = url.TrimEnd(',');
                descriptors = string.Empty;
            }
            else
            {
                int descriptorStart = position;
                int depth = 0;
                while (position < value.Length)
                {
                    char c = value[position];
                    if (c == '(')
                        depth++;
                    else if (c == ')' && depth > 0)
                        depth--;
                    else if (c == ',' && depth == 0)
                        break;
                    position++;
                }
                descriptors = value[descriptorStart..position];
                if (position < value.Length)
                    position++; // consume the separating comma
            }

            if (url.Length == 0)
                continue;

            if (TryParseDescriptors(descriptors, out double? width, out double? density))
                candidates.Add(new SrcsetCandidate(url, width, density));
        }

        return candidates;
    }

    /// <summary>
    /// The descriptor list of one candidate. At most one <c>w</c> and at most one <c>x</c> may
    /// appear, and they are mutually exclusive; anything else — including the future-compatibility
    /// <c>h</c> descriptor on its own — makes the candidate an error and drops it.
    /// </summary>
    private static bool TryParseDescriptors(string descriptors, out double? width, out double? density)
    {
        width = null;
        density = null;
        bool sawHeight = false;

        foreach (var token in descriptors.Split((char[])[' ', '\t', '\n', '\f', '\r'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            char kind = token[^1];
            var number = token[..^1];
            if (number.Length == 0)
                return false;

            switch (kind)
            {
                case 'w':
                    if (width is not null || density is not null
                        || !double.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out double w)
                        || w <= 0)
                    {
                        return false;
                    }
                    width = w;
                    break;

                case 'x':
                    if (width is not null || density is not null || sawHeight
                        || !TryParseFloatingPoint(number, out double d) || d < 0)
                    {
                        return false;
                    }
                    density = d;
                    break;

                case 'h':
                    // Reserved for a future height descriptor: legal to write, but it may not be
                    // combined with `x`, and on its own it makes the candidate an error.
                    if (density is not null || sawHeight
                        || !double.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out double h)
                        || h <= 0)
                    {
                        return false;
                    }
                    sawHeight = true;
                    break;

                default:
                    return false;
            }
        }

        return !sawHeight || width is not null;
    }

    /// <summary>
    /// HTML "rules for parsing floating-point number values", as far as a descriptor needs them.
    /// A magnitude past <see cref="double"/>'s range is an infinity rather than a parse failure —
    /// <c>9e99999999999999999999999x</c> is a well-formed number that names a density no image can
    /// be shown at, and the reference browser renders it as a zero-sized image rather than as a
    /// broken one.
    /// </summary>
    private static bool TryParseFloatingPoint(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return !double.IsNaN(value);

        // .NET only reports overflow as a failure for a few shapes; treat a syntactically valid
        // number that is simply too large as the infinity it converges to.
        value = 0;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;

        bool negative = trimmed[0] == '-';
        if (trimmed[0] is '+' or '-')
            trimmed = trimmed[1..];

        foreach (char c in trimmed)
        {
            if (!char.IsAsciiDigit(c) && c is not ('.' or 'e' or 'E' or '+' or '-'))
                return false;
        }

        value = negative ? double.NegativeInfinity : double.PositiveInfinity;
        return true;
    }

    // ── parse a sizes attribute ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The <b>source size</b> in CSS pixels: the first <c>sizes</c> entry whose media condition
    /// applies, or <c>100vw</c> when none does. The default is the viewport, not the replaced
    /// element's 300×150 default object size — an <c>&lt;img&gt;</c> with a <c>w</c> srcset and no
    /// <c>sizes</c> is asking to be as wide as the page.
    /// </summary>
    private static double ResolveSourceSize(CssBox box, string sizes)
    {
        double em = box.GetEmHeight();

        foreach (var entry in SplitTopLevel(sizes, ','))
        {
            var components = TokenizeComponentValues(entry);
            TrimTrailingWhitespace(components);
            if (components.Count == 0)
                continue;

            if (!TryResolveSourceSizeValue(components[^1], em, out double size))
                continue;

            components.RemoveAt(components.Count - 1);
            TrimTrailingWhitespace(components);

            // A bare length is the unconditional entry and ends the search wherever it sits, so
            // `sizes="1px, 100vw"` is 1px and `sizes="100vw, 1px"` is 100vw.
            if (components.Count == 0)
                return size;

            var condition = Concat(components);
            if (IsMediaCondition(condition) && MatchesMedia(condition))
                return size;
        }

        return ResolveLength(DefaultSourceSize, em);
    }

    /// <summary>
    /// Whether one component value is a valid non-negative <c>&lt;source-size-value&gt;</c>: a
    /// non-negative <c>&lt;length&gt;</c>, or a math function that resolves to one. A percentage is
    /// not one (there is nothing for it to resolve against at this point in the algorithm), a
    /// unitless non-zero number is not one, and no other function — <c>var()</c>, <c>attr()</c>,
    /// <c>toggle()</c> — is one either.
    /// </summary>
    private static bool TryResolveSourceSizeValue(ComponentValue component, double em, out double size)
    {
        size = 0;
        var text = component.Text.Trim();
        if (text.Length == 0)
            return false;

        double resolved;
        if (component.Kind == ComponentKind.Function)
        {
            // A math function's value cannot be read off its text, so validity is the length
            // parser's answer: a `calc(1px / 0)` has no <length> value and is a parse error, and
            // without asking it would come back as the 0 an unparseable length resolves to.
            if (!IsMathFunction(component.Name) || !CSS.CssLengthParser.IsValidLength(text))
                return false;
            resolved = ResolveLength(text, em);
        }
        else if (component.Kind == ComponentKind.Numeric)
        {
            if (!TryResolveDimension(text, em, out resolved))
                return false;
        }
        else
        {
            return false;
        }

        if (!double.IsFinite(resolved) || resolved < 0)
            return false;

        size = resolved;
        return true;
    }

    /// <summary>
    /// The math functions a source size may be written as. This is deliberately the set
    /// <see cref="CSS.CssLengthParser"/> evaluates rather than the full CSS Values 4 list:
    /// <c>clamp()</c> and the stepped/exponential functions are not among them, so a <c>sizes</c>
    /// entry written with one is a parse error here and the attribute falls through to its next
    /// entry. Teaching the length parser those functions is a change to the CSS engine, not to this
    /// algorithm, and it would fix them for every property at once.
    /// </summary>
    private static bool IsMathFunction(string name) => name.ToLowerInvariant() switch
    {
        "calc" or "min" or "max" => true,
        _ => false,
    };

    /// <summary>The CSS length units a source size may be written in — every unit but <c>%</c>.</summary>
    private static readonly string[] LengthUnits =
    [
        "dvmin", "dvmax", "lvmin", "lvmax", "svmin", "svmax", "cqmin", "cqmax",
        "vmin", "vmax", "rem", "rlh",
        "dvw", "dvh", "lvw", "lvh", "svw", "svh", "dvi", "dvb", "lvi", "lvb", "svi", "svb",
        "cqw", "cqh", "cqi", "cqb",
        "em", "ex", "ch", "ic", "lh", "px", "cm", "mm", "in", "pt", "pc", "vw", "vh", "vi", "vb", "q",
    ];

    /// <summary>
    /// Resolves a dimension component value — a number and a unit — in CSS pixels, or reports it as
    /// no <c>&lt;length&gt;</c> at all.
    /// </summary>
    /// <remarks>
    /// The number is parsed here rather than handed to the length parser whole, because CSS numbers
    /// carry scientific notation (<c>0.2e1px</c>, <c>.4E1px</c>, <c>+0.11e+01px</c> — WPT spells all
    /// three) and the parser's own scanner does not read an exponent. Every CSS length unit is a
    /// fixed multiple of a pixel, so the unit is resolved once at <c>1</c> and scaled, which keeps
    /// one definition of what each unit is worth.
    /// </remarks>
    private static bool TryResolveDimension(string text, double em, out double pixels)
    {
        pixels = 0;

        int unitStart = text.Length;
        while (unitStart > 0 && (char.IsAsciiLetter(text[unitStart - 1]) || text[unitStart - 1] == '%'))
            unitStart--;

        var number = text[..unitStart];
        var unit = text[unitStart..];

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.IsNaN(value))
        {
            return false;
        }

        // CSS Values §4.3.2: the unit identifier is optional after a zero, and required otherwise.
        // A percentage is not a <length>, so it is not a source size however it is written.
        if (unit.Length == 0)
            return value == 0;

        if (!IsLengthUnit(unit))
            return false;

        pixels = value * ResolveLength("1" + unit, em);
        return true;
    }

    private static bool IsLengthUnit(string unit)
    {
        foreach (var known in LengthUnits)
        {
            if (unit.Equals(known, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static double ResolveLength(string text, double em)
    {
        try
        {
            return CSS.CssLengthParser.ParseLength(text, 0, em);
        }
        catch
        {
            return double.NaN;
        }
    }

    // ── media conditions ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the text preceding a source size parses as a <c>&lt;media-condition&gt;</c>. A media
    /// condition is a parenthesised feature test, optionally negated or combined — it is
    /// specifically <em>not</em> a full media query, so a media type (<c>print</c>, <c>all</c>,
    /// <c>unknown-media-type</c>) makes the entry a parse error rather than a query that happens to
    /// match. Checking that what is left after the leading <c>not</c>s opens with <c>(</c> is
    /// exactly that distinction.
    /// </summary>
    private static bool IsMediaCondition(string condition)
    {
        var text = condition.Trim();
        while (text.StartsWith("not", StringComparison.OrdinalIgnoreCase)
               && text.Length > 3 && IsHtmlWhitespace(text[3]))
        {
            text = text[3..].TrimStart();
        }

        return text.StartsWith('(');
    }

    private static bool MatchesMedia(string condition)
    {
        try
        {
            return CSS.Dom.CssStyleEngine.MatchesMediaQuery(condition, CurrentEnvironment());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The viewport the media conditions are evaluated against, read back out of the length parser
    /// the whole render already resolves <c>vw</c>/<c>vh</c> through — so a <c>(min-width: …)</c>
    /// and a <c>100vw</c> in the same <c>sizes</c> attribute cannot disagree about how wide the page is.
    /// </summary>
    private static CSS.Dom.CssEnvironment CurrentEnvironment() => new(
        (int)Math.Round(CSS.CssLengthParser.ParseLength("100vw", 0, CSS.CssMetrics.DefaultFontSizePx)),
        (int)Math.Round(CSS.CssLengthParser.ParseLength("100vh", 0, CSS.CssMetrics.DefaultFontSizePx)));

    // ── a CSS component-value scanner, enough for `sizes` ────────────────────────────────────

    private enum ComponentKind { Whitespace, Numeric, Ident, Function, Block, String, Delim }

    private readonly record struct ComponentValue(ComponentKind Kind, string Text, string Name);

    private static void TrimTrailingWhitespace(List<ComponentValue> components)
    {
        while (components.Count > 0 && components[^1].Kind == ComponentKind.Whitespace)
            components.RemoveAt(components.Count - 1);
    }

    private static string Concat(List<ComponentValue> components)
    {
        var builder = new StringBuilder();
        foreach (var component in components)
            builder.Append(component.Kind == ComponentKind.Whitespace ? " " : component.Text);
        return builder.ToString();
    }

    /// <summary>
    /// Splits on a top-level separator, with CSS's nesting rules: a separator inside a block, a
    /// function, a string or an escape does not separate anything. Comments are not stripped here —
    /// <see cref="TokenizeComponentValues"/> drops them, and it has to, because a comment is not
    /// whitespace: <c>1/* */px</c> is a number and an identifier, not the length <c>1px</c>.
    /// </summary>
    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            parts.Add(string.Empty);
            return parts;
        }

        int start = 0;
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                i++;
                continue;
            }
            if (c is '"' or '\'')
            {
                i = SkipString(text, i);
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i = SkipComment(text, i);
                continue;
            }
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth > 0)
                    depth--;
            }
            else if (c == separator && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>
    /// The component values of one <c>sizes</c> entry. This is a reduced CSS tokenizer: it needs to
    /// tell a dimension from an identifier, keep a function and its arguments together, swallow an
    /// unclosed block to the end of the input the way CSS does, and drop comments without leaving
    /// whitespace behind. It does not need to classify anything further, because the only two
    /// questions asked of the result are "is the last one a length?" and "is what precedes it a
    /// media condition?".
    /// </summary>
    private static List<ComponentValue> TokenizeComponentValues(string text)
    {
        var components = new List<ComponentValue>();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i = SkipComment(text, i) + 1;
                continue;
            }

            if (IsHtmlWhitespace(c))
            {
                int start = i;
                while (i < text.Length && IsHtmlWhitespace(text[i]))
                    i++;
                components.Add(new ComponentValue(ComponentKind.Whitespace, text[start..i], string.Empty));
                continue;
            }

            if (c is '"' or '\'')
            {
                int start = i;
                i = SkipString(text, i) + 1;
                components.Add(new ComponentValue(ComponentKind.String, text[start..Math.Min(i, text.Length)], string.Empty));
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                int start = i;
                i = SkipBlock(text, i, out var closers);
                components.Add(new ComponentValue(ComponentKind.Block, text[start..i] + closers, string.Empty));
                continue;
            }

            if (StartsNumber(text, i))
            {
                int start = i;
                i = ScanNumber(text, i);
                // A number followed directly by an identifier is a dimension, and by `%` a
                // percentage; both stay one component value.
                if (i < text.Length && text[i] == '%')
                    i++;
                else if (StartsIdent(text, i))
                    i = ScanIdent(text, i);
                components.Add(new ComponentValue(ComponentKind.Numeric, text[start..i], string.Empty));
                continue;
            }

            if (StartsIdent(text, i))
            {
                int start = i;
                i = ScanIdent(text, i);
                if (i < text.Length && text[i] == '(')
                {
                    var name = text[start..i];
                    i = SkipBlock(text, i, out var closers);
                    components.Add(new ComponentValue(ComponentKind.Function, text[start..i] + closers, name));
                }
                else
                {
                    components.Add(new ComponentValue(ComponentKind.Ident, text[start..i], string.Empty));
                }
                continue;
            }

            components.Add(new ComponentValue(ComponentKind.Delim, text[i].ToString(), string.Empty));
            i++;
        }

        return components;
    }

    /// <summary>Index of the closing <c>*/</c>'s second character, or the last index when unterminated.</summary>
    private static int SkipComment(string text, int start)
    {
        int close = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
        return close < 0 ? text.Length - 1 : close + 1;
    }

    /// <summary>Index of the closing quote, or the last index when unterminated.</summary>
    private static int SkipString(string text, int start)
    {
        char quote = text[start];
        for (int i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }
            if (text[i] == quote)
                return i;
        }
        return text.Length - 1;
    }

    /// <summary>
    /// Index just past the block opened at <paramref name="start"/>, with
    /// <paramref name="missingClosers"/> set to the closers an unterminated block never got.
    /// </summary>
    /// <remarks>
    /// CSS closes an unterminated block at the end of the input rather than discarding it, which is
    /// why <c>sizes="calc(1px"</c> is the length <c>1px</c> — and, on the other side of the same
    /// rule, why <c>sizes="((),1px"</c> is one block that swallows the comma and is therefore not a
    /// length at all.
    /// </remarks>
    private static int SkipBlock(string text, int start, out string missingClosers)
    {
        var open = new Stack<char>();
        open.Push(CloserFor(text[start]));

        for (int i = start + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                i++;
                continue;
            }
            if (c is '"' or '\'')
            {
                i = SkipString(text, i);
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i = SkipComment(text, i);
                continue;
            }
            if (c is '(' or '[' or '{')
            {
                open.Push(CloserFor(c));
                continue;
            }
            if (c == open.Peek())
            {
                open.Pop();
                if (open.Count == 0)
                {
                    missingClosers = string.Empty;
                    return i + 1;
                }
            }
        }

        var builder = new StringBuilder(open.Count);
        while (open.Count > 0)
            builder.Append(open.Pop());
        missingClosers = builder.ToString();
        return text.Length;
    }

    private static char CloserFor(char opener) => opener switch { '(' => ')', '[' => ']', _ => '}' };

    private static bool StartsNumber(string text, int i)
    {
        char c = text[i];
        if (char.IsAsciiDigit(c))
            return true;
        if (c == '.')
            return i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]);
        if (c is '+' or '-')
        {
            if (i + 1 >= text.Length)
                return false;
            if (char.IsAsciiDigit(text[i + 1]))
                return true;
            return text[i + 1] == '.' && i + 2 < text.Length && char.IsAsciiDigit(text[i + 2]);
        }
        return false;
    }

    private static int ScanNumber(string text, int i)
    {
        if (i < text.Length && text[i] is '+' or '-')
            i++;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
            i++;
        if (i < text.Length && text[i] == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
                i++;
        }

        // An exponent only belongs to the number when it is complete: `1e1.5px` is the number `1e1`
        // followed by the dimension `.5px`, not one malformed token.
        int exponent = i;
        if (exponent < text.Length && (text[exponent] is 'e' or 'E'))
        {
            exponent++;
            if (exponent < text.Length && text[exponent] is '+' or '-')
                exponent++;
            if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
            {
                while (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
                    exponent++;
                i = exponent;
            }
        }

        return i;
    }

    private static bool StartsIdent(string text, int i)
    {
        if (i >= text.Length)
            return false;

        char c = text[i];
        if (c == '\\')
            return true;
        if (c == '-')
        {
            if (i + 1 >= text.Length)
                return false;
            char next = text[i + 1];
            return next == '-' || next == '\\' || IsIdentStart(next);
        }
        return IsIdentStart(c);
    }

    private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_' || c >= 0x80;

    private static int ScanIdent(string text, int i)
    {
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c >= 0x80)
            {
                i++;
                continue;
            }
            break;
        }
        return Math.Min(i, text.Length);
    }

    private static bool IsHtmlWhitespace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';
}
