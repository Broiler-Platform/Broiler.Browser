using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal sealed class CssBoxImage : CssBox
{
    private readonly CssRectImage _imageWord;
    private ILayoutImageLoader _imageLoadHandler;

    /// <summary>
    /// This box's replaced-content completion, captured by the image prefetch (multithreading item #8)
    /// on the worker that decoded it and waiting for the layout thread to reach the point that would
    /// have run it. Null whenever the prefetch did not claim this box, which is always when it is off.
    /// </summary>
    private DeferredImageLoad? _deferredContentLoad;

    public CssBoxImage(CssBox parent, HtmlTag tag, Uri baseUrl) : base(parent, tag, baseUrl)
    {
        _imageWord = new CssRectImage(this);
        Words.Add(_imageWord);

    }

    public object Image => _imageWord.Image;

    internal override void MeasureWordsSize(ILayoutEnvironment g)
    {
        if (!_wordsSizeMeasured)
        {
            // Load the CSS background image (if any) via the base class.
            // CssBoxImage overrides MeasureWordsSize entirely, so the base
            // class's background-image loading code would otherwise be skipped.
            LoadBackgroundImageIfNeeded();
            LoadContentImageIfNeeded();

            MeasureWordSpacing(g);
            _wordsSizeMeasured = true;
        }

        CssLayoutEngine.MeasureImageSize(g, _imageWord);
    }

    /// <summary>
    /// Creates this box's image loader and starts the load, once. Extracted from
    /// <see cref="MeasureWordsSize"/> so the image prefetch (multithreading item #8) starts the load
    /// by calling the same code rather than a copy of it — the <c>_imageLoadHandler != null</c> guard
    /// is then what makes the later inline call a no-op, and the two cannot drift apart.
    /// </summary>
    /// <param name="deferCompletion">
    /// <c>true</c> when called from the prefetch worker: the completion is captured rather than applied,
    /// and the inline call applies it. It is the completion, not the decode, that a box's layout can
    /// observe the timing of — see <see cref="DeferredImageLoad"/>.
    /// </param>
    private void LoadContentImageIfNeeded(bool deferCompletion = false)
    {
        // A prefetched load's completion is applied here, which is where the serial path applied it.
        if (_deferredContentLoad is { } deferredLoad)
        {
            _deferredContentLoad = null;
            deferredLoad.Release();
            return;
        }

        if (_imageLoadHandler != null
            || !(LayoutEnvironment.AvoidAsyncImagesLoading || LayoutEnvironment.AvoidImagesLateLoading))
        {
            return;
        }

        var deferred = deferCompletion ? new DeferredImageLoad(OnLoadImageComplete) : null;
        _deferredContentLoad = deferred;
        _imageLoadHandler = LayoutEnvironment.CreateImageLoader(
            deferred is null ? OnLoadImageComplete : deferred.OnCompleted);

        if (Content != null && Content != CssConstants.Normal)
        {
            // Declined by the host (see OfflineSubresources): not starting the load leaves this box
            // in exactly the state a load the host marked handled leaves it in — the handler is
            // built, nothing completes, and the image is the missing one it was always going to be.
            if (!OfflineSubresources.Denies(Content))
                LoadImageWithAnimationPolicy(
                    () => _imageLoadHandler.LoadImage(Content, HtmlTag?.Attributes, BaseUrl));
            return;
        }

        // HTML §4.8.4.3 "select an image source": a responsive <img> — one carrying a `srcset`, or
        // one inside a <picture> — names its source through the candidate list, not through `src`.
        // Reading `src` alone left such an element with nothing to load at all, so it painted the
        // missing-image border instead of the image the page asked for.
        var selection = ResponsiveImageSourceSet.Select(this);

        // A `0x` descriptor parses (the spec rejects only a negative one) and names a density no
        // image can be laid out at — dividing by it is an infinite natural size, not a very large
        // one. It is stored as 1 so every consumer can divide by this unguarded; an *infinite*
        // density is meaningful and kept, because it is a zero-sized image.
        _imageWord.PixelDensity = selection is { Density: > 0 } chosen ? chosen.Density : 1.0;

        var src = selection?.Url ?? GetAttribute("src");
        // <object data="..."> fallback: use 'data' attribute when 'src' is absent
        if (string.IsNullOrEmpty(src))
            src = GetAttribute("data");
        if (!OfflineSubresources.Denies(src))
            LoadImageWithAnimationPolicy(
                () => _imageLoadHandler.LoadImage(src, HtmlTag?.Attributes, BaseUrl));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The replaced content is a load of its own, in addition to any background image, and it is the
    /// one that decides a replaced box's intrinsic size — so an <c>&lt;img&gt;</c> with no background
    /// is still worth claiming.
    /// </remarks>
    internal override bool HasPendingImageLoad =>
        base.HasPendingImageLoad || _imageLoadHandler == null;

    /// <inheritdoc/>
    internal override void LoadImagesForPrefetch(ILayoutEnvironment g)
    {
        base.LoadImagesForPrefetch(g);
        LoadContentImageIfNeeded(deferCompletion: true);
    }

    public override void Dispose()
    {
        _imageLoadHandler?.Dispose();
        base.Dispose();
    }

    /// <remarks>
    /// A load that ended without an image used to give the box a 2px inset border in
    /// <c>#A0A0A0</c>/<c>#E3E3E3</c> — the "broken image" frame a Netscape-era UA drew, inherited
    /// here from HtmlRenderer. No UA draws one now: HTML's rendering rules give <c>&lt;img&gt;</c>
    /// no default border at all, and a browser that cannot decode the image shows its <c>alt</c>
    /// text, or nothing.
    ///
    /// <para>
    /// Removing it is a <b>sizing</b> fix as much as a painting one, which is why it is worth doing
    /// rather than leaving as cosmetic legacy. The frame is 2px on all four sides, so it inflated
    /// the border box by 4px in <em>both axes</em> — and the inflated box is what
    /// <c>getBoundingClientRect</c>, <c>clientWidth</c>/<c>clientHeight</c> and
    /// <c>offsetWidth</c>/<c>offsetHeight</c> all reported: an <c>&lt;img style="width: 50px;
    /// height: 30px"&gt;</c> came back 54×34 with no border and no padding declared anywhere. It
    /// reached far more than genuinely broken images, because the geometry a script reads is
    /// captured before the images finish loading, so an image that loads perfectly well still
    /// reported the broken-image box to script. WPT
    /// <c>css-sizing/contain-intrinsic-size/contain-intrinsic-size-logical-003</c> measures exactly
    /// that, and its sixteen <c>&lt;img&gt;</c> assertions all read 4 where they want 0 and 104
    /// where they want 100.
    /// </para>
    /// </remarks>
    private void OnLoadImageComplete(object? image, RectangleF rectangle, bool async)
    {
        _imageWord.Image = image;
        _imageWord.ImageRectangle = rectangle;
        _wordsSizeMeasured = false;

        if (!LayoutEnvironment.AvoidImagesLateLoading || async)
        {
            var width = new CssLength(Width);
            var height = new CssLength(Height);
            var layout = width.Number <= 0 || width.Unit != CssUnit.Px || height.Number <= 0 || height.Unit != CssUnit.Px;

            LayoutEnvironment.RequestRefresh(layout);
        }
    }
}