using System.Drawing;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.HTML.Graphics;
using Broiler.HTML.Image;
using Broiler.HtmlBridge.Logging;
using Broiler.Layout.IR;
using Broiler.Net.Http;

namespace Broiler.Browser;

/// <summary>
/// Makes the container of one document: its markup, its base URL, the network it loads through, the
/// request context it loads as, and the viewport its media queries resolve against. The window makes
/// a frame's the way it makes a page's.
/// </summary>
internal delegate HtmlContainer ContentContainerFactory(
    string html,
    string baseUrl,
    IBrowserRequestTransport? network,
    DocumentRequestContext? document,
    SizeF viewport);

/// <summary>
/// Paints the documents of a page's nested browsing contexts — <c>&lt;iframe&gt;</c>,
/// <c>&lt;frame&gt;</c> and <c>&lt;object type="text/html"&gt;</c> — in the window, each from a
/// container of its own laid out at its element's content box.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the window needs it.</b> The page's display list holds none of them. Broiler.Layout loads
/// a frame's document onto its element's fragment (<see cref="Fragment.EmbeddedDocumentHtml"/>) and
/// leaves it to whoever rasterises the page: Broiler.HTML's <c>HtmlRender</c> and Broiler.Cli's
/// <c>RenderProbe</c> render it to a bitmap and blend that over the page. The window replays the
/// display list and did nothing with it, so every frame was an empty box — the whole reCAPTCHA
/// checkbox on https://www.google.com/recaptcha/api2/demo, which lives in the
/// <c>api2/anchor</c> iframe, and showed in the analysis's screenshot but not in the browser.
/// </para>
/// <para>
/// <b>Where a frame paints.</b> Over the page, after all of it, as the image renderers do: the
/// display list marks no place in the stacking order for it, so content the page positions over a
/// frame is painted under it here, and an ancestor's <c>overflow</c> clip does not reach it. Clipped
/// to its content box, and transparent where its document propagates no background (CSS Color
/// Adjust §2.4, for a frame whose colour scheme matches its element's — the display list paints no
/// light backdrop).
/// </para>
/// <para>
/// <b>What it loads as.</b> A frame's stylesheets, fonts and images go through the page's network
/// as requests of a child of the page's document (<see cref="DocumentRequestContext.CreateChild"/>),
/// at the URL the frame's document came from. A web page's frame never reads the local file system:
/// one whose document URL is a <c>file:</c> URL is not painted.
/// </para>
/// <para>
/// <b>When it does the work.</b> On the UI thread, when the page's render list is rebuilt. A frame is
/// parsed again only when its markup changes — when its own scripts, or the page's, moved its
/// document on — and keeps its loads across reparses, as the page's container does; it is laid out
/// again only when it was reparsed or its box changed size.
/// </para>
/// </remarks>
internal sealed class FrameCompositor : IDisposable
{
    /// <summary>How deep frames nest before the innermost stop painting: <c>HtmlRender</c>'s bound,
    /// which stops a document that (transitively) embeds itself.</summary>
    internal const int MaxDepth = 4;

    private readonly ContentContainerFactory _createContainer;
    private readonly int _depth;
    private List<Frame> _frames = [];

    /// <summary>The page layout the frames were last brought in line with.</summary>
    private Fragment? _tree;

    public FrameCompositor(ContentContainerFactory createContainer)
        : this(createContainer, depth: 0)
    {
    }

    private FrameCompositor(ContentContainerFactory createContainer, int depth)
    {
        _createContainer = createContainer ?? throw new ArgumentNullException(nameof(createContainer));
        _depth = depth;
    }

    /// <summary>The frames painted since the last <see cref="Update"/>, in document order.</summary>
    internal int Count => _frames.Count;

    /// <summary>Whether every frame, nested ones included, has its render list.</summary>
    private bool IsBuilt => _frames.TrueForAll(static frame => frame.IsBuilt);

    /// <summary>
    /// Brings the frames in line with <paramref name="page"/>'s latest layout and builds their render
    /// lists with <paramref name="renderer"/>.
    /// </summary>
    /// <remarks>
    /// Nothing is done when the page has not been laid out again since the last call and the render
    /// lists are still there: the page's render list is rebuilt for a scroll as well, and a scroll
    /// moves the frames without changing anything in them.
    /// </remarks>
    public void Update(IBroilerRenderer renderer, HtmlContainer page)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(page);

