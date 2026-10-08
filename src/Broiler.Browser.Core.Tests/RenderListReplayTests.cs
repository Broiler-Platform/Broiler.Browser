using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The window copies a page's render list into its own. A command the copy has no case for is
/// dropped without a trace: triangles were, and with them the diagonal corner joins of borders
/// whose sides differ in colour, such as Acid2's nose. So is anything a command carries that the
/// copy does not pass on: an image's sampling, which keeps Acid2's checkerboard tiles behind its
/// eyes solid yellow at 150%.
/// </summary>
public class RenderListReplayTests
{
    [Fact]
    public void Triangles_And_Image_Sampling_Are_Replayed_With_The_Commands_Around_Them()
    {
        var source = new BRenderList();
        source.PushClip(new BRect(0, 0, 100, 100));
        source.FillRect(new BRect(10, 10, 12, 12), BColor.Black);
        source.FillTriangle(new BPoint(10, 22), new BPoint(22, 22), new BPoint(22, 10), BColor.Red);
        source.DrawImage(new BImageHandle(new BResourceHandle(BResourceKind.Image, 1), new BSize(2, 2)), new BRect(0, 0, 2, 2), new BRect(30, 30, 2, 2), 1.0,
            BImageSampling.NearestNeighbor);
        source.PopClip();

        var target = new BRenderList();
        RenderListReplay.Replay(target, source.Commands);

        Assert.Equal(source.Commands, target.Commands);
    }
}
