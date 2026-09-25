using System.Runtime.CompilerServices;

namespace Broiler.Cli.Tests;

/// <summary>What the command line's <c>Main</c> composes before it runs anything, and these tests run without it.</summary>
internal static class TestComposition
{
    /// <summary>
    /// Broiler.Graphics encodes an image only through the codec catalog its host registers, as
    /// <c>Program.Main</c> does. The analysis's window image is such an image.
    /// </summary>
    [ModuleInitializer]
    internal static void RegisterImageCodecs() =>
        Broiler.Graphics.Imaging.BImageCodecs.Use(
            new Broiler.Media.MediaCodecCatalog(Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));
}
