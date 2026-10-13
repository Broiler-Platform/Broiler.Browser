using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Broiler.CSS.Dom;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Paging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.HTML.Graphics;
using Broiler.HTML.Image;
using BBitmap = Broiler.Graphics.Imaging.BBitmap;

namespace Broiler.Browser;

/// <summary>
/// A web page as something a print dialog can paginate: laid out again, for print media, on
/// pages of the setup asked for, and drawn as vector pages.
/// </summary>
/// <remarks>
/// <para>
/// Printing re-lays the page out in a container of its own rather than reusing the one on screen.
/// The page area is a different width, <c>@media print</c> applies, and the layout paginates -
/// <see cref="HtmlContainer.PageSize"/> set to the page area moves forced breaks, boxes that must
/// not be split and lines that would straddle a page boundary onto the next page - and none of
/// that may disturb the view. The new container shares the view's subresource cache, so nothing
/// the page already fetched is fetched again, and loads its images before it lays out.
/// </para>
/// <para>
/// The laid-out document is drawn once, as one render list in CSS pixels, and each page is the
/// band of it one page area tall, scaled to points and clipped to the page's content box. A
/// command wholly outside a band is left out of that page.
/// </para>
/// <para>
/// What this does not print yet: the contents of iframes, which the view composites from frames
/// of their own; the <c>@page</c> rule's size and margins, which the page setup decides instead;
/// and running headers and footers.
/// </para>
/// </remarks>
internal sealed class BrowserPrintDocument : IBPagedDocument
{
    /// <summary>CSS pixels per point: 96 to the inch against 72.</summary>
    internal const double CssPixelsPerPoint = 96.0 / 72.0;

    private const float ContinuousPage = 99999;

    private readonly Func<SizeF, HtmlContainer> _createContainer;

    /// <param name="createContainer">
    /// Creates a container holding the page, sized for the viewport it is given; the document lays
    /// it out and disposes it.
    /// </param>
    public BrowserPrintDocument(string title, Func<SizeF, HtmlContainer> createContainer)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Web page" : title;
        _createContainer = createContainer ?? throw new ArgumentNullException(nameof(createContainer));
    }

    public string Title { get; }

    /// <summary>A4 with half-inch margins, close to the margins a browser prints with by default.</summary>
    public BPageSetup PreferredSetup { get; } = BPageSetup.Default with { Margins = BPageMargins.Uniform(36) };

    public IBPageSource Paginate(BPageSetup setup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setup);
        cancellationToken.ThrowIfCancellationRequested();

        BRect box = setup.ContentBox;
        int width = Math.Max(1, (int)Math.Round(box.Width * CssPixelsPerPoint));
        int height = Math.Max(1, (int)Math.Round(box.Height * CssPixelsPerPoint));

        var images = new PrintImageCollector();
        BRenderCommand[] commands;
        float total;

        // Print media and the page area are thread-static and must be in place before the page's
        // styles are computed, which is when the container is created.
        using (CssPagedMedia.Pin(width, height))
        {
            using HtmlContainer container = _createContainer(new SizeF(width, height));
            container.Location = PointF.Empty;
            container.MaxSize = new SizeF(width, height);
            container.PageSize = new SizeF(width, height);
            container.PerformLayout(new RectangleF(0, 0, width, height));
            cancellationToken.ThrowIfCancellationRequested();

            // The display list is clipped to the viewport the page and the maximum size leave, so
            // both are opened up to the whole laid-out document before it is drawn.
            total = Math.Max(height, container.ActualSize.Height);
            container.PageSize = new SizeF(ContinuousPage, ContinuousPage);
            container.MaxSize = new SizeF(width, total);
            using HtmlGraphicsRenderList drawn = HtmlGraphicsRenderListBuilder.Build(
                images, container.CreateDisplayList(), new RectangleF(0, 0, width, total));
            commands = [.. drawn.RenderList.Commands];
        }

        int pages = Math.Max(1, (int)Math.Ceiling((total / height) - 0.001));
        return new BrowserPrintPages(commands, images, setup, height, pages);
    }
}

/// <summary>The pages of a laid-out web page: one band of its render list each, built when first asked for.</summary>
internal sealed class BrowserPrintPages : IBPageSource
{
    private readonly BRenderCommand[] _commands;
    private readonly PrintImageCollector _images;
    private readonly BPageSetup _setup;
    private readonly double _pageHeight;
    private readonly BPage?[] _pages;

