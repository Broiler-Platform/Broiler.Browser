using System.Text.Json;

namespace Broiler.App;

/// <summary>
/// Manages a persistent list of favorite URLs.
/// Favorites are stored as a JSON array in a file inside the user's
/// application-data directory so they survive across sessions.
/// </summary>
public sealed class FavoritesManager
{
    private readonly string? _filePath;
    private readonly List<string> _favorites = [];

    /// <summary>Current snapshot of favorites (read-only).</summary>
    public IReadOnlyList<string> Favorites => _favorites;

    /// <summary>The file favorites are kept in by default: <c>Broiler\favorites.json</c> under the user's application data.</summary>
    public static string DefaultFilePath => GetDefaultFilePath();

    public FavoritesManager()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Creates a manager that reads/writes the given file path.
    /// Useful for testing with a temporary file.
    /// </summary>
    /// <param name="filePath">
    /// The file to keep the list in, or <see langword="null"/> for a list that lives only in memory:
    /// <see cref="Load"/> starts it empty and <see cref="Save"/> writes nothing (an ephemeral profile).
    /// </param>
    public FavoritesManager(string? filePath)
    {
        _filePath = filePath;
    }

    /// <summary>Load favorites from disk. Safe to call at any time.</summary>
    public void Load()
    {
        _favorites.Clear();

        if (_filePath is null || !File.Exists(_filePath))
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
        if (_filePath is null)
            return;

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
    /// Returns <c>true</c> if the URL was added, <c>false</c> if it — or another spelling of the same
    /// URL — was already present.
    /// </summary>
    public bool Add(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (Contains(url))
            return false;

        _favorites.Add(url);
        return true;
    }

    /// <summary>
    /// Removes a URL from the favorites list, in whichever spelling it was saved.
    /// Returns <c>true</c> if it was found and removed.
    /// </summary>
    public bool Remove(string url) => _favorites.RemoveAll(favorite => SameUrl(favorite, url)) > 0;

    /// <summary>Returns <c>true</c> when the given URL, in any spelling, is in the list.</summary>
    public bool Contains(string url) => _favorites.Exists(favorite => SameUrl(favorite, url));

    /// <summary>
    /// Whether two favorites name the same URL: equal once each is in its canonical form
    /// (<see cref="Uri.AbsoluteUri"/>), which is what the address bar shows for a loaded page.
    /// </summary>
    /// <remarks>
    /// The address bar shows where the page was served from, in canonical form — a lower-case host, a
    /// <c>/</c> for an empty path, percent-encoding — while earlier builds showed, and saved, the URL as
    /// it was requested. Compared as strings, <c>https://example.com</c> saved then was never the page
    /// <c>https://example.com/</c> shown now: its star read as unsaved and a second click saved a
    /// duplicate that the first could not remove.
    /// </remarks>
    internal static bool SameUrl(string a, string b) =>
        string.Equals(Canonical(a), Canonical(b), StringComparison.Ordinal);

    private static string Canonical(string url)
    {
        string trimmed = url.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) ? uri.AbsoluteUri : trimmed;
    }

    private static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Broiler", "favorites.json");
    }
}
