using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The window copies a page's render list into its own. A command the copy has no case for is
/// dropped without a trace: triangles were, and with them the diagonal corner joins of borders
/// whose sides differ in colour, such as Acid2's nose.
/// </summary>
public class RenderListReplayTests
{
    [Fact]
    public void Triangles_Are_Replayed_With_The_Commands_Around_Them()
    {
        var source = new BRenderList();
        source.PushClip(new BRect(0, 0, 100, 100));
        source.FillRect(new BRect(10, 10, 12, 12), BColor.Black);
        source.FillTriangle(new BPoint(10, 22), new BPoint(22, 22), new BPoint(22, 10), BColor.Red);
        source.PopClip();

        var target = new BRenderList();
        RenderListReplay.Replay(target, source.Commands);

        Assert.Equal(source.Commands, target.Commands);
    }
}
