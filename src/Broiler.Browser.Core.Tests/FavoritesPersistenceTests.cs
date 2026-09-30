using System.Text.Json;
using Broiler.App;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The favorites file across the move to a source-generated serializer (<c>FavoritesJson</c>): what one
/// manager saves the next loads, and the file is byte for byte what the reflection-based serializer wrote
/// before, so a file an earlier build left behind still loads.
/// </summary>
public sealed class FavoritesPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "broiler-favorites-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "favorites.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void SavedFavoritesLoadInANewManager()
    {
        var saving = new FavoritesManager(FilePath);
        Assert.True(saving.Add("https://example.com/"));
        Assert.True(saving.Add("https://www.7-zip.org/download.html"));
        saving.Save();

        var loading = new FavoritesManager(FilePath);
        loading.Load();

        Assert.Equal(["https://example.com/", "https://www.7-zip.org/download.html"], loading.Favorites);
    }

    [Fact]
    public void TheFileIsWhatTheReflectionSerializerWrote()
    {
        List<string> urls = ["https://example.com/", "https://www.7-zip.org/download.html"];
        var manager = new FavoritesManager(FilePath);
        foreach (string url in urls)
            manager.Add(url);
        manager.Save();

        string earlierBuilds = JsonSerializer.Serialize(urls, new JsonSerializerOptions { WriteIndented = true });
        Assert.Equal(earlierBuilds, File.ReadAllText(FilePath));
    }

    [Fact]
    public void AFileAnEarlierBuildWroteLoads()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(
            new List<string> { "https://example.com/" },
            new JsonSerializerOptions { WriteIndented = true }));

        var manager = new FavoritesManager(FilePath);
        manager.Load();

        Assert.Equal(["https://example.com/"], manager.Favorites);
    }
}
