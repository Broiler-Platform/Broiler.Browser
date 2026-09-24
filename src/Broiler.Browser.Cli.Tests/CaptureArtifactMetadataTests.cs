using System.Text.Json;
using Broiler.HTML.Image;

namespace Broiler.Cli.Tests;

[Collection(CaptureCollection.Name)]
public class CaptureArtifactMetadataTests
{
    [Fact(Timeout = 600000)]
    public async Task Program_CaptureImage_Writes_Backend_Metadata_Sidecar()
    {
        var htmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        var metadataPath = CaptureArtifactMetadata.GetSidecarPath(outputPath);

        try
        {
            await File.WriteAllTextAsync(htmlPath, "<html><body style='margin:0'>artifact metadata</body></html>");

            var exitCode = await Program.Main([
                "--capture-image", htmlPath,
                "--output", outputPath,
                "--width", "32",
                "--height", "32",
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputPath));
            Assert.True(File.Exists(metadataPath));

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
            var root = document.RootElement;
            var renderBackend = root.GetProperty("renderBackend");

            Assert.Equal(Path.GetFileName(outputPath), root.GetProperty("imagePath").GetString());
            Assert.Equal(BGraphicsBackend.CurrentId, renderBackend.GetProperty("id").GetString());
            Assert.Equal(BGraphicsBackend.CurrentDisplayName, renderBackend.GetProperty("displayName").GetString());
            Assert.Equal(BGraphicsBackend.CurrentLabel, renderBackend.GetProperty("label").GetString());
        }
        finally
        {
            if (File.Exists(htmlPath))
                File.Delete(htmlPath);
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
        }
    }
}
