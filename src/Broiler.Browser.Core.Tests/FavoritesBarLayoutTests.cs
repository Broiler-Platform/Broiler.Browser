using System.Collections.Concurrent;
using Broiler.App;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The favorites bar shows the profile's favorites whenever the window is wide enough for it, however
/// narrow it was before.
/// </summary>
/// <remarks>
/// Below 600 wide the window lays out compact and hides the bar, collapsing every favorite button. A
/// collapsed button measures to nothing, and the bar sized each button by its measured width and
/// collapsed any narrower than 24 — so once a window had been compact, its favorites stayed collapsed
/// at every width after. An Android phone always starts compact, in portrait, and turning it to
/// landscape showed an empty bar; a Linux window that the window manager first maps narrower than 600
/// is the same case.
/// </remarks>
public sealed class FavoritesBarLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "broiler-favorites-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AWideWindowShowsTheFavorites()
    {
        using var window = new Window(Path.Combine(_directory, "favorites.json"), new BSize(1100, 600));

        Assert.Contains("7-zip.org", window.Render());
    }

    [Fact]
    public void ACompactWindowHidesTheFavorites()
    {
        using var window = new Window(Path.Combine(_directory, "favorites.json"), new BSize(400, 600));

        Assert.DoesNotContain("7-zip.org", window.Render());
    }

    [Fact]
    public void AWindowWidenedFromCompactShowsTheFavorites()
    {
        using var window = new Window(Path.Combine(_directory, "favorites.json"), new BSize(400, 600));
        Assert.DoesNotContain("7-zip.org", window.Render());

        window.Size = new BSize(1100, 600);

        IReadOnlyList<string> texts = window.Render();
        foreach (string label in DefaultLabels)
            Assert.Contains(label, texts);
    }

    /// <summary>What the bar labels <see cref="FavoritesManager.DefaultFavorites"/> with: the host, less <c>www.</c>.</summary>
    private static readonly string[] DefaultLabels =
        ["7-zip.org", "acid1.acidtests.org", "acid2.acidtests.org", "html5test.com", "mediawiki.org", "duckduckgo.com"];

    /// <summary>A whole browser over a headless host whose size the test changes, on a profile of its own.</summary>
    private sealed class Window : IDisposable
    {
        private readonly ConcurrentQueue<Action> _posted = new();
        private readonly BrowserProfile _profile;
        private readonly BrowserUiHost _host;
        private readonly BImageRenderer _renderer = new();
        private readonly BrowserApp _app;

        public Window(string favoritesPath, BSize size)
        {
            Size = size;
            _profile = BrowserProfile.CreateWithFavorites(favoritesPath);
            _host = new BrowserUiHost(
                () => Size,
                static () => 1.0,
                static () => { },
                static _ => { },
                action => { _posted.Enqueue(action); return true; });
            _app = new BrowserApp(_host, () => _renderer, initialUrl: null, static _ => { }, _profile);
        }

        public BSize Size { get; set; }

        /// <summary>Lays the window out at its current size and returns the text runs of the frame.</summary>
        public IReadOnlyList<string> Render()
        {
            while (_posted.TryDequeue(out Action? action))
                action();

            _app.Invalidate();
            BRenderList frame = _app.RenderFrame();
            return frame.Commands.OfType<BRenderCommand.DrawText>().Select(text => text.Text.Text).ToList();
        }

        public void Dispose()
        {
            _app.Dispose();
            _renderer.Dispose();
            _profile.Dispose();
        }
    }
}
