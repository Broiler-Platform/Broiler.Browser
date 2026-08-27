using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.Engine;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// The exit gate for multithreading item #14: every page the render-tree ledger declines to rebuild
/// must be the page a rebuild would have produced, to the byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this and not a unit test.</b> <c>RenderTreeInvalidationTests</c> asserts what the
/// classification <em>decides</em>, which is the easier half: a test that agrees with the
/// implementation's own reasoning cannot catch reasoning that is wrong about the engine. What can is
/// rendering the mutated page twice — once with the elision on and once with
/// <see cref="RenderTreeInvalidation.Elision"/> off, which is the version compare that shipped before
/// item #14 — and comparing the two images. A wrong classification is a stale page, and a stale page
/// differs in pixels.
/// </para>
/// <para>
/// <b>It paints, where the profile only lays out.</b> <see cref="RelayoutProfile"/> measures
/// <c>PerformLayout</c> because that is where the rebuild is; parity has to go all the way to the
/// surface, because a difference the layout pass carries but nothing draws is not the failure being
/// hunted. Both renders go through the same container lifecycle, so the only difference between the
/// two runs is whether the rebuild was skipped.
/// </para>
/// <para>
/// <b>Every mutation in the profile's list is covered, not only the elidable ones.</b> A mutation
/// that rebuilds both ways is a pair of identical renders and proves nothing on its own — but it is
/// the control that says a mismatch elsewhere is the elision and not the harness, and if a mutation
/// ever moves from one side to the other it is already covered.
/// </para>
/// </remarks>
internal static class RelayoutParity
{
    private const int Width = Corpus.ViewportWidth;
    private const int Height = Corpus.ViewportHeight;

    /// <summary>
    /// Renders every corpus page after every mutation, with the elision on and off, and compares the
    /// two images. Non-zero exit code if any pair differs.
    /// </summary>
    public static int Run()
    {
        var wasEnabled = RenderTreeInvalidation.Elision.Enabled;
        var mismatches = new List<string>();
        var elided = 0;
        var pairs = 0;

        Console.WriteLine("# Relayout elision parity (multithreading item #14)");
        Console.WriteLine();
        Console.WriteLine($"- Viewport: {Width}x{Height}");
        Console.WriteLine("- Each row: the page rendered after the mutation, elision on vs off");
        Console.WriteLine();
        Console.WriteLine($"| {"page",-8} | {"mutation",-20} | {"rebuild skipped",-15} | image |");
        Console.WriteLine($"|{new string('-', 10)}|{new string('-', 22)}|{new string('-', 17)}|-------|");

        try
        {
            foreach (var page in Corpus.Pages)
            {
                foreach (var mutation in RelayoutProfile.MutationsForParity)
                {
                    RenderTreeInvalidation.Elision.Enabled = false;
                    var reference = Render(page, mutation, out _);

                    RenderTreeInvalidation.Elision.Enabled = true;
                    var actual = Render(page, mutation, out var skipped);

                    pairs++;
                    if (skipped)
                        elided++;

                    var same = SameBytes(reference, actual);
                    if (!same)
                        mismatches.Add($"{page.Name}/{mutation.Name}");

                    Console.WriteLine(
                        $"| {page.Name,-8} | {mutation.Name,-20} | {(skipped ? "yes" : "no"),-15} | {(same ? "identical" : "**DIFFERS**")} |");
                }
            }
        }
        finally
        {
            RenderTreeInvalidation.Elision.Enabled = wasEnabled;
        }

        Console.WriteLine();
        Console.WriteLine($"{pairs} pairs, {elided} of them with the rebuild skipped.");

        // A run in which nothing was elided is a green run that tested nothing, which is the way
        // this harness could quietly stop being a gate — a classification that regressed to
        // "always rebuild" would pass it silently.
        if (elided == 0)
        {
            Console.WriteLine("FAIL: no rebuild was skipped in any pair, so nothing was compared that could differ.");
            return 1;
        }

        if (mismatches.Count > 0)
        {
            Console.WriteLine($"FAIL: {mismatches.Count} pair(s) differ: {string.Join(", ", mismatches)}");
            return 1;
        }

        Console.WriteLine("PASS: every elided relayout renders the page a rebuild would have.");
        return 0;
    }

    /// <summary>
    /// One first render and one post-mutation render on a fresh container, returning the painted
    /// surface and whether the second render skipped its rebuild.
    /// </summary>
    private static byte[] Render(Corpus.Page page, RelayoutProfile.MutationCase mutation, out bool skipped)
    {
        var clip = new RectangleF(0, 0, Width, Height);
        var parser = new HtmlDocumentParser();
        var document = parser.ParseDocument(page.Html).Document;

        using var bitmap = new BBitmap(Width, Height);
        using var container = new HtmlContainer();
        container.Location = new PointF(0, 0);
        container.MaxSize = new SizeF(Width, Height);
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;

        container.SetDocumentWithStyleSet(document, null, null);
        bitmap.Clear(BColor.White);
        container.PerformLayout(bitmap, clip);
        container.PerformPaint(bitmap, clip);

        mutation.Apply(page, document);

        RenderTreeInvalidation.Decisions.Reset();
        bitmap.Clear(BColor.White);
        container.PerformLayout(bitmap, clip);
        container.PerformPaint(bitmap, clip);
        skipped = RenderTreeInvalidation.Decisions.Elided > 0 && RenderTreeInvalidation.Decisions.Required == 0;

        return bitmap.Encode();
    }

    private static bool SameBytes(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
                return false;
        }

        return true;
    }
}
