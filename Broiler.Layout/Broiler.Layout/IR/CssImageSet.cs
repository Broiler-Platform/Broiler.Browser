using System;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Layout.IR;

/// <summary>
/// CSS Images 4 §5.4 <c>image-set()</c>: picks the one option a device should load out of a set
/// offered at several resolutions and types, and hands back the plain <c>&lt;image&gt;</c> it names.
/// </summary>
/// <remarks>
/// <para>
/// Resolving it here rather than at the point of loading is what makes it work for every kind of
/// image the function can hold. An <c>image-set()</c> layer reached the background loader, which
/// looks for <c>url(</c> and found a function it did not know, so the layer loaded nothing; and it
/// reached the paint walker, which looks for <c>gradient(</c> to decide whether a layer is drawn
/// rather than fetched — so <c>image-set(linear-gradient(…) 1x)</c> was neither. Unwrapping the
/// value before either of them looks at it leaves both reading exactly what they already
/// understand.
/// </para>
/// <para>
/// <b>Invalid and "selects nothing" are different answers</b>, and two WPT tests are built to tell
/// them apart. A negative resolution is a parse error, so the whole declaration is dropped and the
/// previous one applies (<c>image-set-negative-resolution-rendering-2</c> puts a green
/// <c>url()</c> behind it and expects green). A zero resolution parses, but no option survives it,
/// so the declaration applies and paints nothing — <c>image-set-zero-resolution-rendering</c> puts
/// a <em>red</em> <c>url()</c> behind it and expects a blank page.
/// </para>
/// </remarks>
internal static class CssImageSet
{
    /// <summary>1x, in the units each <c>&lt;resolution&gt;</c> unit measures.</summary>
    private const double DotsPerInchAt1x = 96.0;
    private const double DotsPerCentimetreAt1x = 96.0 / 2.54;

