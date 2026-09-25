using Broiler.Cli.Analysis;
using Broiler.Layout.Diagnostics;

namespace Broiler.Cli.Tests;

/// <summary>
/// <see cref="LayoutGapRecorder"/> counts what Broiler.Layout reports while it listens, and leaves
/// the process-wide hooks as it found them.
/// </summary>
/// <remarks>In the capture collection: the hooks are process-wide, and a page rendered beside this test would report into it.</remarks>
[Collection(CaptureCollection.Name)]
public sealed class LayoutGapRecorderTests
{
    [Fact(Timeout = 600000)]
    public void Reports_Are_Counted_By_Name_With_Examples_While_Listening_And_Passed_On()
    {
        var passedOn = new List<string>();
        Action<string, string> previous = (property, value) => passedOn.Add($"{property}: {value}");
        LayoutDiagnostics.PropertyNotModeled = previous;
        LayoutDiagnostics.FallbackTaken = null;
        try
        {
            var recorder = new LayoutGapRecorder();
            using (recorder.Listen())
            {
                foreach (var value in (string[])["blur(4px)", "blur(4px)", "blur(8px)", "none", "blur(1px)"])
                    LayoutDiagnostics.PropertyNotModeled!("backdrop-filter", value);
                LayoutDiagnostics.PropertyNotModeled!("accent-color", "red");
                LayoutDiagnostics.FallbackTaken!("grid", "grid-template-columns: repeat(auto-fill, 1fr)");
            }

            LayoutDiagnostics.PropertyNotModeled!("after-listening", "x");

            Assert.Equal(
                ["backdrop-filter ×5: blur(4px) | blur(8px) | none", "accent-color ×1: red"],
                recorder.NotModeled.Select(static u => $"{u.Property} ×{u.Count}: {u.Value}"));
            Assert.Equal("grid", Assert.Single(recorder.Fallbacks).Property);
            Assert.Same(previous, LayoutDiagnostics.PropertyNotModeled);
            Assert.Null(LayoutDiagnostics.FallbackTaken);
            Assert.Equal(7, passedOn.Count);
        }
        finally
        {
            LayoutDiagnostics.PropertyNotModeled = null;
            LayoutDiagnostics.FallbackTaken = null;
        }
    }
}
