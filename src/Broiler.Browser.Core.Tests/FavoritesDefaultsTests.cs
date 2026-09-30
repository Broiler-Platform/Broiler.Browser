using Broiler.App;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The default favorites: a profile starts with them until it saves its own list, and never gets them
/// back after that.
/// </summary>
public sealed class FavoritesDefaultsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "broiler-favorites-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "favorites.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AProfileWithoutAFavoritesFileStartsWithTheDefaults()
    {
        var manager = new FavoritesManager(FilePath);
        manager.Load();

        Assert.Equal(
            [
                "https://www.7-zip.org",
                "http://acid1.acidtests.org",
                "http://acid2.acidtests.org",
                "https://html5test.com/",
                "https://www.mediawiki.org/wiki/MediaWiki",
                "https://duckduckgo.com/",
            ],
            manager.Favorites);
    }

    [Fact]
    public void ARemovedDefaultStaysRemoved()
    {
        var first = new FavoritesManager(FilePath);
        first.Load();
        Assert.True(first.Remove("https://duckduckgo.com/"));
        first.Save();

        var next = new FavoritesManager(FilePath);
        next.Load();

        Assert.DoesNotContain("https://duckduckgo.com/", next.Favorites);
        Assert.Equal(FavoritesManager.DefaultFavorites.Count - 1, next.Favorites.Count);
    }

    [Fact]
    public void AnEmptiedListStaysEmpty()
    {
        var first = new FavoritesManager(FilePath);
        first.Load();
        foreach (string url in FavoritesManager.DefaultFavorites)
            first.Remove(url);
        first.Save();

        var next = new FavoritesManager(FilePath);
        next.Load();

        Assert.Empty(next.Favorites);
    }

    [Fact]
    public void AnEphemeralProfileStaysEmpty()
    {
        var manager = new FavoritesManager(filePath: null);
        manager.Load();

        Assert.Empty(manager.Favorites);
    }
}
