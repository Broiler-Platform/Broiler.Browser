using System;
using System.Collections.Generic;
using Broiler.Graphics;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Render lists for the <em>other</em> rasterizer — <c>Broiler.Graphics.BCanvas</c>, reached through
/// <see cref="BImageRenderer"/> — which multithreading item #3 is about.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this corpus is not <see cref="Corpus"/>.</b> The five HTML pages measure the rasterizer in
/// <c>Broiler.HTML.Image</c>; that is what <see cref="RasterScaling"/> and <see cref="TileScaling"/>
/// time, and the roadmap records that the profile's 46.9–80.8% raster share belongs to that copy.
/// Item #3's copy backs <see cref="BImageRenderer"/>, and through it Broiler.UI and the Writer —
/// hosts that draw a <see cref="BRenderList"/> directly and never parse a document. Timing item #3
/// through an HTML render would measure the wrong rasterizer; timing it through a synthetic
/// <c>FillRect</c> would measure the partitioner's best case and say nothing about a screen.
/// </para>
/// <para>
/// <b>The scenes are shaped by what those hosts actually emit.</b> Counting the draw calls in
/// <c>Broiler.UI/src</c> gives 46 <c>FillRect</c>, 27 <c>DrawText</c>, 14 <c>PushClip</c>, 5
/// <c>StrokeRect</c> and 2 <c>DrawImage</c> — small rectangles, text, and clipped panes, with
/// almost no large fills. <see cref="Chrome"/> is that mix at the scale of a full window.
/// <see cref="ScrolledList"/> isolates the case a clipped pane creates and the roadmap's §8 says to
/// port first: content far taller than the pane it is drawn into. <see cref="Canvas"/> is the
/// control — a handful of surface-sized fills, the one shape band parallelism can definitely
/// split — so a flat scaling row can be read as "this content has nothing to split" rather than
/// "the threads did not work".
/// </para>
/// <para>
/// Deterministic by construction: every string and colour is drawn from a fixed table by index, so
/// two runs on two machines build byte-identical lists.
/// </para>
/// </remarks>
internal static class GraphicsSceneCorpus
{
    /// <summary>Surface every scene is drawn into; the same 1280x1024 the HTML corpus renders at.</summary>
    public const int SurfaceWidth = Corpus.ViewportWidth;

    /// <inheritdoc cref="SurfaceWidth"/>
    public const int SurfaceHeight = Corpus.ViewportHeight;

    private static readonly string[] Words =
    [
        "Document", "Section", "Preview", "Settings", "Toolbar", "Palette", "Selection",
        "Paragraph", "Heading", "Outline", "Revision", "Comment", "Bookmark", "Reference",
    ];

    private static readonly BColor[] RowTints =
    [
        new BColor(0xF2, 0xF4, 0xF7),
        new BColor(0xE8, 0xEC, 0xF2),
        new BColor(0xFA, 0xFB, 0xFD),
        new BColor(0xEE, 0xF1, 0xF6),
    ];

    public static IReadOnlyList<(string Name, Func<BRenderList> Build)> Scenes { get; } =
    [
        ("chrome", Chrome),
        ("list", ScrolledList),
        ("pane", ClippedPane),
        ("canvas", Canvas),
    ];

    /// <summary>
    /// A full application window: title bar, toolbar buttons, a sidebar tree, a content pane of
    /// text rows, a status bar, and a border on everything. The UI draw-call mix at window scale.
    /// </summary>
    public static BRenderList Chrome()
    {
        var list = new BRenderList(2048);
        var text = new BColor(0x1B, 0x1F, 0x27);

        list.FillRect(new BRect(0, 0, SurfaceWidth, SurfaceHeight), new BColor(0xF7, 0xF8, 0xFA));
        list.FillRect(new BRect(0, 0, SurfaceWidth, 32), new BColor(0x24, 0x2A, 0x35));
        list.DrawText(Run("Document", 15, BFontWeight.SemiBold, BColor.White), new BPoint(12, 8));

        // Toolbar: 18 buttons, each a fill, a border and a label.
        for (int i = 0; i < 18; i++)
        {
            double x = 8 + (i * 68);
            list.FillRect(new BRect(x, 40, 62, 26), new BColor(0xFF, 0xFF, 0xFF));
            list.StrokeRect(new BRect(x, 40, 62, 26), new BColor(0xC6, 0xCC, 0xD6), 1);
            list.DrawText(Run(Word(i), 11, BFontWeight.Normal, text), new BPoint(x + 6, 46));
        }

        // Sidebar: a tree of 34 rows inside its own clipped pane.
        list.PushClip(new BRect(0, 76, 240, SurfaceHeight - 100));
        list.FillRect(new BRect(0, 76, 240, SurfaceHeight - 100), new BColor(0xEE, 0xF1, 0xF6));
        for (int i = 0; i < 34; i++)
        {
            double y = 80 + (i * 26);
            list.FillRect(new BRect(4, y, 232, 24), RowTints[i % RowTints.Length]);
            list.DrawText(Run(Word(i), 12, BFontWeight.Normal, text), new BPoint(12 + ((i % 3) * 10), y + 5));
        }

        list.PopClip();

        // Content pane: 30 rows of text with a leading marker, inside its own clip.
        list.PushClip(new BRect(248, 76, SurfaceWidth - 256, SurfaceHeight - 100));
        list.FillRect(new BRect(248, 76, SurfaceWidth - 256, SurfaceHeight - 100), BColor.White);
        for (int i = 0; i < 30; i++)
        {
            double y = 84 + (i * 30);
            list.FillRect(new BRect(256, y + 6, 8, 8), new BColor(0x3B, 0x74, 0xD8));
            list.DrawText(Run(Sentence(i), 13, BFontWeight.Normal, text), new BPoint(272, y));
        }

        list.PopClip();

        list.FillRect(new BRect(0, SurfaceHeight - 24, SurfaceWidth, 24), new BColor(0xE2, 0xE6, 0xEC));
        list.DrawText(Run("Ready", 11, BFontWeight.Normal, text), new BPoint(8, SurfaceHeight - 19));
        list.StrokeRect(new BRect(0, 0, SurfaceWidth, SurfaceHeight), new BColor(0x9A, 0xA3, 0xB2), 1);
        return list;
    }

