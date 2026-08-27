using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// HTML §15.5.13 — the <c>&lt;fieldset&gt;</c> element's rendered legend.
/// </summary>
/// <remarks>
/// <para>
/// A fieldset's first <c>&lt;legend&gt;</c> child is not laid out in the fieldset's content. It is
/// the <em>rendered legend</em>, and it belongs to the block-start border: its margin box is centred
/// on that border, so a legend taller than the border stands proud of the fieldset's border box and
/// the fieldset's content begins below it rather than below the border.
/// </para>
/// <para>
/// WPT's <c>css-break/fieldset-001</c> is written to pin exactly that, and it says so by
/// construction — its reference states the same layout with a <c>&lt;p&gt;</c>, a
/// <c>margin-top</c> that makes room for the part of the legend standing above, and an absolutely
/// positioned legend at a negative <c>top</c>. Reading the geometry back out of that reference is
/// where the rule below comes from: a 49px legend margin box on a 6px border is placed at
/// <c>6/2 − 49/2 = −21.5</c>, and the content that follows starts at <c>6/2 + 49/2 = 27.5</c> plus
/// the fieldset's own padding.
/// </para>
/// <para>
/// The legend's inline size is the other half of the rule and is resolved earlier, in
/// <c>ResolveBlockUsedWidth</c>: an <c>auto</c> inline size is the <em>fit-content</em> inline
/// size, so the legend shrink-wraps rather than stretching to the fieldset's content width the way
/// an ordinary block child would. <see cref="IsRenderedLegend"/> is what that branch asks.
/// </para>
/// <para>
/// Applied after the children are laid out, because the legend's margin box is only measured then —
/// the same shape as every other post-layout placement here. What is <em>not</em> done is the
/// notch: the block-start border is still painted behind the legend, where it should stop at the
/// legend's margin box and resume after it. Nor is the block-size rule that goes with a
/// non-<c>auto</c> <c>block-size</c> (subtract the part of the legend's margin box that spills past
/// the border), which is what WPT's <c>fieldset-block-size</c> asks for.
/// </para>
/// </remarks>
internal partial class CssBox
{
    /// <summary>Moves the rendered legend onto the block-start border and the rest below it.</summary>
    private void ApplyFieldsetLegendPlacement()
    {
        if (!IsFieldset || RenderedLegend() is not { } legend)
            return;

        double border = ActualBorderTopWidth;
        double legendBox = legend.ActualMarginTop
            + (legend.ActualBottom - legend.Location.Y)
            + legend.ActualMarginBottom;

        if (legendBox <= 0)
            return;

        double marginBoxBottom = legend.Location.Y - legend.ActualMarginTop + legendBox;

        // Centred on the border, which puts it above the border box whenever it is the taller.
        double moveLegend = Location.Y + (border - legendBox) / 2 + legend.ActualMarginTop
            - legend.Location.Y;

        if (Math.Abs(moveLegend) > 0.01)
            legend.OffsetTop(moveLegend);

        // The content starts below whichever of the border and the legend reaches further down —
        // the legend's margin-box bottom is `border/2 + legendBox/2` by the centring above.
        double contentTop = Location.Y + Math.Max(border, (border + legendBox) / 2) + ActualPaddingTop;
        double moveRest = contentTop - marginBoxBottom;

        if (Math.Abs(moveRest) <= 0.01)
            return;

        foreach (var child in Boxes)
        {
            if (ReferenceEquals(child, legend)
                || child.Display == CssConstants.None
                || child.Position is CssConstants.Absolute or CssConstants.Fixed)
            {
                continue;
            }

            child.OffsetTop(moveRest);
        }

        // The children moved, so the auto height measured from them is stale by the same amount.
        // MarginBottomCollapse cannot be re-run to find out — it only ever grows ActualBottom
        // (`Math.Max(ActualBottom, …)`), and the move that matters here is upwards: the legend had
        // been laid out in the flow, so the fieldset was left as tall as if the border carried the
        // content *and* a legend-sized block below it. WPT's `legend-block-position-centering`
        // rendered a 100px-bordered fieldset 315px tall where every engine draws 218.
        //
        // Every in-flow child shifted by exactly `moveRest`, and the legend itself never reaches
        // below `contentTop` (it is centred on the border, so its margin box ends at
        // `border/2 + legendBox/2`, which is where the content begins whenever the legend is the
        // taller). So the measured bottom shifts with the children, floored at an empty content box.
        if (Height == CssConstants.Auto || string.IsNullOrEmpty(Height))
        {
            ActualBottom = Math.Max(
                contentTop + ActualPaddingBottom + ActualBorderBottomWidth,
                ActualBottom + moveRest);
        }
    }

    private bool IsFieldset =>
        string.Equals(HtmlTag?.Name, "fieldset", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this box is the rendered legend of the fieldset it is a child of.
    /// </summary>
    /// <remarks>
    /// HTML §15.5.13 gives the rendered legend a fit-content inline size when its own
    /// <c>inline-size</c> is <c>auto</c> — it is blockified, but it is not stretched to the
    /// fieldset's content width the way an ordinary block child would be. Layout asks this before
    /// resolving the used width; see <c>ResolveBlockUsedWidth</c>.
    /// </remarks>
    internal bool IsRenderedLegend =>
        string.Equals(HtmlTag?.Name, "legend", StringComparison.OrdinalIgnoreCase)
        && ParentBox is { } parent
        && parent.IsFieldset
        && ReferenceEquals(parent.RenderedLegend(), this);

    /// <summary>
    /// The fieldset's rendered legend: its first in-flow <c>&lt;legend&gt;</c> child. A second one
    /// is an ordinary block and stays in the content, which is what HTML §15.5.13 says by naming
    /// only the first.
    /// </summary>
    private CssBox RenderedLegend()
    {
        foreach (var child in Boxes)
        {
            if (child.Display == CssConstants.None
                || child.Position is CssConstants.Absolute or CssConstants.Fixed
                || child.Float != CssConstants.None)
            {
                continue;
            }

            // Block-level, because that is what a rendered legend is (HTML §15.5.13). An engine
            // whose user-agent sheet leaves `legend` at the CSS initial `inline` has no rendered
            // legend to place, and this stays out of its way rather than moving an inline box onto
            // a border.
            return string.Equals(child.HtmlTag?.Name, "legend", StringComparison.OrdinalIgnoreCase)
                && !IsInlineLevelDisplay(child.Display)
                ? child
                : null;
        }

        return null;
    }

    private static bool IsInlineLevelDisplay(string display)
    {
        var value = display?.Trim();
        return string.IsNullOrEmpty(value)
            || value.StartsWith(CssConstants.Inline, StringComparison.OrdinalIgnoreCase);
    }
}
