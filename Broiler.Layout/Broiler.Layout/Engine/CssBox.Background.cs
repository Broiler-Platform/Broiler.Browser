using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    /// <summary>
    /// Loads the CSS background image if one is specified and not yet loaded.
    /// Called from <see cref="MeasureWordsSize"/> and overridden versions in
    /// subclasses (e.g. <see cref="CssBoxImage"/>) that replace the base
    /// measurement logic.
    /// </summary>
    protected void LoadBackgroundImageIfNeeded() => LoadBackgroundImages(deferCompletions: false);

    /// <summary>
    /// Loads the background image layers, once.
    /// </summary>
    /// <param name="deferCompletions">
    /// <c>true</c> when called from the image prefetch (multithreading item #8), which runs on a worker
    /// before the layout pass: each layer's completion is then captured rather than applied, and the
    /// inline call below applies it from the site that would have produced it. See
    /// <see cref="DeferredImageLoad"/> for why only the decode is safe to move.
    /// </param>
    private void LoadBackgroundImages(bool deferCompletions)
    {
        // A prefetched layer's completion is applied here — the point the serial path completed at —
        // rather than on the worker that decoded it. This runs before the initialized check because
        // the prefetch has already set that flag.
        if (ReleaseDeferredBackgroundLoads())
            return;

        if (BackgroundImage == CssConstants.None || _backgroundImagesInitialized)
            return;

        _backgroundImagesInitialized = true;
        var layers = SplitBackgroundImageLayers(BackgroundImage);

        if (layers.Count == 0)
            return;

        _backgroundImageLoadHandlers = new List<ILayoutImageLoader?>(layers.Count);

        foreach (var layer in layers)
        {
            var src = TryExtractBackgroundImageUrl(layer);

            if (string.IsNullOrEmpty(src))
            {
                _backgroundImageLoadHandlers.Add(null);
                continue;
            }

            var deferred = deferCompletions ? new DeferredImageLoad(OnImageLoadComplete) : null;
            if (deferred != null)
                (_deferredBackgroundLoads ??= []).Add(deferred);

            var imageLoadHandler = LayoutEnvironment.CreateImageLoader(
                deferred is null ? OnImageLoadComplete : deferred.OnCompleted);

            _backgroundImageLoadHandlers.Add(imageLoadHandler);

            // Declined by the host (see OfflineSubresources): the layer paints nothing, which is
            // where an unreachable background image ends up anyway — after the wait, not instead of
            // it. The handler is still recorded so the layer indices stay aligned.
            if (!OfflineSubresources.Denies(src))
                LoadImageWithAnimationPolicy(
                    () => imageLoadHandler.LoadImage(src, HtmlTag?.Attributes, BaseUrl));
        }
    }

    /// <summary>
    /// Applies the completions the prefetch captured for this box's background layers, in the order it
    /// created them, and returns whether there were any.
    /// </summary>
    private bool ReleaseDeferredBackgroundLoads()
    {
        var deferred = _deferredBackgroundLoads;
        if (deferred is null)
            return false;

        _deferredBackgroundLoads = null;
        foreach (var load in deferred)
            load.Release();

        return true;
    }

    private static List<string> SplitBackgroundImageLayers(string backgroundImage)
    {
        var layers = new List<string>();
        
        if (string.IsNullOrWhiteSpace(backgroundImage))
            return layers;

        int depth = 0;
        int start = 0;
        
        for (int i = 0; i < backgroundImage.Length; i++)
        {
            switch (backgroundImage[i])
            {
                case '(':
                    depth++;
                    break;
                
                case ')':
                    if (depth > 0)
                        depth--;
                    break;
                
                case ',' when depth == 0:
                    layers.Add(backgroundImage[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        layers.Add(backgroundImage[start..].Trim());
        return layers;
    }

    private static string? TryExtractBackgroundImageUrl(string layer)
    {
        if (string.IsNullOrWhiteSpace(layer))
            return null;

        layer = layer.Trim();
        
        if (!layer.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return layer.Contains('(') ? null : layer;

        if (!layer.EndsWith(')'))
            return null;

        var src = layer[4..^1].Trim();
        if (src.Length >= 2 &&
            ((src[0] == '\'' && src[^1] == '\'') ||
             (src[0] == '"' && src[^1] == '"')))
        {
            src = src[1..^1];
        }

        return src;
    }

    private void OnImageLoadComplete(object? image, RectangleF rectangle, bool async)
    {
        if (image != null && async)
            LayoutEnvironment.RequestRefresh(false);
    }
}
