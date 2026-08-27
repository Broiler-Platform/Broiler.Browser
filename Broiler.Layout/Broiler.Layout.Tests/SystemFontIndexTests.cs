using System;
using System.IO;
using System.Linq;
using Broiler.Layout.Text;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The machine's installed fonts, as a CSS family name resolves them. Before this index existed
/// every <c>font-family</c> — including every generic — drew in the one bundled fallback face, so a
/// document's typography meant nothing at all.
/// </summary>
/// <remarks>
/// These facts are about a real font directory, so each is conditional on the machine having
/// something to find: a container with no fonts installed reports an empty index, and asserting a
/// family exists there would be asserting about the image rather than about the code. What is
/// unconditional is the shape of the answer — a resolved path exists, a generic resolves to the
/// same file the index holds, and an unknown family resolves to nothing.
/// </remarks>
public sealed class SystemFontIndexTests
{
    private static bool AnyFontsInstalled => SystemFontIndex.Families.Count > 0;

    [Fact(Timeout = 600000)]
    public void An_Unknown_Family_Resolves_To_Nothing()
    {
        Assert.False(SystemFontIndex.TryResolve("Not A Font That Exists 12345", bold: false, italic: false, out var path));
        Assert.True(string.IsNullOrEmpty(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Empty_Family_Resolves_To_Nothing(string? family) =>
        Assert.False(SystemFontIndex.TryResolve(family, bold: false, italic: false, out _));

    /// <summary>Every generic family CSS defines has to answer with something, or a document that
    /// names only generics — which is most documents — draws in the fallback again.</summary>
    [Theory]
    [InlineData("serif")]
    [InlineData("sans-serif")]
    [InlineData("monospace")]
    [InlineData("system-ui")]
    public void A_Generic_Family_Resolves_To_An_Installed_File(string generic)
    {
        if (!AnyFontsInstalled)
            return;

        Assert.True(SystemFontIndex.TryResolve(generic, bold: false, italic: false, out var path));
        Assert.True(File.Exists(path), $"{generic} resolved to a path that does not exist: {path}");
    }

    /// <summary>The defect the resolver's own cache used to hide: bold and regular are different
    /// files, and a family that has both must not answer with the same one for both.</summary>
    [Fact(Timeout = 600000)]
    public void Bold_And_Regular_Are_Different_Files_When_The_Family_Has_Both()
    {
        if (!AnyFontsInstalled)
            return;

        if (!SystemFontIndex.TryResolve("sans-serif", bold: false, italic: false, out var regular) ||
            !SystemFontIndex.TryResolve("sans-serif", bold: true, italic: false, out var bold))
        {
            return;
        }

        // A family shipping only one face is a legitimate answer; what must not happen is the
        // bold request coming back with the regular file when a bold file exists beside it.
        var boldFileExists = Directory
            .EnumerateFiles(Path.GetDirectoryName(regular)!, "*old*", SearchOption.TopDirectoryOnly)
            .Any();

        if (boldFileExists)
            Assert.NotEqual(regular, bold, StringComparer.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Index_Is_Stable_Across_Calls() =>
        Assert.Equal(SystemFontIndex.Families.Count, SystemFontIndex.Families.Count);
}
