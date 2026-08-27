namespace Broiler.Layout.Engine;

/// <summary>
/// Feature gate for vertical writing-mode flow (logical layout + post-layout
/// coordinate transform).
///
/// Enabled by default; set the environment variable <c>BROILER_VERTICAL_FLOW</c>
/// to <c>0</c> to fall back to the legacy dimension-swap-only path (which does
/// not rotate content flow). When enabled, it lays vertical-lr / vertical-rl
/// subtrees out in a logical horizontal frame and rotates box/line/word
/// positions into physical space (Stage 1), rotates glyphs 90° (Stage 2), does
/// per-glyph vertical advance (Stage 3), and keeps multi-column content as a
/// single logical block run so the rotation places it along the block axis
/// (Stage 4b). All gating is behind <see cref="CssBoxProperties.IsVerticalWritingMode"/>
/// checks, so horizontal-tb rendering is unaffected either way.
///
/// Known limitations: CJK-vs-Latin script detection (mixed orientation assumes
/// Latin); baseline / vertical-align in a vertical context; genuine multi-column
/// fragmentation in vertical mode (content flows as a single block run).
/// </summary>
internal static class VerticalFlowPrototype
{
    private static readonly bool _enabled =
        Environment.GetEnvironmentVariable("BROILER_VERTICAL_FLOW") != "0";

    public static bool Enabled => _enabled;
}
