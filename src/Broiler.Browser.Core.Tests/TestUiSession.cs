using Broiler.Graphics;
using Broiler.UI;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// A minimal Broiler.UI session for hosting controls in tests.
/// </summary>
/// <remarks>
/// Some behaviour only exists inside a session — radio-group exclusivity resolves
/// peers by walking <see cref="UiSession.Roots"/>, and keyboard input follows session
/// focus — so a host that is not attached to one silently does less. Tests that
/// exercise either use this rather than a bare element.
/// </remarks>
internal sealed class TestUiSession : IDisposable
{
    public TestUiSession(double width = 600, double height = 200)
    {
        Host = new TestUiHost(new BSize(width, height));
        Session = new StandardUiSessionBuilder()
            .WithDispatcher(new ImmediateUiDispatcher())
            .Build(Host);

        Root = new StandardPanel();
        Session.AddRoot(Root);
        Root.Arrange(new BRect(0, 0, width, height));
    }

    public UiSession Session { get; }

    public StandardPanel Root { get; }

    public TestUiHost Host { get; }

    public void Dispose() => Session.Dispose();

    internal sealed class TestUiHost(BSize viewportSize) : IUiHost
    {
        public BSize ViewportSize { get; } = viewportSize;

        public double Scale => 1;

        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);

        public void Invalidate(UiInvalidation invalidation)
        {
        }

        public void Present(BRenderList renderList)
        {
        }
    }
}
