using System;
using System.Drawing;
using BenchmarkDotNet.Attributes;
using Broiler.Graphics;
using Broiler.HTML.Image;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// P0-a harnesses (2), (3), and (5): <c>PerformLayout</c>, display-list raster at 1280x1024, and the
/// end-to-end headless render, each over the whole corpus.
/// </summary>
/// <remarks>
/// <para>
/// <b>The per-stage cases run against a container that is already past the earlier stages</b>, set up
/// in <see cref="IterationSetup"/> rather than in the timed method. That is the difference between
/// measuring layout and measuring parse-plus-cascade-plus-layout, and on the <c>rules</c> page the
/// two differ by an order of magnitude.
/// </para>
/// <para>
/// <b>Per-iteration setup, not per-invocation, and the cost is deliberate.</b> Layout and paint both
/// mutate the container — layout writes geometry into the box tree and publishes a fragment tree —
/// so a second invocation against the same container is measuring an already-laid-out tree, which is
/// the *incremental* relayout case (multithreading item #14) and not the one being measured here.
/// BenchmarkDotNet warns that iteration setup with a short workload inflates the measurement; the
/// answer is <see cref="InvocationCountAttribute"/> of 1, which makes each iteration exactly one
/// clean render of one stage.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class RenderStageBenchmarks
{
    /// <summary>Corpus page under test. Every case runs for every page.</summary>
    [ParamsSource(nameof(PageNames))]
    public string Page { get; set; } = "mixed";

    /// <summary>Parameter source; see <see cref="Corpus.Names"/>.</summary>
    public static string[] PageNames() => Corpus.Names();

    private Corpus.Page _page = null!;
    private HtmlContainer? _container;
    private BBitmap? _bitmap;
    private RectangleF _clip;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _page = Corpus.ByName(Page);
        _clip = new RectangleF(0, 0, Corpus.ViewportWidth, Corpus.ViewportHeight);
    }

    /// <summary>
    /// Rebuilds the container up to — but not including — the stage under test. Which stages run
    /// here is what makes each case measure one stage.
    /// </summary>
    private void Rebuild(bool layOut)
    {
        _container?.Dispose();
        _bitmap?.Dispose();

        _bitmap = new BBitmap(Corpus.ViewportWidth, Corpus.ViewportHeight);
        _bitmap.Clear(BColor.White);

        _container = new HtmlContainer
        {
            Location = new PointF(0, 0),
            MaxSize = new SizeF(Corpus.ViewportWidth, Corpus.ViewportHeight),
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
        };
        _container.SetHtmlWithStyleSet(_page.Html, null, null);

        if (layOut)
            _container.PerformLayout(_bitmap, _clip);
    }

    /// <summary>Leaves the tree cascaded but not laid out, which is where <see cref="Layout"/> starts.</summary>
    [IterationSetup(Target = nameof(Layout))]
    public void IterationSetupLayout() => Rebuild(layOut: false);

    /// <summary>Leaves the tree laid out, which is where both paint cases start.</summary>
    [IterationSetup(Targets = [nameof(PaintWalkAndRaster), nameof(PaintWalk)])]
    public void IterationSetupPaint() => Rebuild(layOut: true);

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _container?.Dispose();
        _bitmap?.Dispose();
        _container = null;
        _bitmap = null;
    }

    /// <summary>
    /// P0-a (5): the whole headless render, the number every stage share in
    /// <see cref="StageProfile"/> is taken against.
    /// </summary>
    [Benchmark(Baseline = true, Description = "end-to-end headless render")]
    public int EndToEnd()
    {
        using var bitmap = HtmlRender.RenderToImageWithStyleSet(
            _page.Html, Corpus.ViewportWidth, Corpus.ViewportHeight,
            backgroundColor: BColor.White);
        return bitmap.Width;
    }

    /// <summary>
    /// P0-a (1), as the pipeline actually runs it: HTML parse, box-tree construction, and the
    /// cascade, which <c>SetHtmlWithStyleSet</c> does in one call with no public seam between them.
    /// <see cref="CascadeBenchmarks"/> isolates the cascade half.
    /// </summary>
    [Benchmark(Description = "parse + cascade (SetHtmlWithStyleSet)")]
    public object ParseAndCascade()
    {
        using var container = new HtmlContainer
        {
            MaxSize = new SizeF(Corpus.ViewportWidth, Corpus.ViewportHeight),
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
        };
        container.SetHtmlWithStyleSet(_page.Html, null, null);
        return container;
    }

    /// <summary>P0-a (2): <c>CssBox.PerformLayout</c> over a parsed and cascaded tree.</summary>
    [Benchmark(Description = "layout (PerformLayout)")]
    [InvocationCount(1)]
    public SizeF Layout()
    {
        _container!.PerformLayout(_bitmap!, _clip);
        return _container.ActualSize;
    }

    /// <summary>
    /// P0-a (3): the paint walk and the raster together, which is what <c>PerformPaint</c> is. The
    /// pair is reported alongside <see cref="PaintWalk"/> so the raster half is the difference —
    /// the same split <see cref="StageProfile"/> makes, for the same reason.
    /// </summary>
    [Benchmark(Description = "paint walk + raster (PerformPaint)")]
    [InvocationCount(1)]
    public void PaintWalkAndRaster() => _container!.PerformPaint(_bitmap!, _clip);

    /// <summary>
    /// The paint walk alone: fragment tree to display list, no rasterization. Subtracting this from
    /// <see cref="PaintWalkAndRaster"/> gives the raster cost that multithreading items #3 and #5
    /// are aimed at.
    /// </summary>
    [Benchmark(Description = "paint walk only (CreateDisplayList)")]
    [InvocationCount(1)]
    public int PaintWalk() => _container!.CreateDisplayList().Items.Count;
}
