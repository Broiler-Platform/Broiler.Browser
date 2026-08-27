using System;
using System.Drawing;

namespace Broiler.Layout.Tests;

/// <summary>
/// A layout environment with a page of a stated height and nothing else — the one thing the
/// fragmentation rules read, and the only thing that separates a paged layout from a screen one.
/// </summary>
/// <remarks>
/// Shared by the fragmentation test files because the page height <em>is</em> the fixture: the
/// forced-break and named-page rules are both governed by
/// <see cref="ILayoutEnvironment.PageSize"/> and by nothing else, so the same stub states the whole
/// setup for both. Text measurement returns nothing, which keeps every case a matter of box
/// geometry rather than of the font the container happens to resolve.
/// </remarks>
internal sealed class PagedLayoutEnvironment(double pageHeight, double pageWidth = 400)
    : ILayoutEnvironment
{
    public Broiler.Graphics.ILayoutFont GetFont(
        string family, double size, LayoutFontStyle style, string? fontFeatures = null) => new StubFont(size);

    public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => SizeF.Empty;

    public void MeasureText(
        Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth)
    {
        charFit = 0;
        charFitWidth = 0;
    }

    public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 0;

    public ImageIntrinsics GetImageIntrinsics(object imageHandle) => default;

    public Broiler.Graphics.BColor ParseColor(string value) => default;

    public void RequestRefresh(bool relayout) { }

    public SizeF ViewportSize => new((float)pageWidth, 1000);

    public PointF RootLocation => PointF.Empty;

    public SizeF ActualSize { get; set; }

    public bool AvoidGeometryAntialias => false;

    public SizeF PageSize => new((float)pageWidth, (float)pageHeight);

    public int MarginTop => 0;

    public void ReportLayoutError(string message, Exception? exception = null) { }

    public bool AvoidAsyncImagesLoading => true;

    public bool AvoidImagesLateLoading => true;

    public ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete) => null!;

    public string FormatListMarker(int number, string style) => string.Empty;

    private sealed class StubFont(double size) : Broiler.Graphics.ILayoutFont
    {
        public double Size { get; } = size;
        public double Height => size;
        public double UnderlineOffset => 0;
        public double LeftPadding => 0;
        public string? FontFeatures => null;
    }
}
