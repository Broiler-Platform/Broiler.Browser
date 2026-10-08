using System.Collections.Concurrent;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;

namespace Broiler.Browser;

/// <summary>
/// The head's renderer, with every image it creates also created in a separate
/// <see cref="BImageRenderer"/>, so that <c>--artifact-dir</c> can replay a frame on the CPU and
/// compare it with what the backend presented.
/// </summary>
/// <remarks>
/// An image handle names an image in the renderer that created it and nowhere else, so a frame
/// that draws one cannot simply be handed to another renderer: it fails with "Image #1 was not
/// created by this renderer". This keeps the second renderer's handle for each of the backend's,
/// and <see cref="RenderToCpuImage"/> replays a copy of the frame with its images swapped for
/// them. Everything else goes to the backend unchanged. Only an artifact run creates one, since it
/// holds a second copy of every image the page decodes.
/// </remarks>
internal sealed class CpuReplayRenderer(IBroilerRenderer backend) : IBroilerRenderer
{
    private readonly IBroilerRenderer _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    private readonly BImageRenderer _cpu = new();
    private readonly ConcurrentDictionary<ulong, BImageHandle> _cpuImages = new();

    public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor) => _backend.CreateSurface(descriptor);

    public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage)
    {
        BImageHandle image = _backend.CreateImage(encodedImage);
        try
        {
            _cpuImages[image.Handle.Id] = _cpu.CreateImage(encodedImage);
        }
        catch
        {
            // The caller sees the creation fail and will never release the backend's image.
            _backend.ReleaseImage(image);
            throw;
        }

        return image;
    }

    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        BImageHandle image = _backend.CreateImage(pixels);
        try
        {
            _cpuImages[image.Handle.Id] = _cpu.CreateImage(pixels);
        }
        catch
        {
            _backend.ReleaseImage(image);
            throw;
        }

        return image;
    }

    public void ReleaseImage(BImageHandle image)
    {
        _backend.ReleaseImage(image);
        if (_cpuImages.TryRemove(image.Handle.Id, out BImageHandle cpuImage))
            _cpu.ReleaseImage(cpuImage);
    }

    public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext) =>
        _backend.Render(surface, renderList, frameContext);

    public BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext) =>
        _backend.RenderToImage(renderList, descriptor, frameContext);

    /// <summary>Replays a frame the backend rendered through the platform-neutral CPU renderer.</summary>
    public BBitmap RenderToCpuImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        return _cpu.RenderToImage(WithCpuImages(renderList), descriptor, frameContext);
    }

    public void Dispose()
    {
        // The backend belongs to the caller; the images are the CPU renderer's own.
        _cpu.Dispose();
        _cpuImages.Clear();
    }

    // BRenderList records only through its typed methods, so the copy re-issues each command.
    private BRenderList WithCpuImages(BRenderList renderList)
    {
        BRenderList copy = new(renderList.Count);
        foreach (BRenderCommand command in renderList.Commands)
        {
            switch (command)
            {
                case BRenderCommand.FillRect c: copy.FillRect(c.Rect, c.Color); break;
                case BRenderCommand.StrokeRect c: copy.StrokeRect(c.Rect, c.Color, c.Thickness); break;
                case BRenderCommand.FillRoundedRect c: copy.FillRoundedRect(c.Rect, c.Color, c.RadiusX, c.RadiusY); break;
                case BRenderCommand.StrokeRoundedRect c: copy.StrokeRoundedRect(c.Rect, c.Color, c.RadiusX, c.RadiusY, c.Thickness); break;
                case BRenderCommand.FillTriangle c: copy.FillTriangle(c.A, c.B, c.C, c.Color); break;
                case BRenderCommand.DrawText c: copy.DrawText(c.Text, c.Origin); break;
                case BRenderCommand.DrawImage c: copy.DrawImage(CpuImage(c.Image), c.Source, c.Destination, c.Opacity, c.Sampling); break;
                case BRenderCommand.PushClip c: copy.PushClip(c.Rect); break;
                case BRenderCommand.PopClip: copy.PopClip(); break;
                case BRenderCommand.PushTransform c: copy.PushTransform(c.Transform); break;
                case BRenderCommand.PopTransform: copy.PopTransform(); break;
                default:
                    // A command Broiler.Graphics added after this was written. Failing names it;
                    // dropping it would write an artifact that differs for no visible reason.
                    throw new NotSupportedException(
                        $"The CPU replay does not know the render command {command.GetType().Name}; add it to {nameof(CpuReplayRenderer)}.");
            }
        }

        return copy;
    }

    private BImageHandle CpuImage(BImageHandle image) =>
        _cpuImages.TryGetValue(image.Handle.Id, out BImageHandle cpuImage)
            ? cpuImage
            : throw new InvalidOperationException(
                $"The frame draws image #{image.Handle.Id}, which was not created through {nameof(CpuReplayRenderer)}.");
}
