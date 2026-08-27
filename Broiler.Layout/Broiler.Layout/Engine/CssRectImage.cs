using System.Drawing;

namespace Broiler.Layout.Engine;

internal sealed class CssRectImage(CssBox owner) : CssRect(owner)
{
    private object _image;
    private RectangleF _imageRectangle;

    public override object Image
    {
        get { return _image; }
        set { _image = value; }
    }

    public override bool IsImage => true;

    public RectangleF ImageRectangle
    {
        get { return _imageRectangle; }
        set { _imageRectangle = value; }
    }

    /// <summary>
    /// HTML §4.8.4.3 <b>current pixel density</b>: how many image pixels this word's bitmap spends
    /// per CSS pixel, as chosen by <c>srcset</c>. <c>1</c> — the default, and what every image
    /// without a <c>srcset</c> keeps — means the bitmap is laid out at its decoded size; <c>2</c>
    /// halves it. Infinite for a candidate selected against a zero-width slot, whose natural size
    /// is zero.
    /// </summary>
    public double PixelDensity { get; set; } = 1.0;

    public override string ToString() => "Image";
}