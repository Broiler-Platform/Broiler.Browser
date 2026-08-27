using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Broiler.Layout.IR;

/// <summary>
/// A 2D affine transform parsed from an SVG <c>transform</c>-list attribute
/// (<c>translate</c>/<c>scale</c>/<c>rotate</c>/<c>skewX</c>/<c>skewY</c>/<c>matrix</c>,
/// SVG 1.1 §7.6), in the column-vector convention CSS <c>matrix(a,b,c,d,e,f)</c> uses.
/// </summary>
/// <remarks>
/// Kept as a value rather than folded into the caller so a <c>patternTransform</c> and an element
/// <c>transform</c> parse the same way, and so the inverse — which a tiling needs to know which
/// tiles can reach the shape — is written once.
/// </remarks>
internal readonly partial record struct SvgTransform(float A, float B, float C, float D, float E, float F)
{
    public static SvgTransform Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => this == Identity;

    /// <summary>Parses a transform list; an absent or unparseable list is the identity.</summary>
    public static SvgTransform Parse(string? transformList)
    {
        TryParse(transformList, out var transform);
        return transform;
    }

    /// <summary>
    /// Parses a transform list, reporting separately whether <em>any</em> function in it was
    /// recognised. The transform alone cannot say: an unparseable list and a list that genuinely
    /// resolves to the identity — <c>rotate(0deg)</c>, <c>scale(1)</c> — both come out as
    /// <see cref="Identity"/>, and a caller deciding whether a declaration beats a presentation
    /// attribute has to tell those apart. CSS Transforms 1 §3 drops an invalid declaration whole,
    /// so the attribute stands; a valid one that happens to be the identity still wins.
    /// </summary>
    public static bool TryParse(string? transformList, out SvgTransform transform)
    {
        transform = Identity;
        if (string.IsNullOrWhiteSpace(transformList))
            return false;

        var result = Identity;
        bool any = false;

        foreach (Match m in FunctionRegex().Matches(transformList))
        {
            float[] a = ParseNumbers(m.Groups["args"].Value);
            SvgTransform step;
            switch (m.Groups["name"].Value.ToLowerInvariant())
            {
                case "translate" when a.Length >= 1:
                    step = new SvgTransform(1, 0, 0, 1, a[0], a.Length > 1 ? a[1] : 0);
                    break;
                case "scale" when a.Length >= 1:
                    step = new SvgTransform(a[0], 0, 0, a.Length > 1 ? a[1] : a[0], 0, 0);
                    break;
                case "rotate" when a.Length >= 1:
                    step = Rotate(a[0], a.Length >= 3 ? a[1] : 0, a.Length >= 3 ? a[2] : 0);
                    break;
                case "skewx" when a.Length >= 1:
                    step = new SvgTransform(1, 0, (float)Math.Tan(a[0] * Math.PI / 180), 1, 0, 0);
                    break;
                case "skewy" when a.Length >= 1:
                    step = new SvgTransform(1, (float)Math.Tan(a[0] * Math.PI / 180), 0, 1, 0, 0);
                    break;
                case "matrix" when a.Length >= 6:
                    step = new SvgTransform(a[0], a[1], a[2], a[3], a[4], a[5]);
                    break;
                default:
                    continue;
            }

            result = result.Concat(step);
            any = true;
        }

        if (any)
            transform = result;
        return any;
    }

    /// <summary><c>rotate(angle, cx, cy)</c>: a rotation about an arbitrary centre.</summary>
    private static SvgTransform Rotate(float degrees, float cx, float cy)
    {
        double radians = degrees * Math.PI / 180;
        float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
        return new SvgTransform(
            cos, sin, -sin, cos,
            cx - cx * cos + cy * sin,
            cy - cx * sin - cy * cos);
    }

    /// <summary>This transform followed by <paramref name="inner"/> applied first (<c>this · inner</c>).</summary>
    public SvgTransform Concat(SvgTransform inner) => new(
        A * inner.A + C * inner.B,
        B * inner.A + D * inner.B,
        A * inner.C + C * inner.D,
        B * inner.C + D * inner.D,
        A * inner.E + C * inner.F + E,
        B * inner.E + D * inner.F + F);

    /// <summary>The inverse, or the identity when the matrix is singular (a degenerate scale).</summary>
    public SvgTransform Inverse()
    {
        float determinant = A * D - B * C;
        if (Math.Abs(determinant) < 1e-9f)
            return Identity;

        float ia = D / determinant, ib = -B / determinant;
        float ic = -C / determinant, id = A / determinant;
        return new SvgTransform(ia, ib, ic, id, -(ia * E + ic * F), -(ib * E + id * F));
    }

    public PointF Map(float x, float y) => new(A * x + C * y + E, B * x + D * y + F);

    /// <summary>The axis-aligned bounding box of a mapped rectangle.</summary>
    public RectangleF MapBounds(RectangleF rect)
    {
        if (IsIdentity)
            return rect;

        PointF p0 = Map(rect.Left, rect.Top), p1 = Map(rect.Right, rect.Top);
        PointF p2 = Map(rect.Right, rect.Bottom), p3 = Map(rect.Left, rect.Bottom);
        float left = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        float right = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        float top = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        float bottom = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return new RectangleF(left, top, right - left, bottom - top);
    }

    /// <summary>Whether the transform maps axis-aligned rectangles to axis-aligned rectangles.</summary>
    public bool IsAxisAligned => Math.Abs(B) < 1e-6f && Math.Abs(C) < 1e-6f;

    /// <summary>
    /// The same transform expressed in page pixels: conjugated by the view-box mapping
    /// (scale <paramref name="sx"/>/<paramref name="sy"/> about the user-space origin, whose page
    /// position is <paramref name="originX"/>/<paramref name="originY"/>), so that applying it to a
    /// page coordinate has the effect of applying this transform to the user coordinate behind it.
    /// </summary>
    public SvgTransform ToPageSpace(float sx, float sy, float originX, float originY)
    {
        if (IsIdentity || sx == 0 || sy == 0)
            return Identity;

        float a = A, b = B * sy / sx, c = C * sx / sy, d = D;
        return new SvgTransform(
            a, b, c, d,
            E * sx + originX - (a * originX + c * originY),
            F * sy + originY - (b * originX + d * originY));
    }

    [GeneratedRegex(@"(?<name>matrix|translate|scale|rotate|skewX|skewY)\s*\(\s*(?<args>[^)]*)\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")]
    private static partial Regex NumberRegex();

    private static float[] ParseNumbers(string text)
    {
        var matches = NumberRegex().Matches(text);
        var values = new float[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            values[i] = float.TryParse(
                matches[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }
        return values;
    }
}
