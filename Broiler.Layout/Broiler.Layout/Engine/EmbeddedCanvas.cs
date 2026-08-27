namespace Broiler.Layout.Engine;

/// <summary>
/// CSS Color Adjust 1 §2.4: whether the canvas of a <em>nested browsing context</em> — the document
/// inside an <c>&lt;iframe&gt;</c>, <c>&lt;frame&gt;</c> or <c>&lt;object type="text/html"&gt;</c> —
/// is painted opaquely, or left transparent so the embedding page shows through it.
/// </summary>
/// <remarks>
/// <para>
/// A nested browsing context's canvas is <b>transparent by default</b>: a frame whose document
/// propagates no background lets the embedder's pixels through, which is why
/// <c>&lt;iframe src="dark-frame-ref.html"&gt;</c> over a white page is white. The one exception is
/// the colour-scheme mismatch: <em>"if the used color scheme of the element and the used color
/// scheme of the embedded document's root element differ, the canvas is painted with the embedded
/// root's default background"</em>. Without it a dark-scheme document embedded in a light page
/// would show light through its own dark text.
/// </para>
/// <para>
/// The comparison is between the <b>embedding element</b> and the <b>embedded root</b> — not
/// between the two documents. That distinction is the whole content of WPT
/// <c>css/css-color-adjust/rendering/dark-color-scheme/color-scheme-iframe-background</c> (a dark
/// frame in a dark-scheme <c>&lt;iframe&gt;</c> on a light page stays transparent) and of
/// <c>…-mismatch-opaque-cross-origin-003.sub</c> (a light frame in a light-scheme
/// <c>&lt;iframe&gt;</c> on a dark page stays transparent). Both rendered an opaque box before this
/// existed, because the embedding element's <c>color-scheme</c> was never consulted.
/// </para>
/// <para>
/// Thread-static and scope-restoring, like the engine's other render levers
/// (<see cref="CanvasBackdrop.Current"/>, <see cref="DocumentRoot.Current"/>): a frame is
/// rasterised synchronously on the thread compositing it, and the value has to be the one belonging
/// to <em>this</em> frame while its own canvas is painted. Nothing pinned means "not embedded", so
/// a top-level document is unaffected — <see cref="PaintsOpaqueBackdrop"/> answers
/// <see langword="true"/> and every render that sets nothing is byte-identical to before.
/// </para>
/// </remarks>
internal static class EmbeddedCanvas
{
    /// <summary>
    /// The computed <c>color-scheme</c> of the element embedding the document being rendered on
    /// this thread, or <see langword="null"/> when the document is not embedded at all.
    /// </summary>
    [System.ThreadStatic]
    public static string? EmbedderColorScheme;

    /// <summary>
    /// Whether the document being rendered on this thread is inside a nested browsing context.
    /// Distinct from <see cref="EmbedderColorScheme"/> being null, because an embedding element
    /// with no <c>color-scheme</c> at all is still an embedding element.
    /// </summary>
    [System.ThreadStatic]
    public static bool IsEmbedded;

    /// <summary>
    /// Marks the render inside the returned scope as embedded in an element whose computed
    /// <c>color-scheme</c> is <paramref name="embedderColorScheme"/>, restoring the previous values
    /// when it is disposed — so sibling frames and nested frames each see their own embedder.
    /// </summary>
    public static System.IDisposable Pin(string? embedderColorScheme)
    {
        // A nested browsing context is also its own *formatting* context, so an enclosing paged
        // one does not reach into it: the frame's `width`/`height` media features describe the
        // frame, not the page the embedder is printed on. Suspending it here rather than at the
        // two call sites means the rule holds for every embedded render, including the one inside
        // Broiler.HTML that this file cannot see.
#if BROILER_CSS_PAGED_MEDIA
        var scope = new Scope(
            EmbedderColorScheme, IsEmbedded, Broiler.CSS.Dom.CssPagedMedia.Suspend());
#else
        // The paged formatting context is a pending Broiler.CSS patch (see patches/). Until it
        // lands there is no context to suspend, and this reads exactly as it did before.
        var scope = new Scope(EmbedderColorScheme, IsEmbedded, NoScope.Instance);
#endif
        EmbedderColorScheme = embedderColorScheme;
        IsEmbedded = true;
        return scope;
    }

    /// <summary>
    /// CSS Color Adjust §2.2–2.3: whether a <c>color-scheme</c> value resolves to a dark used
    /// scheme against the reference environment's light preference — the list offers <c>dark</c>
    /// but not <c>light</c>. The <c>only</c> keyword does not change this (it forbids a UA override
    /// that is not performed here).
    /// </summary>
    public static bool UsesDarkColorScheme(string? colorScheme)
    {
        if (string.IsNullOrWhiteSpace(colorScheme))
            return false;

        bool hasDark = false, hasLight = false;
        foreach (var token in colorScheme.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("dark", System.StringComparison.OrdinalIgnoreCase))
                hasDark = true;
            else if (token.Equals("light", System.StringComparison.OrdinalIgnoreCase))
                hasLight = true;
        }

        return hasDark && !hasLight;
    }

    /// <summary>
    /// Whether the UA paints an opaque backdrop under a document whose root element's computed
    /// <c>color-scheme</c> is <paramref name="embeddedRootColorScheme"/>: always for a document
    /// that is not embedded, and for an embedded one only when its used scheme differs from its
    /// embedding element's. <see langword="false"/> means the canvas is transparent and the
    /// embedder shows through.
    /// </summary>
    public static bool PaintsOpaqueBackdrop(string? embeddedRootColorScheme) =>
        !IsEmbedded
        || UsesDarkColorScheme(EmbedderColorScheme) != UsesDarkColorScheme(embeddedRootColorScheme);

#if !BROILER_CSS_PAGED_MEDIA
    /// <summary>A disposable that does nothing, for the build without the pending patch.</summary>
    private sealed class NoScope : System.IDisposable
    {
        internal static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
#endif

    private sealed class Scope(
        string? previousScheme, bool previousIsEmbedded, System.IDisposable continuousMedia)
        : System.IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            EmbedderColorScheme = previousScheme;
            IsEmbedded = previousIsEmbedded;
            continuousMedia.Dispose();
        }
    }
}