    public BrowserPrintPages(BRenderCommand[] commands, PrintImageCollector images, BPageSetup setup, double pageHeight, int pageCount)
    {
        _commands = commands;
        _images = images;
        _setup = setup;
        _pageHeight = pageHeight;
        _pages = new BPage?[pageCount];
    }

    public int PageCount => _pages.Length;

    public BPage GetPage(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages.Length);
        lock (_pages)
            return _pages[index] ??= Build(index);
    }

    private BPage Build(int index)
    {
        var builder = new BPageBuilder(_setup.PageSize);
        BRect box = _setup.ContentBox;
        double scale = 1 / BrowserPrintDocument.CssPixelsPerPoint;
        double top = index * _pageHeight;
        double bottom = top + _pageHeight;

        builder.Content.PushClip(box);
        builder.Content.PushTransform(new BMatrix3x2(scale, 0, 0, scale, box.Left, box.Top - (top * scale)));

        var pageImages = new Dictionary<ulong, BImageHandle>();
        int transforms = 0;
        foreach (BRenderCommand command in _commands)
        {
            switch (command)
            {
                case BRenderCommand.PushTransform:
                    transforms++;
                    break;
                case BRenderCommand.PopTransform:
                    transforms--;
                    break;
            }

            // Only where nothing has moved the drawing does a command's own position say where it lands.
            if (transforms == 0 && IsOutside(command, top, bottom))
                continue;

            if (command is BRenderCommand.DrawImage image)
            {
                if (!_images.TryGetPixels(image.Image, out BPixelBuffer? pixels))
                    continue;

                if (!pageImages.TryGetValue(image.Image.Handle.Id, out BImageHandle handle))
                {
                    handle = builder.AddImage(pixels);
                    pageImages[image.Image.Handle.Id] = handle;
                }

                builder.Content.Append(image with { Image = handle });
                continue;
            }

            builder.Content.Append(command);
        }

        builder.Content.PopTransform();
        builder.Content.PopClip();
        return builder.Build();
    }

    /// <summary>True for a drawing command that lies wholly above or below the band from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
    internal static bool IsOutside(BRenderCommand command, double top, double bottom)
    {
        (double Top, double Bottom)? extent = command switch
        {
            BRenderCommand.FillRect c => (c.Rect.Top, c.Rect.Bottom),
            BRenderCommand.StrokeRect c => (c.Rect.Top - c.Thickness, c.Rect.Bottom + c.Thickness),
            BRenderCommand.FillRoundedRect c => (c.Rect.Top, c.Rect.Bottom),
            BRenderCommand.StrokeRoundedRect c => (c.Rect.Top - c.Thickness, c.Rect.Bottom + c.Thickness),
            BRenderCommand.FillTriangle c => (Math.Min(c.A.Y, Math.Min(c.B.Y, c.C.Y)), Math.Max(c.A.Y, Math.Max(c.B.Y, c.C.Y))),
            BRenderCommand.DrawImage c => (c.Destination.Top, c.Destination.Bottom),
            // A run is drawn from its top-left origin; twice its size covers any line height and descender.
            BRenderCommand.DrawText c => (c.Origin.Y, c.Origin.Y + (c.Text.Font.Size * 2)),
            _ => null,
        };

        return extent is (double extentTop, double extentBottom) && (extentBottom <= top || extentTop >= bottom);
    }
}

/// <summary>
/// The renderer a page is drawn through for printing: it keeps the pixels of every image instead
/// of uploading them, so each printed page can carry its own.
/// </summary>
internal sealed class PrintImageCollector : IBroilerRenderer
{
    private readonly Dictionary<ulong, BPixelBuffer> _images = [];
    private ulong _next;

    public bool TryGetPixels(BImageHandle image, out BPixelBuffer pixels)
    {
        lock (_images)
            return _images.TryGetValue(image.Handle.Id, out pixels!);
    }

    public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage)
    {
        using BBitmap decoded = BBitmap.Decode(encodedImage);
        return CreateImage(decoded.ToPixelBuffer());
    }

    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        lock (_images)
        {
            ulong id = ++_next;
            _images[id] = pixels;
            return BImageHandle.FromId(id, new BSize(pixels.Width, pixels.Height));
        }
    }

    /// <summary>Kept: the pages draw them after the render list that asked for them is gone.</summary>
    public void ReleaseImage(BImageHandle image)
    {
    }

    public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor) =>
        throw new NotSupportedException("A print collector draws nothing itself.");

    public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext) =>
        throw new NotSupportedException("A print collector draws nothing itself.");

    public BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext) =>
        throw new NotSupportedException("A print collector draws nothing itself.");

    public void Dispose()
    {
    }
}
