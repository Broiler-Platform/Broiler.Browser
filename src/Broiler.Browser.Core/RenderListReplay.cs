using Broiler.Graphics.RenderList;

namespace Broiler.Browser;

/// <summary>Copies render commands into another render list, under whatever clip and transform it has open.</summary>
internal static class RenderListReplay
{
    public static void Replay(BRenderList target, IReadOnlyList<BRenderCommand> commands)
    {
        foreach (BRenderCommand command in commands)
        {
            switch (command)
            {
                case BRenderCommand.FillRect fill:
                    target.FillRect(fill.Rect, fill.Color);
                    break;
                case BRenderCommand.StrokeRect stroke:
                    target.StrokeRect(stroke.Rect, stroke.Color, stroke.Thickness);
                    break;
                case BRenderCommand.FillRoundedRect fillRounded:
                    target.FillRoundedRect(fillRounded.Rect, fillRounded.Color, fillRounded.RadiusX, fillRounded.RadiusY);
                    break;
                case BRenderCommand.StrokeRoundedRect strokeRounded:
                    target.StrokeRoundedRect(strokeRounded.Rect, strokeRounded.Color, strokeRounded.RadiusX, strokeRounded.RadiusY, strokeRounded.Thickness);
                    break;
                case BRenderCommand.FillTriangle triangle:
                    target.FillTriangle(triangle.A, triangle.B, triangle.C, triangle.Color);
                    break;
                case BRenderCommand.DrawText text:
                    target.DrawText(text.Text, text.Origin);
                    break;
                case BRenderCommand.DrawImage image:
                    target.DrawImage(image.Image, image.Source, image.Destination, image.Opacity);
                    break;
                case BRenderCommand.PushClip clip:
                    target.PushClip(clip.Rect);
                    break;
                case BRenderCommand.PopClip:
                    target.PopClip();
                    break;
                case BRenderCommand.PushTransform transform:
                    target.PushTransform(transform.Transform);
                    break;
                case BRenderCommand.PopTransform:
                    target.PopTransform();
                    break;
            }
        }
    }
}
