using Broiler.Layout.Engine;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the CSS Box Alignment Level 3 §"Aligning Abspos" overflow behaviour for
/// absolutely-positioned boxes — <c>CssBox.ResolveAbsposSelfAlignment</c>.
///
/// <para>
/// WHY THIS EXISTS (WPT issue #1100, css-align/abspos cluster): the abspos
/// self-alignment path used to clamp the box to its inset-modified containing
/// block (IMCB) alone, so it only placed the box correctly when it fit inside
/// the IMCB. The spec aligns the box <i>unsafely</i> in the IMCB and then clamps
/// the final position to the range that ENCLOSES BOTH the IMCB and the actual
/// containing block (CB), start edge winning on conflict. The expected offsets
/// below are the eight <c>data-offset-x</c> values from the WPT reftest
/// <c>css/css-align/abspos/justify-self-default-overflow-htb-ltr-htb.html</c>
/// (container CB = [0,100]; <c>.item</c> has no margin/border so it shrink-wraps
/// its <c>.inner</c>).
/// </para>
///
/// <para>
/// DIAGNOSTIC: if these assertions fail, abspos <c>align-self</c>/<c>justify-self</c>
/// placement has drifted — re-check <c>ResolveAbsposSelfAlignment</c> and the four
/// IMCB call sites in <c>CssBox.cs</c> before chasing renderer/pixel issues.
/// </para>
/// </summary>
public sealed class AbsposSelfAlignmentTests
{
    // Mirrors the inline-axis (justify-self, horizontal-tb) call site in
    // CssBox.cs: final x = imcbLeft + offset (+ leading margin, which is 0 in
    // the WPT test). The containing-block padding box is fixed at [0, 100].
    private static double JustifySelfX(
        string value, double left, double right, double boxWidth,
        bool itemRtl, bool cbRtl)
    {
        const double cbPadLeft = 0, cbPadWidth = 100;
        double imcbLeft = cbPadLeft + left;
        double imcbWidth = cbPadWidth - left - right;
        bool startIsLow = !cbRtl;

        double offset = CssBox.ResolveAbsposSelfAlignment(
            value, imcbLeft, imcbWidth, cbPadLeft, cbPadWidth,
            boxWidth, itemRtl, startIsLow);
        return imcbLeft + offset;
    }

    // left=20 right=10 → IMCB [20,90]; left=-20 right=-10 → IMCB [-20,110].
    [Theory]
    // No overflow / overflow-IMCB-but-not-CB → unsafe-centered in the IMCB.
    [InlineData(20, 10, 20, 45)]
    [InlineData(20, 10, 80, 15)]
    // Overflows the weak/strong CB edge → shifted to stay within the IMCB∪CB union.
    [InlineData(20, 10, 95, 5)]
    [InlineData(20, 10, 120, 0)]
    // Negative insets: IMCB extends past the CB, so the box may overflow the CB.
    [InlineData(-20, -10, 20, 35)]
    [InlineData(-20, -10, 120, -15)]
    [InlineData(-20, -10, 160, -20)]
    public void JustifySelfCenter_DefaultOverflow_ClampsToImcbCbUnion(
        double left, double right, double boxWidth, double expectedX)
    {
        // LTR item in an LTR container (the first half of the WPT test).
        Assert.Equal(expectedX, JustifySelfX("center", left, right, boxWidth, itemRtl: false, cbRtl: false), 3);

        // The RTL items in the same test expect the SAME offsets: the overflow
        // clamp follows the CONTAINING BLOCK's direction (ltr), not the item's,
        // and 'center' is direction-symmetric.
        Assert.Equal(expectedX, JustifySelfX("center", left, right, boxWidth, itemRtl: true, cbRtl: false), 3);
    }

    [Fact(Timeout = 600000)]
    public void ExplicitUnsafe_AlignsWithoutClamping()
    {
        // unsafe center of a 120px box in IMCB [20,90]: (70-120)/2 = -25 → x = -5,
        // overflowing both CB edges with no clamp.
        Assert.Equal(-5, JustifySelfX("unsafe center", 20, 10, 120, itemRtl: false, cbRtl: false), 3);
    }

    [Fact(Timeout = 600000)]
    public void ExplicitSafe_FallsBackToImcbStartOnOverflow()
    {
        // safe center of a 120px box in IMCB [20,90]: overflow → start of the
        // IMCB (x = imcbLeft = 20), never overflowing the strong edge.
        Assert.Equal(20, JustifySelfX("safe center", 20, 10, 120, itemRtl: false, cbRtl: false), 3);
    }
}