    /// <summary>
    /// The image types this engine can decode, which is what <c>type()</c> is asked about. An
    /// option naming anything else is skipped — that is the point of the modifier, and
    /// <c>image-set-type-skip-unsupported-rendering</c> offers a red one first to check it happens.
    /// </summary>
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/gif", "image/bmp", "image/x-icon",
        "image/vnd.microsoft.icon", "image/webp", "image/svg+xml", "image/apng",
    };

    /// <summary>Whether <paramref name="value"/> is an <c>image-set()</c> function — the prefixed
    /// spelling included, which is still what a good deal of the web ships.</summary>
    internal static bool IsImageSet(string? value) =>
        TryGetArguments(value, out _);

    /// <summary>
    /// The device resolution the renderer draws at. Page zoom is baked into used values before
    /// paint rather than carried as a device ratio, so this is 1 — and it is named rather than
    /// written as a literal because it is the input the selection is *about*.
    /// </summary>
    internal const double RendererDevicePixelRatio = 1.0;

    /// <summary>
    /// Resolves every <c>image-set()</c> layer of a comma-separated image property, leaving every
    /// other layer byte-identical. Returns <see langword="false"/> when any layer is a parse error,
    /// which invalidates the declaration as a whole (CSS Syntax 3 §9: a declaration is dropped
    /// whole or not at all) — the caller then leaves the property at the value cascaded under it.
    /// </summary>
    internal static bool TryResolveLayers(string? value, out string resolved)
    {
        resolved = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf("image-set(", StringComparison.OrdinalIgnoreCase) < 0)
            return true;

        var layers = SplitTopLevel(value, ',');
        var rewritten = new List<string>(layers.Count);
        bool changed = false;

        foreach (var layer in layers)
        {
            if (!IsImageSet(layer))
            {
                rewritten.Add(layer);
                continue;
            }

            if (!TryResolve(layer, RendererDevicePixelRatio, out var image))
                return false;

            // A function that selects nothing is a valid image that paints nothing, which is what
            // `none` already means to every reader of this value.
            rewritten.Add(image ?? "none");
            changed = true;
        }

        if (changed)
            resolved = string.Join(", ", rewritten);
        return true;
    }

    /// <summary>
    /// Resolves an <c>image-set()</c> layer to the <c>&lt;image&gt;</c> it selects for a display of
    /// <paramref name="devicePixelRatio"/>. Returns <see langword="false"/> when the function is a
    /// parse error and the declaration carrying it must be dropped whole; <see langword="true"/>
    /// with <paramref name="image"/> <see langword="null"/> when it parses but no option survives.
    /// </summary>
    internal static bool TryResolve(string? value, double devicePixelRatio, out string? image)
    {
        image = null;
        if (!TryGetArguments(value, out var arguments))
            return false;

        string? best = null;
        double bestResolution = 0;

        foreach (var option in SplitTopLevel(arguments, ','))
        {
            if (!TryParseOption(option, out var candidate, out double resolution, out bool usable))
                return false;   // a parse error anywhere invalidates the whole function

            if (!usable)
                continue;

            // CSS Images 4 leaves the choice to the UA; every engine takes the smallest resolution
            // that is at least the device's, and the largest on offer when none is. Ties go to the
            // first, which `image-set-first-match-rendering` states by offering two at 1x.
            if (best is null || Better(resolution, bestResolution, devicePixelRatio))
            {
                best = candidate;
                bestResolution = resolution;
            }
        }

        image = best;
        return true;
    }

    /// <summary>Whether <paramref name="candidate"/> is a better match for
    /// <paramref name="devicePixelRatio"/> than <paramref name="incumbent"/>.</summary>
    private static bool Better(double candidate, double incumbent, double devicePixelRatio)
    {
        bool candidateReaches = candidate >= devicePixelRatio;
        bool incumbentReaches = incumbent >= devicePixelRatio;

        if (candidateReaches != incumbentReaches)
            return candidateReaches;

        // Both above the device's resolution: the smaller wins, being the least oversampled. Both
        // below it: the larger wins, being the least blurred. Equal: the incumbent came first.
        return candidateReaches ? candidate < incumbent : candidate > incumbent;
    }

    /// <summary>
    /// Parses one option — an <c>&lt;image&gt;</c> with an optional <c>&lt;resolution&gt;</c> and an
    /// optional <c>type()</c>, in either order. <paramref name="usable"/> is <see langword="false"/>
    /// for an option that parses but this device cannot take: an unsupported type, an empty URL, or
    /// a zero resolution. A <see langword="false"/> return is a parse error.
    /// </summary>
    private static bool TryParseOption(string option, out string? image, out double resolution, out bool usable)
    {
        image = null;
        resolution = 1.0;   // an omitted resolution is 1x — `image-set-no-res-rendering`
        usable = false;

        bool sawResolution = false;
        bool sawType = false;
        bool typeSupported = true;

        foreach (var token in SplitTopLevel(option, ' '))
        {
            if (token.Length == 0)
                continue;

            if (StartsWithFunction(token, "type", out var typeArgument))
            {
                if (sawType)
                    return false;
                sawType = true;
                typeSupported = SupportedTypes.Contains(Unquote(typeArgument).Trim());
                continue;
            }

            if (TryParseResolution(token, out double parsed))
            {
                if (sawResolution)
                    return false;
                sawResolution = true;
                if (parsed < 0)
                    return false;   // negative: a parse error, not an unusable option
                resolution = parsed;
                continue;
            }

            if (image is not null)
                return false;       // two images in one option

            // `<string>` is an image in this function, the same as wrapping it in url() —
            // `image-set-no-url-rendering` writes it that way.
            image = token.Length > 1 && (token[0] == '"' || token[0] == '\'')
                ? "url(" + token + ")"
                : token;
        }

        if (image is null)
            return false;

        usable = typeSupported && resolution > 0 && !IsEmptyUrl(image);
        return true;
    }

    /// <summary>An option whose URL is empty names no image, so it is skipped rather than
    /// selected — <c>image-set-empty-url-rendering</c> offers one at 2x behind a green 1x.</summary>
    private static bool IsEmptyUrl(string image) =>
        StartsWithFunction(image, "url", out var argument) && Unquote(argument).Trim().Length == 0;

    /// <summary>
    /// A <c>&lt;resolution&gt;</c> in multiples of 1x, or <see langword="false"/> when the token is
    /// not one. <c>calc()</c> is evaluated only far enough to cover a resolution expression, which
    /// is a product or quotient of one resolution and plain numbers — <c>calc(0.5x * 2)</c>.
    /// </summary>
    private static bool TryParseResolution(string token, out double resolution)
    {
        resolution = 0;

        if (StartsWithFunction(token, "calc", out var expression))
            return TryEvaluateResolutionProduct(expression, out resolution);

        return TryParseResolutionLiteral(token, out resolution);
    }

    private static bool TryParseResolutionLiteral(string token, out double resolution)
    {
        resolution = 0;
        var (number, unit) = SplitNumberAndUnit(token);
        if (number is null || unit.Length == 0)
            return false;

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return false;

        switch (unit.ToLowerInvariant())
        {
            case "x":
            case "dppx":
                resolution = value;
                return true;
            case "dpi":
                resolution = value / DotsPerInchAt1x;
                return true;
            case "dpcm":
                resolution = value / DotsPerCentimetreAt1x;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Evaluates <c>calc()</c> over a single resolution and plain numbers, left to right with no
    /// precedence — which is all a resolution expression can be, because multiplying two
    /// resolutions is not one. Anything else is declined and the token is then not a resolution.
    /// </summary>
    private static bool TryEvaluateResolutionProduct(string expression, out double resolution)
    {
        resolution = 0;
        var tokens = SplitTopLevel(expression, ' ');
        if (tokens.Count == 0)
            return false;

        double? accumulator = null;
        bool sawResolutionTerm = false;
        string pendingOperator = "*";

        foreach (var token in tokens)
        {
            if (token is "*" or "/")
            {
                pendingOperator = token;
                continue;
            }

            if (token is "+" or "-")
                return false;   // adding a resolution to a number is not a resolution

            double term;
            if (TryParseResolutionLiteral(token, out double asResolution))
            {
                if (sawResolutionTerm)
                    return false;
                sawResolutionTerm = true;
                term = asResolution;
            }
            else if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out term))
            {
                return false;
            }

            if (accumulator is null)
                accumulator = term;
            else if (pendingOperator == "*")
                accumulator *= term;
            else if (term == 0)
                return false;
            else
                accumulator /= term;
        }

        if (accumulator is null || !sawResolutionTerm)
            return false;

        resolution = accumulator.Value;
        return true;
    }

    private static (string? Number, string Unit) SplitNumberAndUnit(string token)
    {
        int i = 0;
        if (i < token.Length && (token[i] == '+' || token[i] == '-'))
            i++;
        int digitsStart = i;
        while (i < token.Length && (char.IsAsciiDigit(token[i]) || token[i] == '.'))
            i++;
        if (i == digitsStart)
            return (null, string.Empty);

        // An exponent is part of the number, not the unit: `1e2dppx` is a hundred.
        if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
        {
            int save = i;
            i++;
            if (i < token.Length && (token[i] == '+' || token[i] == '-'))
                i++;
            int expStart = i;
            while (i < token.Length && char.IsAsciiDigit(token[i]))
                i++;
            if (i == expStart)
                i = save;
        }

        return (token[..i], token[i..]);
    }

    /// <summary>The arguments of an <c>image-set()</c> function, or <see langword="false"/> when
    /// <paramref name="value"/> is not one.</summary>
    private static bool TryGetArguments(string? value, out string arguments)
    {
        arguments = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return StartsWithFunction(trimmed, "image-set", out arguments)
            || StartsWithFunction(trimmed, "-webkit-image-set", out arguments);
    }

    /// <summary>Whether <paramref name="value"/> is the named function, and its argument text.</summary>
    private static bool StartsWithFunction(string value, string name, out string arguments)
    {
        arguments = string.Empty;
        if (value.Length < name.Length + 2
            || !value.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            || value[name.Length] != '('
            || value[^1] != ')')
            return false;

        arguments = value[(name.Length + 1)..^1];
        return true;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\''))
            ? trimmed[1..^1]
            : trimmed;
    }

    /// <summary>
    /// Splits on <paramref name="separator"/> at nesting depth zero, so a nested function — a
    /// gradient, a <c>url()</c> holding a comma, a <c>calc()</c> — stays in one piece. A space
    /// separator collapses runs of whitespace.
    /// </summary>
    private static List<string> SplitTopLevel(string value, char separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        bool inString = false;
        char quote = '\0';

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (inString)
            {
                if (c == quote)
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    inString = true;
                    quote = c;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    if (depth > 0)
                        depth--;
                    break;
                default:
                    if (depth == 0 && Separates(c, separator))
                    {
                        Add(value[start..i]);
                        start = i + 1;
                    }
                    break;
            }
        }

        Add(value[start..]);
        return parts;

        static bool Separates(char c, char separator) =>
            separator == ' ' ? char.IsWhiteSpace(c) : c == separator;

        void Add(string part)
        {
            part = part.Trim();
            if (part.Length > 0 || separator != ' ')
                parts.Add(part);
        }
    }
}
