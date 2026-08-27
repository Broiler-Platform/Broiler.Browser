using System.Drawing;

namespace Broiler.Layout;

/// <summary>
/// Host-provided loader for a replaced-element or background image. Created via
/// <see cref="ILayoutEnvironment.CreateImageLoader"/>; the loaded image is an
/// opaque handle (<see cref="Image"/>) the host understands. Lets layout request
/// image loading and read intrinsic geometry without binding to a graphics
/// backend (see <c>Broiler.Layout/docs/roadmap.md</c>).
/// </summary>
public interface ILayoutImageLoader : IDisposable
{
    /// <summary>The loaded image handle, or <c>null</c> if not yet loaded or load failed.</summary>
    object? Image { get; }

    /// <summary>
    /// Sub-rectangle of the image to use, or <see cref="RectangleF.Empty"/> for the whole image.
    /// </summary>
    RectangleF Rectangle { get; }

    /// <summary>Initiates image loading from the given source.</summary>
    void LoadImage(string src, IReadOnlyDictionary<string, string>? attributes, Uri baseUrl);
}
