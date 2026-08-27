using System;
using BenchmarkDotNet.Attributes;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Media.Image;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// P0-a harness (4): PNG and JPEG decode, the stages multithreading items #6, #7, and #8 are about.
/// </summary>
/// <remarks>
/// <para>
/// <b>The images are encoded at setup rather than checked in.</b> A committed fixture would fix the
/// compression ratio to whatever encoder produced it, and both decoders' cost is dominated by how
/// much entropy-coded data they have to walk: PNG's inflate and JPEG's Huffman pass are the parts
/// items #6 and #7 say cannot be parallelized, so the interesting variable is precisely how large a
/// share of the decode they are. Encoding here keeps that ratio a property of *this* repository's
/// encoders, which is what a decoder benchmark should track.
/// </para>
/// <para>
/// <b>Two content shapes, because they load the two decoders differently.</b> A photographic gradient
/// compresses poorly under PNG's filters and well under JPEG's DCT; flat blocks do the reverse. A
/// single image would give one point on a curve and read as if it were the whole answer.
/// </para>
/// <para>
/// <b>Decode goes through <c>BBitmap.Decode</c>, not the decoders directly</b>, because
/// <c>PngDecoder</c> and <c>JpegDecoder</c> are internal to <c>Broiler.Media.Image.Managed</c>.
/// That is the right seam anyway: it is the entry the renderer's image path and the WPT runner's
/// reference load both use, so what it measures is what the pipeline pays.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class DecodeBenchmarks
{
    /// <summary>Decoded image edge length. Square, so the figure scales with pixel count cleanly.</summary>
    [Params(512, 1024)]
    public int Size { get; set; } = 512;

    /// <summary>
    /// Image content: <c>gradient</c> is a smooth two-axis ramp, <c>blocks</c> is flat 16px tiles.
    /// </summary>
    [Params("gradient", "blocks")]
    public string Content { get; set; } = "gradient";

    private byte[] _png = [];
    private byte[] _jpeg = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        using var source = BuildImage(Size, Content);
        _png = source.Encode(ImageEncodeFormat.Png, 100);
        _jpeg = source.Encode(ImageEncodeFormat.Jpeg, 85);
    }

    /// <summary>Deterministic source image; no randomness, so two runs encode identical bytes.</summary>
    private static BBitmap BuildImage(int size, string content)
    {
        var bitmap = new BBitmap(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                BColor color;
                if (string.Equals(content, "blocks", StringComparison.Ordinal))
                {
                    var block = ((x / 16) + (y / 16)) % 5;
                    color = new BColor((byte)(40 * block), (byte)(200 - 30 * block), (byte)(60 + 40 * block), 255);
                }
                else
                {
                    color = new BColor(
                        (byte)(x * 255 / size),
                        (byte)(y * 255 / size),
                        (byte)((x + y) * 255 / (2 * size)),
                        255);
                }

                bitmap.SetPixel(x, y, color);
            }
        }

        return bitmap;
    }

    /// <summary>How many bytes the PNG encoder produced — context for the decode figure, not a timing.</summary>
    [Benchmark(Description = "png encoded size (control, not a timing)")]
    public int PngSize() => _png.Length;

    /// <summary>How many bytes the JPEG encoder produced — context for the decode figure, not a timing.</summary>
    [Benchmark(Description = "jpeg encoded size (control, not a timing)")]
    public int JpegSize() => _jpeg.Length;

    /// <summary>
    /// PNG decode: inflate, unfilter, expand to RGBA. Item #7 says only the last of the three is
    /// parallelizable, so a 1.3-1.6x ceiling is claimed there; this is the denominator for it.
    /// </summary>
    [Benchmark(Baseline = true, Description = "PNG decode")]
    public int DecodePng()
    {
        using var bitmap = BBitmap.Decode(_png);
        return bitmap.Width;
    }

    /// <summary>
    /// JPEG decode: entropy decode, dequantize + IDCT, upsample and colour-convert. Item #6 puts the
    /// serial Huffman share at about 35%, which is the number this benchmark exists to check rather
    /// than to assume.
    /// </summary>
    [Benchmark(Description = "JPEG decode")]
    public int DecodeJpeg()
    {
        using var bitmap = BBitmap.Decode(_jpeg);
        return bitmap.Width;
    }
}
