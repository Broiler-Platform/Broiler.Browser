using System.Drawing;

namespace Broiler.Layout;

/// <summary>
/// HTML element/attribute name constants used by layout (tag dispatch, link
/// detection). This is the single source for the names shared with the renderer:
/// the renderer's <c>Broiler.HTML.Utils.HtmlConstants</c> forwards its
/// <c>A</c>/<c>Hr</c>/<c>Iframe</c>/<c>Img</c>/<c>Href</c> members here
/// (measurement-dedup roadmap M6), since it already references this lower layer.
/// </summary>
public static class HtmlConstants
{
    public const string A = "a";
    public const string Hr = "hr";
    public const string Iframe = "iframe";
    public const string Img = "img";
    public const string Href = "href";
}

/// <summary>
/// Small layout helpers ported from the renderer's
/// <c>Broiler.HTML.Utils.CommonUtils</c>. List-marker number formatting
/// (<c>ConvertToAlphaNumber</c>) stays host-side and is reached via
/// <see cref="ILayoutEnvironment.FormatListMarker"/>.
/// </summary>
public static class CommonUtils
{
    /// <summary>True for CJK-range characters (used as inter-character line-break opportunities).</summary>
    public static bool IsAsianCharecter(char ch) => ch >= 0x4e00 && ch <= 0xFA2D;

    /// <summary>Component-wise maximum of two sizes.</summary>
    public static SizeF Max(SizeF size, SizeF other)
        => new(Math.Max(size.Width, other.Width), Math.Max(size.Height, other.Height));
}