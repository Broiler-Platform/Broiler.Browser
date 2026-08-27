using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// CSS2.1 §10.6.4 block-axis auto-margin centring for out-of-flow boxes, run as a root post-pass — after
/// the single-pass layout, when every box's used height is final. The inline axis and definite (explicit
/// length/percentage) heights are centred in-line during <see cref="CssBox.ComputeStaticAndFloatPosition"/>
/// (those sizes are known before positioning), but a <b>content / intrinsic-keyword</b> block size is not
/// resolved until layout completes — and for a fixed/absolute box its used height is not folded into the
/// box until after its own layout pass returns — so it cannot be centred earlier. This pass re-positions
/// each such box by its block-axis auto-margin offset once the final height is known. Powers native modal
/// <c>&lt;dialog&gt;</c> vertical centring for a content-height dialog (<c>inset:0; margin:auto</c>).
/// </summary>
partial class CssBox
{
    /// <summary>
    /// Entry point for the block-axis auto-margin centring post-pass, invoked from
    /// <c>PerformLayout</c> at the document root when native placement is enabled. Top-down, so a box is
    /// centred against its (already-placed) containing block before its own descendants are processed.
    /// </summary>
    internal static void CenterOutOfFlowBlockAxis(CssBox root)
    {
        foreach (var child in root.Boxes)
        {
            child.CenterOwnBlockAxisAutoMargins();
            CenterOutOfFlowBlockAxis(child);
        }
    }

    private void CenterOwnBlockAxisAutoMargins()
    {
        if (Position != CssConstants.Absolute && Position != CssConstants.Fixed)
            return;

        // A definite (explicit length/percentage) height was already centred pre-sizing; only a content /
        // intrinsic-keyword block size needs this post-pass. A `height:auto` box with both insets fills
        // its containing block (§10.6.4 — no leftover space), so the offset below resolves to 0.
        if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height) && !IsIntrinsicSizingHeightKeyword(Height))
            return;

        bool hasTop = Top != null && Top != CssConstants.Auto;
        bool hasBottom = Bottom != null && Bottom != CssConstants.Auto;
        if (!hasTop || !hasBottom)
            return;
        if (!(IsSpecifiedMarginTopAuto && IsSpecifiedMarginBottomAuto))
            return;

        double boxHeight = Bounds.Height; // final used border-box height
        if (boxHeight <= 0)
            return;

        double cbPadTop, cbPadHeight;
        if (Position == CssConstants.Fixed && LayoutEnvironment != null)
        {
            var vp = FixedPositioningViewport();
            cbPadTop = vp.Y;
            cbPadHeight = vp.Height;
        }
        else
        {
            var cb = FindPositionedContainingBlock();
            GetAbsoluteContainingBlockPaddingBox(cb, out _, out cbPadTop, out _, out cbPadHeight);
        }

        double cssTop = CssLengthParser.ParseLength(Top, cbPadHeight, GetEmHeight());
        double cssBottom = CssLengthParser.ParseLength(Bottom, cbPadHeight, GetEmHeight());
        double remaining = cbPadHeight - cssTop - cssBottom - boxHeight;
        double marginTop = remaining >= 0 ? remaining / 2 : 0;

        double newY = cbPadTop + cssTop + marginTop;
        float deltaY = (float)newY - Location.Y;
        if (deltaY != 0)
            OffsetTop(deltaY);
    }
}
