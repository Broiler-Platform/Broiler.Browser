using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Broiler.Layout.Text;

/// <summary>
/// The fonts installed on this machine, indexed by the family name CSS asks for.
/// </summary>
/// <remarks>
/// <para>
/// Nothing enumerated them, so every <c>font-family</c> resolved to the one font the text backend
/// carries with it: <c>serif</c>, <c>monospace</c> and a named face all drew in the same sans.
/// That is not only a texture difference — a serif face is narrower than a sans at the same size,
/// so text set in one wraps where the other does not. MediaWiki's page heading
/// (<c>'Linux Libertine', Georgia, Times, serif</c>) fits on one line in a browser and took two
/// here, which moved every element below it down by a line.
/// </para>
/// <para>
/// The index reads each font's <c>name</c> table, which is the only place a file states the family
/// CSS will name it by — a filename is not it (<c>DejaVuSerif.ttf</c> is "DejaVu Serif") and
/// guessing from one gets the generic-family mapping wrong in exactly the cases that matter. It
/// lives here rather than in a graphics backend because it decides nothing about rasterisation: it
/// answers "which file is this family", which is a question about the document's style.
/// </para>
/// </remarks>
public static class SystemFontIndex
{
    /// <summary>
    /// Enough of a bound that no font set in practice is truncated, and small enough that a
    /// pathological directory cannot turn first paint into a filesystem walk.
    /// </summary>
    private const int MaxFilesScanned = 4000;

    /// <summary>
    /// CSS Fonts 4 §9: the generic families, each with the faces to prefer for it. The lists are
    /// the conventional Linux/Windows/macOS names in descending order of "what a browser on this
    /// platform would have picked".
    /// </summary>
    private static readonly (string Generic, string[] Candidates)[] GenericFamilies =
    [
        ("serif", ["Times New Roman", "Liberation Serif", "DejaVu Serif", "Nimbus Roman", "FreeSerif", "Noto Serif", "Bitstream Charter"]),
        ("sans-serif", ["Arial", "Helvetica", "Liberation Sans", "DejaVu Sans", "Nimbus Sans", "FreeSans", "Noto Sans"]),
        ("monospace", ["Courier New", "Liberation Mono", "DejaVu Sans Mono", "Nimbus Mono PS", "FreeMono", "Noto Sans Mono"]),
        ("cursive", ["Comic Sans MS", "URW Chancery L", "Z003", "DejaVu Serif"]),
        ("fantasy", ["Impact", "Papyrus", "DejaVu Sans"]),
        // CSS Fonts 4 §9.1: the UI generics resolve to the platform's interface font, which on a
        // headless box is simply its sans.
        ("system-ui", ["Liberation Sans", "DejaVu Sans", "Arial", "FreeSans"]),
        ("ui-sans-serif", ["Liberation Sans", "DejaVu Sans", "Arial", "FreeSans"]),
        ("ui-serif", ["Liberation Serif", "DejaVu Serif", "Times New Roman", "FreeSerif"]),
        ("ui-monospace", ["Liberation Mono", "DejaVu Sans Mono", "Courier New", "FreeMono"]),
        ("ui-rounded", ["Liberation Sans", "DejaVu Sans"]),
        ("math", ["Latin Modern Math", "DejaVu Serif", "Liberation Serif"]),
        ("emoji", ["Noto Color Emoji", "DejaVu Sans"]),
    ];

    private static readonly object Sync = new();

    private static Dictionary<string, FamilyFaces>? _families;

    /// <summary>One family's files, split by the two style axes CSS selects a face on.</summary>
    private sealed class FamilyFaces
    {
        public string? Regular;
        public string? Bold;
        public string? Italic;
        public string? BoldItalic;

        public string? Best(bool bold, bool italic) =>
            (bold, italic) switch
            {
                (true, true) => BoldItalic ?? Bold ?? Italic ?? Regular,
                (true, false) => Bold ?? Regular ?? BoldItalic ?? Italic,
                (false, true) => Italic ?? Regular ?? BoldItalic ?? Bold,
                _ => Regular ?? Bold ?? Italic ?? BoldItalic,
            };

        public void Add(string path, bool bold, bool italic)
        {
            ref string? slot = ref Regular;

            if (bold && italic)
                slot = ref BoldItalic;
            else if (bold)
                slot = ref Bold;
            else if (italic)
                slot = ref Italic;

            slot ??= path;
        }
    }

    /// <summary>
    /// The file to draw <paramref name="family"/> with, or <see langword="false"/> when this
    /// machine has no such family. <paramref name="family"/> may be a generic
    /// (<c>serif</c>, <c>monospace</c>, …), which resolves to the best installed face for it.
    /// </summary>
    public static bool TryResolve(string? family, bool bold, bool italic, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(family))
            return false;

        string wanted = Normalize(family);
        var families = EnsureIndexed();

        if (families.TryGetValue(wanted, out var faces) && faces.Best(bold, italic) is { } file)
        {
            path = file;
            return true;
        }