    /// <summary>
    /// One clipped pane holding 1 200 rows, of which about thirty are inside it. The shape a
    /// scrolled list, tree or editor produces.
    /// </summary>
    /// <remarks>
    /// The rows below the pane are issued, not skipped by the caller: a host that already knew
    /// which rows were visible would not need a clip. Nearly all of them are also below the
    /// <em>surface</em>, though, which is why this is the scroll case and not the clip case — the
    /// loop clamp to the target's height rejects those with or without a clip bound. See
    /// <see cref="ClippedPane"/> for the one the bound is actually for.
    /// </remarks>
    public static BRenderList ScrolledList()
    {
        var list = new BRenderList(4096);
        var text = new BColor(0x1B, 0x1F, 0x27);

        list.FillRect(new BRect(0, 0, SurfaceWidth, SurfaceHeight), BColor.White);
        list.PushClip(new BRect(40, 40, SurfaceWidth - 80, 640));

        for (int i = 0; i < 1200; i++)
        {
            double y = 44 + (i * 22);
            list.FillRect(new BRect(44, y, SurfaceWidth - 88, 20), RowTints[i % RowTints.Length]);
            list.DrawText(Run(Sentence(i), 12, BFontWeight.Normal, text), new BPoint(52, y + 3));
        }

        list.PopClip();
        list.StrokeRect(new BRect(40, 40, SurfaceWidth - 80, 640), new BColor(0x9A, 0xA3, 0xB2), 1);
        return list;
    }

    /// <summary>
    /// A grid drawn across the whole surface inside a pane that clips it to a third of it — the
    /// case a clip bound can actually help with, which <see cref="ScrolledList"/> turns out not to
    /// be.
    /// </summary>
    /// <remarks>
    /// <b>The distinction is worth a scene of its own.</b> A primitive below the surface is already
    /// rejected without any clip bound, because every fill clamps its loop to the target's height;
    /// so a scrolled list whose overflow falls off the bottom of the screen is *not* a test of the
    /// narrowing, and measuring one produced a figure near 1.00× that would have been read as "the
    /// narrowing is worth nothing". What only a clip bound can reject is content that is on the
    /// surface and outside the pane — a list beside a sidebar, a table in the middle of a window,
    /// any pane that is not the whole screen. That is this scene: every cell is inside the surface,
    /// and roughly two thirds of them are outside the pane on one axis or the other.
    /// </remarks>
    public static BRenderList ClippedPane()
    {
        var list = new BRenderList(4096);
        var text = new BColor(0x1B, 0x1F, 0x27);
        var pane = new BRect(360, 320, 560, 384);

        list.FillRect(new BRect(0, 0, SurfaceWidth, SurfaceHeight), BColor.White);
        list.PushClip(pane);

        // A 10-column grid over the whole surface: on-surface throughout, mostly outside the pane.
        for (int row = 0; row < 46; row++)
        {
            double y = row * 22;
            for (int column = 0; column < 10; column++)
            {
                double x = column * 128;
                list.FillRect(new BRect(x + 2, y + 1, 124, 20), RowTints[(row + column) % RowTints.Length]);
                list.DrawText(Run(Word(row + column), 12, BFontWeight.Normal, text), new BPoint(x + 8, y + 4));
            }
        }

        list.PopClip();
        list.StrokeRect(pane, new BColor(0x9A, 0xA3, 0xB2), 1);
        return list;
    }

    /// <summary>
    /// The control: twelve surface-sized fills and a large rounded panel, and nothing small. This
    /// is the content band parallelism is able to split, so its row is what says whether the
    /// partitioner works at all.
    /// </summary>
    public static BRenderList Canvas()
    {
        var list = new BRenderList(32);

        for (int i = 0; i < 12; i++)
        {
            var tint = new BColor((byte)(0x30 + (i * 9)), (byte)(0x60 + (i * 5)), (byte)(0xC0 - (i * 7)), 0x40);
            list.FillRect(new BRect(0, 0, SurfaceWidth, SurfaceHeight), tint);
        }

        list.FillRoundedRect(new BRect(80, 80, SurfaceWidth - 160, SurfaceHeight - 160), new BColor(0xFF, 0xFF, 0xFF, 0xC0), 24, 24);
        return list;
    }

    private static BTextRun Run(string text, double size, BFontWeight weight, BColor color) =>
        new(text, new BFontStyle("sans-serif", size, weight), color);

    private static string Word(int index) => Words[index % Words.Length];

    private static string Sentence(int index) =>
        $"{Word(index)} {Word(index + 3)} {Word(index + 7)} {Word(index + 11)} {index}";
}
