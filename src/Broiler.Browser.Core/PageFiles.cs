using Broiler.HtmlBridge;

namespace Broiler.Browser;

/// <summary>
/// A file the user chose in the window's picker, as the page's file input receives it: its name, its
/// media type, its time and its bytes (<see cref="ChosenFile"/>).
/// </summary>
/// <remarks>
/// The type is what Chromium gives a picked file, which it decides from the file's extension: a table of
/// the common ones here, and the empty string -- as a <c>.bin</c> has in Chromium (measured) -- for one it
/// does not know.
/// </remarks>
internal static class PageFiles
{
    /// <summary>The file at <paramref name="path"/> as the page receives it, or null when it cannot be read.</summary>
    internal static ChosenFile? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new ChosenFile(info.Name, TypeOf(info.Extension), new DateTimeOffset(info.LastWriteTimeUtc), File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The media type a file named with <paramref name="extension"/> has, as Chromium decides it; empty when unknown.</summary>
    internal static string TypeOf(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".txt" or ".text" => "text/plain",
            ".htm" or ".html" => "text/html",
            ".css" => "text/css",
            ".js" or ".mjs" => "text/javascript",
            ".json" => "application/json",
            ".xml" => "text/xml",
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/vnd.microsoft.icon",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => string.Empty,
        };
}
