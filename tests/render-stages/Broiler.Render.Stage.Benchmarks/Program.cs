using System;
using System.Globalization;
using BenchmarkDotNet.Running;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Entry point. Two modes, because P0-a asks two different questions.
/// </summary>
/// <remarks>
/// <c>--profile</c> answers "what share of a render is each stage" and is the command the P0-a exit
/// gate is checked with — it exits non-zero when the gate is missed. Everything else falls through
/// to BenchmarkDotNet, which answers "how long does one stage take, precisely". Mixing the two into
/// one command would mean either paying BenchmarkDotNet's multi-minute run to get a share, or
/// quoting a stopwatch figure as if it had BenchmarkDotNet's error bars.
/// </remarks>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--profile")
        {
            var iterations = ArgValue(args, "--iterations", 15);
            var warmup = ArgValue(args, "--warmup", 3);
            var json = ArgString(args, "--json");
            return StageProfile.Run(iterations, warmup, json);
        }

        if (args.Length >= 1 && args[0] == "--decode-scaling")
        {
            return DecodeScaling.Run(ArgValue(args, "--iterations", 11), ArgValue(args, "--warmup", 3));
        }

        if (args.Length >= 1 && args[0] == "--raster-scaling")
        {
            return RasterScaling.Run(ArgValue(args, "--iterations", 9), ArgValue(args, "--warmup", 2));
        }

        if (args.Length >= 1 && args[0] == "--graphics-raster-scaling")
        {
#if BROILER_GRAPHICS_RASTER_PARALLELISM
            var only = ArgValue(args, "--only-threads", 0);
            return GraphicsRasterScaling.Run(
                ArgValue(args, "--iterations", 9),
                ArgValue(args, "--warmup", 2),
                only > 0 ? only : null);
#else
            // Compiled out because the checked-out Broiler.Graphics has no raster partitioner; see
            // the condition in this project's .csproj and patches/0127 in patches/README.md.
            Console.WriteLine(
                "--graphics-raster-scaling needs patches/0127-graphics-raster-band-parallelism.patch"
                + " applied to the Broiler.Graphics submodule. See patches/README.md.");
            return 2;
#endif
        }

        if (args.Length >= 1 && args[0] == "--tile-scaling")
        {
            return TileScaling.Run(ArgValue(args, "--iterations", 9), ArgValue(args, "--warmup", 2));
        }

        if (args.Length >= 1 && args[0] == "--style-scaling")
        {
            return StyleScaling.Run(ArgValue(args, "--iterations", 9), ArgValue(args, "--warmup", 2));
        }

        if (args.Length >= 1 && args[0] == "--image-prefetch-scaling")
        {
            return ImagePrefetchScaling.Run(ArgValue(args, "--iterations", 9), ArgValue(args, "--warmup", 2));
        }

        if (args.Length >= 1 && args[0] == "--relayout-profile")
        {
            return RelayoutProfile.Run(ArgValue(args, "--iterations", 9), ArgValue(args, "--warmup", 2));
        }

        if (args.Length >= 1 && args[0] == "--js-context-scaling")
        {
            return JSContextScaling.Run(ArgValue(args, "--iterations", 7), ArgValue(args, "--warmup", 2));
        }

        if (args.Length >= 1 && args[0] == "--pixel-compare-cost")
        {
            return PixelCompareCost.Run(ArgValue(args, "--iterations", 11), ArgValue(args, "--warmup", 3));
        }

        if (args.Length >= 1 && args[0] == "--render-fixed-cost")
        {
            return RenderFixedCost.Run(ArgValue(args, "--iterations", 11), ArgValue(args, "--warmup", 4));
        }

        if (args.Length >= 1 && args[0] == "--layout-passes")
        {
            return LayoutPassProfile.Run(ArgValue(args, "--iterations", 9), ArgValue(args, "--warmup", 2));
        }

        if (args.Length >= 1 && args[0] == "--layout-composition")
        {
            return LayoutComposition.Run(ArgValue(args, "--iterations", 9), ArgValue(args, "--warmup", 2));
        }

        if (args.Length >= 1 && args[0] == "--layout-independence")
        {
            return LayoutIndependenceCensus.Run(
                ArgString(args, "--wpt-dir") ?? "tests/wpt",
                ArgValue(args, "--limit", 0));
        }

        if (args.Length >= 1 && args[0] == "--relayout-parity")
        {
            return RelayoutParity.Run();
        }

        if (args.Length >= 1 && args[0] == "--gc-config")
        {
            GcConfigurationMetrics.Run(ArgValue(args, "--rounds", 20));
            return 0;
        }

        if (args.Length >= 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            WriteUsage();
            return 0;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Broiler render-stage benchmarks (multithreading roadmap P0-a, #19).");
        Console.WriteLine();
        Console.WriteLine("  --profile [--iterations N] [--warmup N] [--json PATH]");
        Console.WriteLine("        Attribute headless-render wall time to named stages over the corpus.");
        Console.WriteLine("        Exits 1 if any page attributes less than 90% to named stages.");
        Console.WriteLine();
        Console.WriteLine("  --decode-scaling [--iterations N] [--warmup N]");
        Console.WriteLine("        PNG/JPEG decode time against the thread budget (items #6, #7).");
        Console.WriteLine("        Exits 1 if any thread setting decodes different pixels.");
        Console.WriteLine();
        Console.WriteLine("  --raster-scaling [--iterations N] [--warmup N]");
        Console.WriteLine("        Corpus render time against the raster thread budget (item #4).");
        Console.WriteLine("        Exits 1 if any thread setting renders different pixels.");
        Console.WriteLine();
        Console.WriteLine("  --graphics-raster-scaling [--iterations N] [--warmup N] [--only-threads N]");
        Console.WriteLine("        Broiler.Graphics scene replay time against the raster thread budget (item #3).");
        Console.WriteLine("        This is the BImageRenderer / Broiler.UI / Writer rasterizer, not the HTML one.");
        Console.WriteLine("        Exits 1 if any thread setting renders different pixels.");
        Console.WriteLine("        --only-threads N times ONE setting, which is the comparable measurement:");
        Console.WriteLine("        interleaving settings in one process makes the 1-thread baseline read 2.2x fast.");
        Console.WriteLine();
        Console.WriteLine("  --tile-scaling [--iterations N] [--warmup N]");
        Console.WriteLine("        Corpus render time against the raster tile budget (item #5).");
        Console.WriteLine("        Exits 1 if any tile setting renders different pixels.");
        Console.WriteLine();
        Console.WriteLine("  --style-scaling [--iterations N] [--warmup N]");
        Console.WriteLine("  --relayout-profile [--iterations N] [--warmup N]");
        Console.WriteLine("        Cascade sub-stage and render time against the style thread budget (item #12).");
        Console.WriteLine("        Exits 1 if any thread setting renders different pixels.");
        Console.WriteLine();
        Console.WriteLine("  --js-context-scaling [--iterations N] [--warmup N]");
        Console.WriteLine("        Whether JavaScript in separate JSContexts runs concurrently across threads");
        Console.WriteLine("        or serializes — the measurement item #18 (Web Workers) turns on.");
        Console.WriteLine();
        Console.WriteLine("  --pixel-compare-cost [--iterations N] [--warmup N]");
        Console.WriteLine("        What PixelDiffRunner.Compare costs and which half of it is the cost.");
        Console.WriteLine();
        Console.WriteLine("  --render-fixed-cost [--iterations N] [--warmup N]");
        Console.WriteLine("        Render time against document size through the entry the WPT runner uses,");
        Console.WriteLine("        fitted to separate per-render fixed cost from per-element cost.");
        Console.WriteLine();
        Console.WriteLine("  --layout-passes [--iterations N] [--warmup N]");
        Console.WriteLine("        How many full-tree layout passes one PerformLayout performs, per corpus");
        Console.WriteLine("        page and per caller shape (fixed viewport / auto-size / measure) — the");
        Console.WriteLine("        precondition for the Broiler.Layout roadmap's step 1.");
        Console.WriteLine();
        Console.WriteLine("  --layout-composition [--iterations N] [--warmup N]");
        Console.WriteLine("        What one layout pass spends its time on, and how much of the box tree sits");
        Console.WriteLine("        under an independent subtree — the premise check for item #13's two shapes.");
        Console.WriteLine();
        Console.WriteLine("  --layout-independence [--wpt-dir <dir>] [--limit N]");
        Console.WriteLine("        The same census over real WPT documents rather than the corpus, whose own");
        Console.WriteLine("        answer is a property of its generator. Counts only — no clock.");
        Console.WriteLine();
        Console.WriteLine("  --relayout-parity");
        Console.WriteLine("        Renders every corpus page after every mutation with the render-tree");
        Console.WriteLine("        elision on and off (item #14). Exits 1 if any elided relayout differs");
        Console.WriteLine("        by a byte, or if no rebuild was skipped at all.");
        Console.WriteLine();
        Console.WriteLine("  --image-prefetch-scaling [--iterations N] [--warmup N]");
        Console.WriteLine("        Render time of an image-heavy page against the concurrent-load budget (item #8).");
        Console.WriteLine("        Exits 1 if any setting renders different pixels.");
        Console.WriteLine();
        Console.WriteLine("  --gc-config [--rounds N]");
        Console.WriteLine("        Throughput and GC counters for the process's GC mode (item #19).");
        Console.WriteLine();
        Console.WriteLine("  --filter <pattern> [BenchmarkDotNet options]");
        Console.WriteLine("        Run the BenchmarkDotNet harnesses. See the project README.");
    }

    private static int ArgValue(string[] args, string name, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return int.Parse(args[i + 1], CultureInfo.InvariantCulture);
        }

        return fallback;
    }

    private static string? ArgString(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }

        return null;
    }
}