        foreach (var (generic, candidates) in GenericFamilies)
        {
            if (!string.Equals(generic, wanted, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var candidate in candidates)
            {
                if (families.TryGetValue(Normalize(candidate), out var fallback)
                    && fallback.Best(bold, italic) is { } fallbackFile)
                {
                    path = fallbackFile;
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    /// <summary>The family names this machine has, for a backend that wants to advertise them.</summary>
    public static IReadOnlyCollection<string> Families => [.. EnsureIndexed().Keys];

    /// <summary>
    /// Quotes and surrounding space are part of the CSS value, not of the family, and family names
    /// are matched case-insensitively (CSS Fonts 4 §2.2).
    /// </summary>
    private static string Normalize(string family)
    {
        string trimmed = family.Trim();

        if (trimmed.Length >= 2
            && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed.ToLowerInvariant();
    }

    private static Dictionary<string, FamilyFaces> EnsureIndexed()
    {
        if (_families is { } indexed)
            return indexed;

        lock (Sync)
        {
            if (_families is { } raced)
                return raced;

            var families = new Dictionary<string, FamilyFaces>(StringComparer.Ordinal);
            int scanned = 0;

            foreach (string root in FontRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    if (scanned >= MaxFilesScanned)
                        break;

                    string extension = Path.GetExtension(file);
                    if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                        && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    scanned++;

                    if (!TryReadNames(file, out string family, out string subfamily))
                        continue;

                    string key = Normalize(family);
                    if (!families.TryGetValue(key, out var faces))
                        families[key] = faces = new FamilyFaces();

                    faces.Add(
                        file,
                        subfamily.Contains("bold", StringComparison.OrdinalIgnoreCase),
                        subfamily.Contains("italic", StringComparison.OrdinalIgnoreCase)
                            || subfamily.Contains("oblique", StringComparison.OrdinalIgnoreCase));
                }
            }

            return _families = families;
        }
    }

    private static IEnumerable<string> FontRoots()
    {
        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";
        yield return "/system/fonts";
        yield return "C:/Windows/Fonts";
        yield return "/System/Library/Fonts";
        yield return "/Library/Fonts";

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".fonts");
            yield return Path.Combine(home, ".local", "share", "fonts");
        }
    }

    /// <summary>
    /// Reads the family and subfamily a font file states in its <c>name</c> table (OpenType §name).
    /// The typographic names (IDs 16/17) win over the legacy ones (1/2) where a file has them,
    /// because that is the pair a large family states its real grouping in.
    /// </summary>
    private static bool TryReadNames(string path, out string family, out string subfamily)
    {
        family = string.Empty;
        subfamily = string.Empty;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            uint tag = ReadUInt32(reader);

            // 0x00010000 (TrueType outlines) and 'OTTO' (CFF outlines) are the two a file can open
            // with here; a collection ('ttcf') holds several fonts and is left to a backend that
            // can index into one.
            if (tag != 0x00010000 && tag != 0x4F54544F)
                return false;

            int tableCount = ReadUInt16(reader);
            reader.BaseStream.Position += 6;

            long nameOffset = -1;
            for (int i = 0; i < tableCount; i++)
            {
                uint tableTag = ReadUInt32(reader);
                reader.BaseStream.Position += 4;
                uint offset = ReadUInt32(reader);
                reader.BaseStream.Position += 4;

                if (tableTag == 0x6E616D65) // 'name'
                    nameOffset = offset;
            }

            if (nameOffset < 0 || nameOffset >= stream.Length)
                return false;

            reader.BaseStream.Position = nameOffset;
            reader.BaseStream.Position += 2; // format
            int recordCount = ReadUInt16(reader);
            int stringOffset = ReadUInt16(reader);

            string? legacyFamily = null, legacySubfamily = null;
            string? typographicFamily = null, typographicSubfamily = null;

            for (int i = 0; i < recordCount; i++)
            {
                int platformId = ReadUInt16(reader);
                reader.BaseStream.Position += 2; // encodingID
                reader.BaseStream.Position += 2; // languageID
                int nameId = ReadUInt16(reader);
                int length = ReadUInt16(reader);
                int offset = ReadUInt16(reader);

                if (nameId is not (1 or 2 or 16 or 17))
                    continue;

                long resume = reader.BaseStream.Position;
                string? value = ReadNameString(reader, nameOffset + stringOffset + offset, length, platformId);
                reader.BaseStream.Position = resume;

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                switch (nameId)
                {
                    case 1: legacyFamily ??= value; break;
                    case 2: legacySubfamily ??= value; break;
                    case 16: typographicFamily ??= value; break;
                    case 17: typographicSubfamily ??= value; break;
                }
            }

            family = typographicFamily ?? legacyFamily ?? string.Empty;
            subfamily = typographicSubfamily ?? legacySubfamily ?? string.Empty;

            return !string.IsNullOrWhiteSpace(family);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }

    private static string? ReadNameString(BinaryReader reader, long offset, int length, int platformId)
    {
        if (offset < 0 || length <= 0 || offset + length > reader.BaseStream.Length)
            return null;

        reader.BaseStream.Position = offset;
        byte[] bytes = reader.ReadBytes(length);

        // Platform 1 (Macintosh) strings are single-byte; platforms 0 and 3 are UTF-16BE.
        return platformId == 1
            ? Encoding.ASCII.GetString(bytes)
            : Encoding.BigEndianUnicode.GetString(bytes);
    }

    private static uint ReadUInt32(BinaryReader reader) =>
        ((uint)reader.ReadByte() << 24) | ((uint)reader.ReadByte() << 16)
        | ((uint)reader.ReadByte() << 8) | reader.ReadByte();

    private static int ReadUInt16(BinaryReader reader) =>
        (reader.ReadByte() << 8) | reader.ReadByte();
}
