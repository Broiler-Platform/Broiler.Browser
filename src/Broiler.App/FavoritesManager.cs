using System.Text.Json;

namespace Broiler.App;

/// <summary>
/// Manages a persistent list of favorite URLs.
/// Favorites are stored as a JSON array in a file inside the user's
/// application-data directory so they survive across sessions.
/// </summary>
public sealed class FavoritesManager
{
    private readonly string _filePath;
    private readonly List<string> _favorites = [];

    /// <summary>Current snapshot of favorites (read-only).</summary>
    public IReadOnlyList<string> Favorites => _favorites;

    public FavoritesManager()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Creates a manager that reads/writes the given file path.
    /// Useful for testing with a temporary file.
    /// </summary>
    public FavoritesManager(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>Load favorites from disk. Safe to call at any time.</summary>
    public void Load()
    {
        _favorites.Clear();

        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var urls = JsonSerializer.Deserialize<List<string>>(json);
            if (urls != null)
                _favorites.AddRange(urls);
        }
        catch (Exception)
        {
            // Corrupt or unreadable file – start with an empty list.
        }
    }

    /// <summary>Persist the current list to disk.</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_favorites, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception)
        {
            // Best-effort – do not crash the app if the save fails.
        }
    }

    /// <summary>
    /// Adds a URL to the favorites list (no duplicates).
    /// Returns <c>true</c> if the URL was added, <c>false</c> if it was already present.
    /// </summary>
    public bool Add(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (_favorites.Contains(url))
            return false;

        _favorites.Add(url);
        return true;
    }

    /// <summary>
    /// Removes a URL from the favorites list.
    /// Returns <c>true</c> if it was found and removed.
    /// </summary>
    public bool Remove(string url) => _favorites.Remove(url);

    /// <summary>Returns <c>true</c> when the given URL is in the list.</summary>
    public bool Contains(string url) => _favorites.Contains(url);

    private static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Broiler", "favorites.json");
    }
}
