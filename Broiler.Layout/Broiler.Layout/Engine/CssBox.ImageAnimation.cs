using System;
using Broiler.Media.Image;

namespace Broiler.Layout.Engine;

// CSS Image Animation 1: which frame of an animated image this box's loads decode.
internal partial class CssBox
{
    /// <summary>
    /// The point on an animated image's own timeline that this box's images should show, or
    /// <see langword="null"/> to follow the document's (<see cref="ImageAnimationClock"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>paused</c> holds the frame the image had reached and <c>stopped</c> resets to the first
    /// one. A still render has no history for the two to differ over — the animation never ran, so
    /// "the frame it had reached" *is* the first frame — and both resolve to zero here. The
    /// distinction only becomes observable once an image has been running and is paused mid-flight
    /// (WPT <c>image-animation-paused-exact-frame</c>), which needs a per-element timeline this
    /// renderer does not keep; that case is deliberately not modelled rather than approximated.
    /// </para>
    /// <para>
    /// <c>running</c> asks for a timeline of the element's own, unsynchronised with other elements.
    /// With no such timeline there is nothing to offset against, so it reads as the document clock
    /// — the same answer <c>normal</c> gives, which is what a single-shot render can honestly say.
    /// </para>
    /// </remarks>
    internal TimeSpan? ImagePresentationTime =>
        GoverningImageAnimation is { } governing
        && (governing.Equals("paused", StringComparison.OrdinalIgnoreCase)
            || governing.Equals("stopped", StringComparison.OrdinalIgnoreCase))
            ? TimeSpan.Zero
            : null;

    /// <summary>
    /// The <c>image-animation</c> that decides this box's images — its own, except for a
    /// <c>&lt;body&gt;</c> background that propagates to the canvas (CSS2.1 §14.2), where the
    /// <em>root element's</em> value governs and body's is ignored.
    /// </summary>
    /// <remarks>
    /// The rule is asserted from both sides by WPT and the pair is easy to get backwards:
    /// <c>image-animation-body-background-root-propagation-paused</c> pauses a body background
    /// because <c>html</c> asks for it, and
    /// <c>image-animation-body-background-no-propagation-paused</c> keeps the same background
    /// <em>animating</em> when it is <c>body</c> that asks — the propagation carries the
    /// background up to the canvas but not the property that governs it.
    /// <para>
    /// Propagation is tested the way the paint walker tests it, on the root's own background
    /// being absent. Deliberately not modelled here: containment on <c>html</c>/<c>body</c> also
    /// disables propagation (CSS Contain 2 §2). Getting that wrong only misreads which of two
    /// <c>image-animation</c> values applies to a contained body's background, and both are
    /// <c>normal</c> unless a document sets one — whereas reaching into containment from the
    /// image-load path would duplicate a rule that already lives in the paint walker.
    /// </para>
    /// </remarks>
    private string? GoverningImageAnimation
    {
        get
        {
            if (!IsBodyElement)
                return ImageAnimation;

            var root = RootElementBox;
            if (root is null || !RootHasNoBackgroundOfItsOwn(root))
                return ImageAnimation;

            return root.ImageAnimation;
        }
    }

    private bool IsBodyElement =>
        HtmlTag is not null && HtmlTag.Name.Equals("body", StringComparison.OrdinalIgnoreCase);

    /// <summary>The <c>&lt;html&gt;</c> box, found by walking up from this one.</summary>
    private CssBox? RootElementBox
    {
        get
        {
            for (var box = ParentBox; box != null; box = box.ParentBox)
            {
                if (box.HtmlTag is { } tag && tag.Name.Equals("html", StringComparison.OrdinalIgnoreCase))
                    return box;
            }
            return null;
        }
    }

    /// <summary>
    /// Whether the root element has neither a background colour nor a background image, which is
    /// the condition under which body's background is the one that reaches the canvas
    /// (CSS Backgrounds 3 §2.11.2).
    /// </summary>
    private static bool RootHasNoBackgroundOfItsOwn(CssBox root) =>
        (string.IsNullOrEmpty(root.BackgroundColor)
            || root.BackgroundColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrEmpty(root.BackgroundImage)
            || root.BackgroundImage.Equals("none", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Runs <paramref name="load"/> with the frame choice this box asks for in force, so the decode
    /// the load reaches — on this thread or on the pool thread an async host defers it to — selects
    /// against it instead of the document clock. A no-op when the box follows the document, which
    /// is every box unless <c>image-animation</c> says otherwise.
    /// </summary>
    protected void LoadImageWithAnimationPolicy(Action load)
    {
        if (ImagePresentationTime is not { } presentationTime)
        {
            load();
            return;
        }

        using var pinned = ImageAnimationClock.PinForImage(presentationTime);
        load();
    }
}
