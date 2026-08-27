using System.Globalization;
using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// Native scroll-offset placement (HtmlBridge complexity-reduction roadmap Phase 5,
/// P5.8d.2b scroll expansion). A root post-pass — run after the main single-pass
/// layout, before <see cref="CssBox.RunNativeAnchorPlacement"/> — that shifts the
/// content of each scroll container the bridge marked with
/// <see cref="ScrollTopAttr"/>/<see cref="ScrollLeftAttr"/> by the negative scroll
/// offset, so a script-set <c>scrollTop</c>/<c>scrollLeft</c> renders natively instead
/// of via the bridge's DOM-shift wrapper. The container's own <c>overflow</c> clip
/// (applied at paint) clips the shifted content.
/// </summary>
/// <remarks>
/// Gated by <see cref="NativeAnchorPlacement.Enabled"/> (default off), so this is inert
/// until the cutover. The bridge marks a container only when the document has no anchor
/// content (see <c>DomBridge.ApplyScrollSimulationTree</c>), so this pass never
/// interacts with the anchor-scroll-container / position-visibility machinery, which
/// keeps the bridge's DOM-shift.
/// </remarks>
partial class CssBox
{
    internal const string ScrollTopAttr = "data-broiler-scroll-top";
    internal const string ScrollLeftAttr = "data-broiler-scroll-left";

    /// <summary>
    /// Entry point for the scroll post-pass, invoked from <c>PerformLayout</c> at the
    /// document root when the flag is on.
    /// </summary>
    internal static void RunScrollSimulation(CssBox root) => ApplyScrollSimulation(root);

    private static void ApplyScrollSimulation(CssBox box)
    {
        double top = ParseScrollOffset(box.GetAttribute(ScrollTopAttr));
        double left = ParseScrollOffset(box.GetAttribute(ScrollLeftAttr));

        if (top != 0 || left != 0)
        {
            // Shift the container's in-flow content up/left by the scroll offset. OffsetTop/
            // OffsetLeft translate a box and its whole subtree and already skip position:fixed
            // descendants (CSS2.1 §9.6.1), matching the bridge's fixed handling; the container
            // box itself stays put, so its overflow box clips the shifted content.
            foreach (var child in box.Boxes)
            {
                if (child.Position == CssConstants.Fixed)
                    continue;
                if (top != 0)
                    child.OffsetTop(-top);
                if (left != 0)
                    child.OffsetLeft(-left);
            }
        }

        // Recurse so nested scroll containers compose (each shifts its own content, on top
        // of any shift an ancestor already applied to it).
        foreach (var child in box.Boxes)
            ApplyScrollSimulation(child);
    }

    private static double ParseScrollOffset(string? value) =>
        !string.IsNullOrEmpty(value)
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;
}
