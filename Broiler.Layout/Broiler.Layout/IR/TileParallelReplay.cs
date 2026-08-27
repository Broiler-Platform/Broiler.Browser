using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// Replays one <see cref="DisplayList"/> into several disjoint regions of a surface at once.
/// Multithreading roadmap item #5.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the tile is the unit and the primitive is not.</b> Item #4 split the scanlines of a
/// single fill across threads and measured 1.42× on one corpus page and nothing at all on three of
/// them: a page's raster is not a few big fills, it is thousands of small ones — 3 858 fills
/// averaging 95 pixels on the text page, which are glyphs, and no threshold makes a 95-pixel fill
/// worth a thread. Tiling partitions the <em>surface</em> instead and replays the whole list into
/// each part, so the unit of work is "every fill that touches this tile". That unit exists on every
/// page; the per-primitive one does not.
/// </para>
/// <para>
/// <b>Identical pixels by construction, not by comparison.</b> A tile view draws with the same
/// transform, the same clip stack and the same arithmetic as the sequential replay — the only
/// difference is one further clip narrowing which pixels it may write. Every primitive computes a
/// pixel from the input geometry alone, so a pixel's value does not depend on which tile drew it,
/// and the tiles partition the surface, so exactly one tile draws each pixel. That is why the exit
/// gate can be byte equality rather than a difference budget.
/// </para>
/// <para>
/// <b>Horizontal strips.</b> The rasterizer walks rows, its clip test is per pixel and its layer
/// buffers are full-surface, so a strip both narrows the row range a fill iterates and keeps each
/// tile's memory access contiguous. Rectangular tiles would narrow columns too, at the cost of
/// splitting every fill's row loop across tiles that then re-run its per-row setup; strips were the
/// cheaper shape to get right first and the partition is private to this class, so a later
/// measurement can change it without touching a surface.
/// </para>
/// <para>
/// <b>Two budgets, one core count.</b> Band parallelism (item #4) and tile parallelism are both
/// per-render and both default to one thread per core, so a host that enables both would ask for
/// cores² threads. A tile view is therefore expected to run its bands inline: the tiles already own
/// the cores. What a host still owns is the division between <em>its</em> parallelism and this one
/// — the WPT runner's worker pool and the CLI's batch processes each divide the render budget by
/// their process count.
/// </para>
/// </remarks>
public static class TileParallelReplay
{
    /// <summary>Environment variable that overrides the default tile budget.</summary>
    public const string TilesEnvironmentVariable = "BROILER_RASTER_TILES";

    /// <summary>Environment variable overriding <see cref="MinimumTileRows"/>.</summary>
    public const string MinimumTileRowsEnvironmentVariable = "BROILER_RASTER_MIN_TILE_ROWS";

    private static int _maxDegreeOfParallelism = ReadConfiguredDegree();

    [ThreadStatic] private static long _surfaceRefusals;
    [ThreadStatic] private static long _contentRefusals;
    [ThreadStatic] private static long _tiledReplays;
    [ThreadStatic] private static long _tiles;

    private static long _compatFallbacks;

