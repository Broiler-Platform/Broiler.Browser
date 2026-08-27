namespace Broiler.Layout;

/// <summary>
/// Intrinsic dimensions of a replaced element's image, supplied by the host so
/// layout can size <c>&lt;img&gt;</c> and other replaced boxes without touching a
/// graphics backend. Mirrors the <c>RImage.Width</c>/<c>Height</c>/
/// <c>HasIntrinsicRatio</c> surface layout reads today.
/// </summary>
/// <param name="Width">Intrinsic width in CSS pixels, or 0 if unknown.</param>
/// <param name="Height">Intrinsic height in CSS pixels, or 0 if unknown.</param>
/// <param name="HasIntrinsicRatio">
/// Whether the image carries an intrinsic aspect ratio usable for sizing.
/// </param>
/// <param name="AspectRatio">
/// The intrinsic aspect ratio (width ÷ height) when <paramref name="HasIntrinsicRatio"/> says there
/// is one, or 0. It is <em>not</em> always <paramref name="Width"/> ÷ <paramref name="Height"/>: an
/// SVG with a <c>viewBox</c> and no <c>width</c>/<c>height</c> has a ratio but no intrinsic size, and
/// the size reported for it is the 300×150 default object size, which carries a different ratio.
/// </param>
/// <param name="HasIntrinsicSize">
/// Whether <paramref name="Width"/> and <paramref name="Height"/> are the content's own dimensions
/// rather than the default object size standing in for them. Replaced <em>sizing</em> uses the
/// default either way (CSS Images 3 §5.1, matching Chromium), so it does not need to tell them
/// apart; <c>object-fit</c> does — <c>none</c> means "the intrinsic size" and there is not one.
/// </param>
public readonly record struct ImageIntrinsics(
    double Width,
    double Height,
    bool HasIntrinsicRatio,
    double AspectRatio = 0,
    bool HasIntrinsicSize = true);