        var tree = _depth < MaxDepth ? page.LatestFragmentTree : null;
        if (ReferenceEquals(tree, _tree) && IsBuilt)
            return;

        _tree = tree;
        var found = new List<(Fragment Fragment, RectangleF Box)>();
        if (tree is not null)
            Collect(tree, found);

        // Matched by position among the page's frames and by the URL their documents came from: a
        // frame keeps its container — and what it has loaded — for as long as the same document is
        // shown in the same place.
        List<Frame?> previous = [.. _frames];
        var next = new List<Frame>(found.Count);
        for (var i = 0; i < found.Count; i++)
        {
            var (fragment, box) = found[i];
            Frame? frame = null;
            if (i < previous.Count && previous[i] is { } candidate && candidate.Shows(fragment.EmbeddedDocumentBaseUrl))
            {
                frame = candidate;
                previous[i] = null;
            }

            frame ??= new Frame(new FrameCompositor(_createContainer, _depth + 1));
            if (TryUpdate(frame, renderer, page, fragment, box))
                next.Add(frame);
            else
                frame.Dispose();
        }

        foreach (var stale in previous)
            stale?.Dispose();

        _frames = next;
    }

    /// <summary>
    /// Appends every frame to <paramref name="target"/>, in the page's layout coordinates shifted by
    /// <paramref name="scroll"/> — the offset the page's own display list was built with.
    /// </summary>
    public void Replay(BRenderList target, PointF scroll)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (var frame in _frames)
            frame.Replay(target, scroll);
    }

    /// <summary>
    /// Releases the frames' render lists, whose images belong to the renderer that built them; the
    /// next <see cref="Update"/> builds them again. The frames' containers are kept.
    /// </summary>
    public void ReleaseRenderLists()
    {
        foreach (var frame in _frames)
            frame.ReleaseRenderList();
    }

    /// <summary>Drops every frame and what it loaded.</summary>
    public void Clear()
    {
        foreach (var frame in _frames)
            frame.Dispose();

        _frames = [];
        _tree = null;
    }

    public void Dispose() => Clear();

    /// <summary>
    /// Updates one frame, or answers <see langword="false"/> when it is not to be painted. A document
    /// the renderer fails on costs only its own frame: this runs while the window paints, and an
    /// exception out of it would cost the page its paint.
    /// </summary>
    private bool TryUpdate(Frame frame, IBroilerRenderer renderer, HtmlContainer page, Fragment fragment, RectangleF box)
    {
        try
        {
            return frame.TryUpdate(renderer, page, fragment, box, _createContainer);
        }
        catch (Exception ex)
        {
            RenderLogger.LogWarning(LogCategory.HtmlRenderer, "Browser.frames",
                $"The frame of {fragment.EmbeddedDocumentBaseUrl} is not painted: rendering its document failed", ex);
            return false;
        }
    }

    /// <summary>Every fragment that carries a document, with its content box in layout coordinates.</summary>
    private static void Collect(Fragment fragment, List<(Fragment, RectangleF)> found)
    {
        if (!string.IsNullOrEmpty(fragment.EmbeddedDocumentHtml))
        {
            // The content box: the border box less border and padding, as the image renderers place
            // a frame (and PaintWalker an image).
            var border = fragment.Border;
            var padding = fragment.Padding;
            var x = fragment.Location.X + (float)(border.Left + padding.Left);
            var y = fragment.Location.Y + (float)(border.Top + padding.Top);
            var width = fragment.Size.Width - (float)(border.Left + border.Right + padding.Left + padding.Right);
            var height = fragment.Size.Height - (float)(border.Top + border.Bottom + padding.Top + padding.Bottom);
            if (width >= 1 && height >= 1 && float.IsFinite(x) && float.IsFinite(y))
                found.Add((fragment, new RectangleF(x, y, width, height)));
        }

        foreach (var child in fragment.Children)
            Collect(child, found);
    }

    private sealed class Frame(FrameCompositor nested) : IDisposable
    {
        private HtmlContainer? _container;
        private DocumentRequestContext? _page;
        private string? _source;
        private string? _baseUrl;
        private SizeF _laidOutAt;
        private RectangleF _box;
        private HtmlGraphicsRenderList? _renderList;

        public bool Shows(string? baseUrl) => _container is not null && string.Equals(_baseUrl, baseUrl, StringComparison.Ordinal);

        public bool IsBuilt => _renderList is not null && nested.IsBuilt;

        public bool TryUpdate(
            IBroilerRenderer renderer,
            HtmlContainer page,
            Fragment fragment,
            RectangleF box,
            ContentContainerFactory createContainer)
        {
            var source = fragment.EmbeddedDocumentHtml;
            var baseUrl = fragment.EmbeddedDocumentBaseUrl ?? page.BaseUrl ?? string.Empty;
            var reparsed = false;
            if (_container is null || !ReferenceEquals(_page, page.DocumentContext))
            {
                if (!TryGetDocument(page, baseUrl, out var document))
                    return false;

                _container?.Dispose();
                var network = document is null ? null : page.RequestTransport;
                _container = createContainer(PrepareForBrowsing(source), baseUrl, network, document, box.Size);
                _page = page.DocumentContext;
                _baseUrl = fragment.EmbeddedDocumentBaseUrl;
                reparsed = true;
            }
            else if (!string.Equals(source, _source, StringComparison.Ordinal))
            {
                // The same document, moved on: parsed again in the same container, which keeps what
                // it loaded, the way the page's own container is when its scripts change it.
                _container.MaxSize = box.Size;
                _container.SetHtmlWithStyleSet(PrepareForBrowsing(source), baseUrl: baseUrl);
                reparsed = true;
            }

            _source = source;
            var container = _container;
            if (reparsed || _laidOutAt != box.Size)
            {
                container.Location = PointF.Empty;
                container.MaxSize = box.Size;
                container.PerformLayout(new RectangleF(PointF.Empty, box.Size));
                _laidOutAt = box.Size;
            }

            ReleaseRenderList();
            _renderList = HtmlGraphicsRenderListBuilder.Build(
                renderer,
                container.CreateDisplayList(),
                new RectangleF(PointF.Empty, box.Size));
            _box = box;
            nested.Update(renderer, container);
            return true;
        }

        public void Replay(BRenderList target, PointF offset)
        {
            if (_renderList is null)
                return;

            target.PushTransform(BMatrix3x2.Translation(_box.X + offset.X, _box.Y + offset.Y));
            target.PushClip(new BRect(0, 0, _box.Width, _box.Height));
            RenderListReplay.Replay(target, _renderList.RenderList.Commands);

            // A frame does not scroll, so the frames inside it sit where its own layout put them.
            nested.Replay(target, PointF.Empty);
            target.PopClip();
            target.PopTransform();
        }

        public void ReleaseRenderList()
        {
            var renderList = _renderList;
            _renderList = null;
            renderList?.Dispose();
            nested.ReleaseRenderLists();
        }

        public void Dispose()
        {
            ReleaseRenderList();
            nested.Dispose();
            _container?.Dispose();
            _container = null;
        }

        /// <summary>
        /// Prepared as the page is: the scripts in it have run, and a <c>&lt;noscript&gt;</c> or an
        /// inner frame's fallback does not render where scripts do.
        /// </summary>
        private static string PrepareForBrowsing(string html) => HtmlPostProcessor.ProcessForBrowsing(html);

        /// <summary>
        /// The request context the frame's document loads as: a child of the page's, at the URL the
        /// frame's document came from. <see langword="null"/> with <see langword="true"/> for a page
        /// that loads nothing as anyone's document (the browser's own pages); <see langword="false"/>
        /// for a web page's frame whose document URL is on the local file system.
        /// </summary>
        private static bool TryGetDocument(HtmlContainer page, string baseUrl, out DocumentRequestContext? document)
        {
            document = null;
            if (page.DocumentContext is not { } parent)
                return true;

            var url = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) ? parsed : parent.DocumentUrl;
            if (url.IsFile && !parent.DocumentUrl.IsFile)
                return false;

            document = parent.CreateChild(url);
            return true;
        }
    }
}