    /// <summary>
    /// Maximum tiles one replay may be split into. <c>1</c> is the sequential replay — not an
    /// approximation of it, the same loop over the same list on the calling thread.
    /// </summary>
    public static int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => _maxDegreeOfParallelism = Math.Max(1, value);
    }

    /// <summary>
    /// Rows a tile must be worth before the surface is split any further.
    /// </summary>
    /// <remarks>
    /// A tile costs one full walk of the display list — every item is visited, and the ones that
    /// miss the tile are rejected on their bounds rather than skipped for free. That fixed cost is
    /// what this constant is against: splitting a 40-row surface four ways buys ten rows of drawing
    /// per list walk. Item #4's threshold is the cautionary precedent — a plausible value made the
    /// feature inert — so this one is deliberately low and is reported by the scaling harness, which
    /// prints the tile count it actually used.
    /// </remarks>
    public static int MinimumTileRows { get; set; } =
        ReadConfiguredPositive(MinimumTileRowsEnvironmentVariable, 64);

    /// <summary>
    /// Whether to count what the driver decided. Off by default; read once per replay.
    /// </summary>
    /// <remarks>
    /// The question a scaling table cannot answer on its own is whether a flat row means "the tiles
    /// did not help" or "the surface was never tiled". Those call for opposite next steps, which is
    /// why the count is here rather than inferred.
    /// </remarks>
    public static bool CollectDiagnostics { get; set; }

    /// <summary>
    /// Replays refused by the surface or the budget, replays refused by what the display list
    /// contains, replays split into tiles, and the total tiles created — counted on the calling
    /// thread since the last reset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per thread for the same reason item #4's counters are: the decision is taken on the thread
    /// that drives the replay, so a thread's totals describe exactly the renders it drove, with no
    /// interlocked writes and no cross-talk from a worker rendering alongside it.
    /// </para>
    /// <para>
    /// <b>The two refusals are separated because they call for opposite next steps.</b> A surface
    /// refusal means this render could not be tiled here — wrong backend, budget of one, a surface
    /// too short to divide. A content refusal means the display list held an item the raster canvas
    /// cannot draw by itself, so the page would need that item supported before tiling could reach
    /// it. A single "not tiled" count would hide which of those a flat scaling row is.
    /// </para>
    /// </remarks>
    public static (long SurfaceRefusals, long ContentRefusals, long TiledReplays, long Tiles) Diagnostics =>
        (_surfaceRefusals, _contentRefusals, _tiledReplays, _tiles);

    /// <summary>
    /// Tile views that had to materialize a compat canvas, since the process started or the last
    /// reset. <b>This should be zero.</b>
    /// </summary>
    /// <remarks>
    /// Tiling is only offered for a display list whose every item the raster canvas draws on its
    /// own, precisely so that no tile reaches the compat backend — which the tiles share and whose
    /// threading rules are the backend's, not ours. The eligibility test is per <em>item</em>, while
    /// a few of the adapter's fallbacks are decided per <em>call</em> (a pen that turns out not to
    /// be a simple stroke, a path that flattens to too few points), so the test is a necessary
    /// condition and not provably a sufficient one. This counter is how that gap is observed rather
    /// than assumed: the scaling harness prints it and the exit-gate test asserts it stayed zero.
    /// Process-wide and interlocked, because the fallback happens on a tile thread rather than on
    /// the thread that drove the replay.
    /// </remarks>
    public static long CompatFallbacks => Interlocked.Read(ref _compatFallbacks);

    /// <summary>Records that a tile view reached the compat backend. See <see cref="CompatFallbacks"/>.</summary>
    public static void NoteCompatFallback() => Interlocked.Increment(ref _compatFallbacks);

    /// <summary>Zeroes the calling thread's counters, and the process-wide fallback count.</summary>
    public static void ResetDiagnostics()
    {
        _surfaceRefusals = 0;
        _contentRefusals = 0;
        _tiledReplays = 0;
        _tiles = 0;
        Interlocked.Exchange(ref _compatFallbacks, 0);
    }

    /// <summary>
    /// Replays <paramref name="list"/> into tiles of <paramref name="surface"/> concurrently and
    /// returns <c>true</c>, or returns <c>false</c> without drawing anything when this surface and
    /// this budget do not justify tiling — in which case the caller replays sequentially as before.
    /// </summary>
    /// <param name="contentAllowsTiling">
    /// Whether every item in <paramref name="list"/> is one the surface can draw without falling
    /// back to a backend the tiles share. Decided by the caller, which is the component that knows
    /// what its own primitives need; a <c>false</c> here is counted separately from a surface that
    /// simply cannot be tiled.
    /// </param>
    /// <param name="replay">
    /// The sequential replay, invoked once per tile view. It must not retain the view: the driver
    /// disposes each one as soon as its tile finishes.
    /// </param>
    /// <remarks>
    /// Returning <c>false</c> <em>before</em> any pixel is written is the whole contract. A driver
    /// that discovered halfway through that a surface could not be tiled would have no way back:
    /// the primitives blend against what is already there, so re-running the list sequentially over
    /// a partly drawn surface does not reproduce the sequential image.
    /// </remarks>
    public static bool TryReplay(
        DisplayList list,
        RGraphics surface,
        bool contentAllowsTiling,
        Action<DisplayList, RGraphics> replay)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(replay);

        if (!contentAllowsTiling)
        {
            if (CollectDiagnostics)
                _contentRefusals++;
            return false;
        }

        var tiles = PartitionOrEmpty(surface);
        if (tiles.Length == 0)
        {
            if (CollectDiagnostics)
                _surfaceRefusals++;
            return false;
        }

        if (CollectDiagnostics)
        {
            _tiledReplays++;
            _tiles += tiles.Length;
        }

        var tileable = (ITileParallelSurface)surface;
        Parallel.For(
            0,
            tiles.Length,
            new ParallelOptions { MaxDegreeOfParallelism = tiles.Length },
            index =>
            {
                var view = tileable.CreateTileView(tiles[index]);
                try
                {
                    replay(list, view);
                }
                finally
                {
                    view.Dispose();
                }
            });

        return true;
    }

    /// <summary>
    /// The tiles this replay is worth, or an empty array whenever it should stay sequential.
    /// </summary>
    private static Rectangle[] PartitionOrEmpty(RGraphics surface)
    {
        if (_maxDegreeOfParallelism <= 1 || surface is not ITileParallelSurface tileable)
            return [];

        var size = tileable.TileParallelSurfaceSize;
        if (size.Width <= 0 || size.Height <= 0)
            return [];

        var rows = Math.Max(1, MinimumTileRows);
        var count = Math.Min(_maxDegreeOfParallelism, size.Height / rows);
        if (count <= 1)
            return [];

        var tiles = new Rectangle[count];
        var rowsPerTile = (size.Height + count - 1) / count;
        var next = 0;
        for (var i = 0; i < count; i++)
        {
            var top = i * rowsPerTile;
            var height = Math.Min(rowsPerTile, size.Height - top);
            if (height <= 0)
                break;

            tiles[next++] = new Rectangle(0, top, size.Width, height);
        }

        // An uneven split can leave the last tiles empty (height 100 over 3 tiles of 34 rows fills
        // in two). Hand back only the ones that cover rows, and fall back to the sequential replay
        // if that leaves one — a single "tile" is the sequential replay with a redundant clip.
        return next == tiles.Length ? tiles : next <= 1 ? [] : tiles[..next];
    }

    private static int ReadConfiguredDegree()
    {
        var configured = ReadConfiguredPositive(TilesEnvironmentVariable, 0);
        return configured > 0 ? configured : Environment.ProcessorCount;
    }

    private static int ReadConfiguredPositive(string variable, int fallback)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(configured) && int.TryParse(configured, out var value) && value > 0
            ? value
            : fallback;
    }
}
