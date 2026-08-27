namespace Broiler.Layout.IR;

/// <summary>
/// Draws a <see cref="DisplayList"/> to a platform surface.
/// Implementations: WpfRasterBackend.
/// </summary>
public interface IRasterBackend
{
    /// <summary>
    /// Replays the display list items onto the given surface.
    /// </summary>
    /// <param name="list">The display list to render.</param>
    /// <param name="surface">Platform-specific surface (e.g. a GDI+ Graphics or WPF DrawingContext).</param>
    void Render(DisplayList list, object surface);
}
