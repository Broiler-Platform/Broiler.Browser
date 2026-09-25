using System.Globalization;
using System.Text;

namespace Broiler.Cli.Analysis;

/// <summary>Writes an <see cref="AnalysisReport"/> as <c>report.md</c>.</summary>
/// <remarks>
/// Ordered the way a reader diagnoses: what went wrong first (the ranked findings), then the evidence
/// for each area in the order a page is processed — the document, its scripts, its network, the
/// render, the layout, its markup, its stylesheets — and last the run itself.
/// </remarks>
internal static class MarkdownReport
{
    public static string Write(AnalysisReport report)
    {
        var md = new StringBuilder();
        md.AppendLine("# Broiler page analysis").AppendLine();
        md.Append("**").Append(Escape(report.Url)).AppendLine("**").AppendLine();

        md.AppendLine("| | |");
        md.AppendLine("| --- | --- |");
        Row(md, "Started", report.StartedAt.ToString("u", CultureInfo.InvariantCulture));
        Row(md, "Duration", Ms(report.DurationMs));
        Row(md, "Viewport", report.Viewport);
        Row(md, "JavaScript engine", $"{report.Environment.Engine} {report.Environment.EngineVersion}");
        Row(md, "Completed", report.Completed ? "yes" : $"no{(report.WatchdogFired is { } w ? " — " + w : string.Empty)}");
        if (report.Document is { } document)
        {
            Row(md, "Final URL", document.FinalUrl);
            Row(md, "Status", $"{document.Status} {document.ContentType ?? "(no content type)"}");
            Row(md, "Document size", Bytes(document.Bytes));
        }

        md.AppendLine();

        if (report.Render?.ViewportImage is { } viewport)
        {
            md.Append("![The page after its scripts](").Append(viewport).AppendLine(")").AppendLine();
            md.Append("Also: ");
            md.Append(Link(report.Render.FullPageImage, "full page"));
            md.Append(Link(report.Render.BoxesImage, "layout boxes"));
            md.Append(Link(report.RenderWithoutScripts?.ViewportImage, "without scripts"));
            md.AppendLine().AppendLine();
        }

        Findings(md, report);
        Scripting(md, report);
        Network(md, report);
        Render(md, report);
        Layout(md, report);
        Html(md, report);
        Css(md, report);
        Exceptions(md, report);
        Phases(md, report);
        Files(md, report);
        Environment(md, report);
        return md.ToString();
    }

    private static void Findings(StringBuilder md, AnalysisReport report)
    {
        md.AppendLine("## Findings").AppendLine();
        if (report.Findings.Count == 0)
        {
            md.AppendLine("Nothing stood out. The files below still hold everything the run produced.").AppendLine();
            return;
        }

        md.AppendLine("| Severity | Area | Finding | Evidence | See |");
        md.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var finding in report.Findings)
        {
            md.Append("| ").Append(finding.Severity)
                .Append(" | ").Append(finding.Area)
                .Append(" | ").Append(Escape(finding.Title))
                .Append(" | ").Append(Escape(finding.Detail))
                .Append(" | ").Append(finding.SeeAlso is { } see ? $"[{Escape(see)}]({see})" : string.Empty)
                .AppendLine(" |");
        }

        md.AppendLine();
    }

    private static void Scripting(StringBuilder md, AnalysisReport report)
    {
        if (report.Scripting is not { } js)
            return;

        md.AppendLine("## JavaScript").AppendLine();
        md.AppendLine("| | |");
        md.AppendLine("| --- | --- |");
        Row(md, "Engine", js.Engine);
        Row(md, "Scripts", $"{js.ClassicScripts} classic, {js.DeferredScripts} deferred, {js.ModuleRoots} module root(s)");
        Row(md, "Failures", $"{js.Failures} ({js.DistinctFailures.Count} distinct)");
        Row(md, "Unhandled rejections", js.UnhandledRejections.ToString(CultureInfo.InvariantCulture));
        Row(md, "Exceptions thrown inside JavaScript", $"{js.JavaScriptExceptions} (caught ones included)");
        Row(md, "Console", js.Console.Count == 0 ? "silent" : string.Join(", ", js.Console.Select(static c => $"{c.Key} ×{c.Value}")));
        Row(md, "Load window", js.SettleExhausted ? "did not settle (iteration budget spent)" : js.PendingWorkAfterSettle ? "settled; timers still queued beyond it" : "settled");
        Row(md, "Navigation not followed", js.NavigationNotFollowed ?? "none");
        Row(md, "Layouts caused by geometry questions", string.Create(CultureInfo.InvariantCulture,
            $"{js.GeometryLayouts} of {js.GeometryRequests} request(s), {js.GeometryLayoutMs:0} ms in total, slowest {js.SlowestGeometryLayoutMs:0} ms"));
        md.AppendLine();

        if (js.LongTurns.Count > 0)
        {
            md.AppendLine("### Long turns and idle gaps").AppendLine();
            md.AppendLine("From the script bridge's turn trace: a *turn* is JavaScript running (busy), a *gap* is the page waiting between turns (idle).").AppendLine();
            md.AppendLine("```");
            foreach (var line in js.LongTurns)
                md.AppendLine(line);
            md.AppendLine("```").AppendLine();
        }

        if (js.DistinctFailures.Count > 0)
        {
            md.AppendLine("### Distinct failures, most frequent first").AppendLine();
            foreach (var group in js.DistinctFailures.Take(40))
            {
                md.Append("- **×").Append(group.Count).Append("** ");
                if (group.FirstContext is { Length: > 0 } context)
                    md.Append('`').Append(context).Append("` — ");
                md.AppendLine(Escape(group.Example));
            }

            md.AppendLine();
        }

        if (js.MissingApis.Count > 0)
        {
            md.AppendLine("### Platform features the page asked for and did not get").AppendLine();
            md.AppendLine("| Name | Kind | Failures |");
            md.AppendLine("| --- | --- | --- |");
            foreach (var api in js.MissingApis.Take(40))
                md.Append("| `").Append(api.Name).Append("` | ").Append(api.Kind).Append(" | ").Append(api.Count).AppendLine(" |");
            md.AppendLine();
        }

        if (js.Timings.Count > 0)
        {
            md.AppendLine("### Script timings").AppendLine();
            md.AppendLine("Each label matches a file in `resources/` (see `resources/index.json`) and the labels in `javascript-errors.log`.").AppendLine();
            md.AppendLine("| Script | ms | Completed |");
            md.AppendLine("| --- | --- | --- |");
            foreach (var timing in js.Timings.OrderByDescending(static t => t.Ms).Take(40))
                md.Append("| `").Append(timing.Label).Append("` | ").Append(Ms(timing.Ms)).Append(" | ").Append(timing.Succeeded ? "yes" : "**threw**").AppendLine(" |");
            md.AppendLine();
        }
    }

    private static void Network(StringBuilder md, AnalysisReport report)
    {
        if (report.Network is not { } net)
            return;

        md.AppendLine("## Network").AppendLine();
        md.Append(net.Requests).Append(" request(s), ").Append(net.Failed).Append(" failed, ")
            .Append(Bytes(net.Bytes)).Append(" received. ")
            .AppendLine(string.Join(", ", net.ByDestination.Select(static d => $"{d.Key} ×{d.Value}")));
        if (net.Requests == 0)
            md.AppendLine("Nothing went over HTTP: a `file:` page and what it references are read from disk, and a `data:` URL is decoded in place.");
        if (net.NeverRead > 0)
            md.Append("- ").Append(net.NeverRead).AppendLine(" response body/bodies were never read by anything.");
        if (net.StillPending > 0)
            md.Append("- ").Append(net.StillPending).AppendLine(" request(s) had no response when the analysis ended.");
        md.AppendLine();

        if (net.Failures.Count > 0)
        {
            md.AppendLine("### Failed requests").AppendLine();
            md.AppendLine("| Destination | Status | URL | Error |");
            md.AppendLine("| --- | --- | --- | --- |");
            foreach (var failure in net.Failures)
            {
                md.Append("| ").Append(failure.Destination)
                    .Append(" | ").Append(failure.Status?.ToString(CultureInfo.InvariantCulture) ?? "—")
                    .Append(" | `").Append(Escape(failure.Url))
                    .Append("` | ").Append(Escape(failure.Error ?? failure.BodyError ?? failure.StatusText ?? string.Empty)).AppendLine(" |");
            }

            md.AppendLine();
        }

        if (net.Slowest.Count > 0)
        {
            md.AppendLine("### Slowest requests").AppendLine();
            md.AppendLine("| ms to headers | ms to last byte | Destination | URL |");
            md.AppendLine("| --- | --- | --- | --- |");
            foreach (var slow in net.Slowest)
            {
                md.Append("| ").Append(Ms(slow.HeadersMs))
                    .Append(" | ").Append(slow.CompleteMs is { } complete ? Ms(complete) : "—")
                    .Append(" | ").Append(slow.Destination)
                    .Append(" | `").Append(Escape(slow.Url)).AppendLine("` |");
            }

            md.AppendLine();
        }
    }

    private static void Render(StringBuilder md, AnalysisReport report)
    {
        if (report.Render is not { } render)
            return;

        md.AppendLine("## Render").AppendLine();
        md.AppendLine("| | |");
        md.AppendLine("| --- | --- |");
        Row(md, "Content size", render.ContentSize);
        Row(md, "Canvas background", render.CanvasBackground ?? "—");
        Row(md, "Display list", $"{render.DisplayListItems} item(s): {string.Join(", ", render.DisplayListByKind.Take(8).Select(static k => $"{k.Key} ×{k.Value}"))}");
        Row(md, "Requests from the renderer", $"{render.StylesheetRequests} stylesheet(s), {render.ImageRequests} image(s)");
        Row(md, "Embedded documents composited", render.EmbeddedDocuments.ToString(CultureInfo.InvariantCulture));
        Row(md, "Timings", string.Join(", ", render.Timings.Select(static t => $"{t.Key} {Ms(t.Value)}")));
        if (report.ScriptVisualEffect is { } effect)
            Row(md, "Pixels changed by the scripts", effect.ToString("P2", CultureInfo.InvariantCulture));
        md.AppendLine();

        if (render.Errors.Count > 0)
        {
            md.AppendLine("### Render errors").AppendLine();
            foreach (var error in render.Errors.Take(40))
                md.Append("- `").Append(error.Kind).Append("` at ").Append(Ms(error.AtMs)).Append(": ").AppendLine(Escape(error.Detail));
            md.AppendLine();
        }
    }

    private static void Layout(StringBuilder md, AnalysisReport report)
    {
        if (report.Layout is not { } layout)
            return;

        md.AppendLine("## Layout").AppendLine();
        md.Append(layout.Boxes).Append(" box(es), ").Append(layout.TextRuns).Append(" text run(s), depth ").Append(layout.MaxDepth)
            .Append(", content ").Append(layout.ContentSize).AppendLine(". Boxes by display: " +
                string.Join(", ", layout.BoxesByDisplay.Take(10).Select(static b => $"{b.Key} ×{b.Value}")) + ".").AppendLine();

        if (layout.InvariantViolations.Count > 0)
        {
            md.AppendLine("### Invariant violations (Broiler.Layout's own checker)").AppendLine();
            foreach (var violation in layout.InvariantViolations.Take(25))
                md.Append("- ").AppendLine(Escape(violation));
            md.AppendLine();
        }

        FindingTable(md, "Far taller than their content", layout.TallerThanContent);
        FindingTable(md, "Reaching past the right edge of the viewport", layout.HorizontalOverflow);
        FindingTable(md, "Holding text but laid out with no width or height", layout.CollapsedWithText);
        FindingTable(md, "Placed entirely off the page", layout.OffPage);

        List(md, "Layout files that could not be written", layout.DumpErrors);

        if (layout.UnboxedElementsByTag.Count > 0)
        {
            md.AppendLine("### Elements in the body with no layout box").AppendLine();
            md.AppendLine("`display: none`, or content the renderer does not draw: " +
                string.Join(", ", layout.UnboxedElementsByTag.Take(20).Select(static u => $"`{u.Key}` ×{u.Value}"))).AppendLine();
        }
    }

    private static void FindingTable(StringBuilder md, string title, IReadOnlyList<LayoutFinding> findings)
    {
        if (findings.Count == 0)
            return;

        md.Append("### ").AppendLine(title).AppendLine();
        md.AppendLine("| Element | Inside | Border box | Detail |");
        md.AppendLine("| --- | --- | --- | --- |");
        foreach (var finding in findings)
        {
            md.Append("| `").Append(Escape(finding.Element))
                .Append("` | ").Append(Escape(finding.Path))
                .Append(" | ").Append(finding.Box)
                .Append(" | ").Append(Escape(finding.Detail)).AppendLine(" |");
        }

        md.AppendLine();
    }

    private static void Html(StringBuilder md, AnalysisReport report)
    {
        if (report.Html is not { } html)
            return;

        md.AppendLine("## HTML").AppendLine();
        md.AppendLine("| | |");
        md.AppendLine("| --- | --- |");
        Row(md, "Doctype", html.Doctype ?? "**none**");
        Row(md, "Rendering mode", html.QuirksMode ? "**quirks**" : "standards");
        Row(md, "Declared charset", html.DeclaredCharset ?? "none");
        Row(md, "Title", html.Title ?? "none");
        Row(md, "Language", html.Language ?? "none");
        Row(md, "Viewport meta", html.Viewport ?? "none");
        Row(md, "Base URL", html.BaseHref ?? "none");
        Row(md, "Refresh", html.MetaRefresh ?? "none");
        Row(md, "Elements", $"{html.ElementsAsFetched:N0} as fetched → {html.ElementsAfterScripts:N0} after scripts");
        Row(md, "Body text", $"{html.TextLengthAsFetched:N0} → {html.TextLengthAfterScripts:N0} characters");
        Row(md, "Deepest nesting", html.MaxDepth.ToString(CultureInfo.InvariantCulture));
        Row(md, "Styling", $"{html.InlineStyleElements} <style> element(s), {html.StyleAttributes} style attribute(s), {html.Stylesheets.Count} linked stylesheet(s)");
        Row(md, "Forms", $"{html.Forms} form(s), {html.FormControls} control(s)");
        Row(md, "Media and graphics", html.MediaAndGraphics.Count == 0 ? "none" : string.Join(", ", html.MediaAndGraphics.Select(static m => $"{m.Tag} ×{m.Count}")));
        md.AppendLine();

        List(md, "Parse diagnostics", html.ParseDiagnostics);
        Tags(md, "Duplicate ids", html.DuplicateIds, "#");
        Tags(md, "Elements HTML does not define", html.UnknownElements, "<");
        Tags(md, "Obsolete elements", html.ObsoleteElements, "<");
        Tags(md, "Custom elements", html.CustomElements, "<");

        if (html.Scripts.Count > 0)
        {
            md.AppendLine("### Script elements, as fetched").AppendLine();
            md.AppendLine("| # | Kind | Source | Attributes | Load |");
            md.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var script in html.Scripts.Take(100))
            {
                md.Append("| ").Append(script.Index)
                    .Append(" | ").Append(Escape(script.Kind))
                    .Append(" | `").Append(Escape(script.Source))
                    .Append("` | ").Append(script.Attributes)
                    .Append(" | ").Append(Escape(script.Load ?? string.Empty)).AppendLine(" |");
            }

            md.AppendLine();
        }

        Resources(md, "Linked stylesheets", html.Stylesheets);
        Resources(md, "Frames and embedded objects", html.Frames);
        Resources(md, "Images", html.Images.Where(static i => i.Load is not { } load || !load.StartsWith("2", StringComparison.Ordinal)).ToArray(), "Images that did not load");
    }

    private static void Css(StringBuilder md, AnalysisReport report)
    {
        if (report.Css is not { } css)
            return;

        md.AppendLine("## CSS").AppendLine();
        md.Append(css.Sheets.Count).Append(" source(s), ")
            .Append(css.ImportantDeclarations).Append(" `!important` declaration(s), ")
            .Append(css.CustomProperties).Append(" custom propert(ies) set, ")
            .Append(css.VendorPrefixedDeclarations).AppendLine(" vendor-prefixed declaration(s).").AppendLine();

        md.AppendLine("| Source | Bytes | Rules | Declarations | Problems | File |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var sheet in css.Sheets)
        {
            md.Append("| `").Append(Escape(sheet.Source))
                .Append("` | ").Append(sheet.Bytes.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(sheet.Rules)
                .Append(" | ").Append(sheet.Declarations)
                .Append(" | ").Append(sheet.Problems)
                .Append(" | ").Append(sheet.SavedAs is { } saved ? $"[{saved}](resources/{Uri.EscapeDataString(saved)})" : "inline")
                .AppendLine(" |");
        }

        md.AppendLine();

        if (css.ParseProblems.Count > 0)
        {
            md.AppendLine("### Parse problems (Broiler.CSS)").AppendLine();
            md.AppendLine("| Source | Line:col | Code | Message | Text |");
            md.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var problem in css.ParseProblems.Take(100))
            {
                md.Append("| `").Append(Escape(problem.Source))
                    .Append("` | ").Append(problem.Line).Append(':').Append(problem.Column)
                    .Append(" | ").Append(problem.Code)
                    .Append(" | ").Append(Escape(problem.Message))
                    .Append(" | `").Append(Escape(problem.Excerpt)).AppendLine("` |");
            }

            md.AppendLine();
        }

        Usages(md, "Dropped by the style engine while it cascaded", css.RejectedDuringCascade,
            "reported by Broiler.CSS as the declarations were applied to elements");
        Usages(md, "Values the style engine does not accept", css.RejectedValues,
            "asked of Broiler.CSS's declaration validator for every declaration in the page's CSS");
        Usages(md, "Property names the CSS engine does not know", css.UnknownProperties,
            "asked of Broiler.CSS's own @supports evaluation; a known name can still be one Broiler's layout does not draw");
        Usages(md, "Vendor-prefixed properties", css.VendorPrefixedProperties, null);

        if (css.AtRules.Count > 0)
        {
            md.AppendLine("### At-rules").AppendLine();
            md.AppendLine(string.Join(", ", css.AtRules.Select(static a => $"`@{a.Key}` ×{a.Value}"))).AppendLine();
            if (css.UnknownAtRules.Count > 0)
                md.Append("Not defined by CSS: ").AppendLine(string.Join(", ", css.UnknownAtRules.Select(static a => $"`@{a}`"))).AppendLine();
        }

        md.AppendLine("<a id=\"fonts\"></a>").AppendLine();
        md.AppendLine("### Fonts").AppendLine();
        if (css.FontFaces.Count > 0)
        {
            md.AppendLine("Declared with `@font-face`: " + string.Join(", ", css.FontFaces.Select(static f => $"`{Escape(f.Family)}`").Distinct()) + ".").AppendLine();
        }

        if (css.Fonts.Count > 0)
        {
            md.AppendLine("| Requested | Text runs | Set in | How | Unavailable ahead of it |");
            md.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var font in css.Fonts.Take(40))
            {
                md.Append("| `").Append(Escape(font.Requested))
                    .Append("` | ").Append(font.TextRuns)
                    .Append(" | ").Append(Escape(font.ResolvedFamily ?? "a fallback"))
                    .Append(" | ").Append(font.Resolution)
                    .Append(" | ").Append(Escape(string.Join(", ", font.Unavailable))).AppendLine(" |");
            }

            md.AppendLine();
        }
    }

    private static void Exceptions(StringBuilder md, AnalysisReport report)
    {
        if (report.Exceptions is not { Total: > 0 } exceptions)
            return;

        md.AppendLine("## Exceptions").AppendLine();
        md.Append(exceptions.Total).Append(" exception(s) raised in the process during the run, first-chance ones included — most were caught ");
        md.AppendLine("by the code that raised them, which is exactly how a fallback hides. By component: " +
            string.Join(", ", exceptions.ByComponent.Take(10).Select(static c => $"{c.Key} ×{c.Value}")) + ".").AppendLine();

        md.AppendLine("| Count | Kind | Type | Thrown in | Phases | First message |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var signature in exceptions.Top.Take(40))
        {
            md.Append("| ").Append(signature.Count)
                .Append(" | ").Append(signature.Kind)
                .Append(" | `").Append(signature.Type)
                .Append("` | `").Append(Escape(signature.Site))
                .Append("` | ").Append(string.Join(", ", signature.Phases))
                .Append(" | ").Append(Escape(AnalysisConsole.OneLine(signature.FirstMessage, 200))).AppendLine(" |");
        }

        md.AppendLine().AppendLine("Every occurrence, with the stack at the throw, is in [exceptions.log](exceptions.log).").AppendLine();
    }

    private static void Phases(StringBuilder md, AnalysisReport report)
    {
        if (report.HotSpots.Count > 0)
        {
            md.AppendLine("## Where slow phases spent their time").AppendLine();
            md.AppendLine("The innermost Broiler method on each sampled stack (`--sample-stacks`); the stacks are in [slow-phase-stacks.txt](slow-phase-stacks.txt).").AppendLine();
            md.AppendLine("| Samples | Frame | Phases |");
            md.AppendLine("| --- | --- | --- |");
            foreach (var hotSpot in report.HotSpots)
                md.Append("| ").Append(hotSpot.Samples).Append(" | `").Append(Escape(hotSpot.Frame)).Append("` | ").Append(string.Join(", ", hotSpot.Phases)).AppendLine(" |");
            md.AppendLine();
        }

        md.AppendLine("## Phases").AppendLine();
        md.AppendLine("| Phase | Started | Took | Outcome | Detail |");
        md.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var phase in report.Phases)
        {
            md.Append("| ").Append(phase.Name)
                .Append(" | ").Append(Ms(phase.StartMs))
                .Append(" | ").Append(Ms(phase.DurationMs))
                .Append(" | ").Append(phase.Outcome)
                .Append(" | ").Append(Escape(phase.Error ?? phase.Detail ?? string.Empty)).AppendLine(" |");
        }

        md.AppendLine();
    }

    private static void Files(StringBuilder md, AnalysisReport report)
    {
        md.AppendLine("## Files").AppendLine();
        foreach (var file in report.Files)
            md.Append("- [").Append(file.Path).Append("](").Append(file.Path).Append(") — ").AppendLine(file.Description);
        md.AppendLine();
    }

    private static void Environment(StringBuilder md, AnalysisReport report)
    {
        var environment = report.Environment;
        md.AppendLine("## Environment").AppendLine();
        md.AppendLine("| | |");
        md.AppendLine("| --- | --- |");
        Row(md, "Runtime", environment.Runtime);
        Row(md, "Operating system", $"{environment.OperatingSystem} ({environment.Architecture})");
        Row(md, "Command line build", environment.CliConfiguration);
        Row(md, "Command line", environment.CommandLine);
        foreach (var (component, version) in environment.Components)
            Row(md, component, version);
        md.AppendLine();
    }

    private static void List(StringBuilder md, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;

        md.Append("### ").AppendLine(title).AppendLine();
        foreach (var item in items.Take(50))
            md.Append("- ").AppendLine(Escape(item));
        md.AppendLine();
    }

    private static void Tags(StringBuilder md, string title, IReadOnlyList<TagCount> tags, string prefix)
    {
        if (tags.Count == 0)
            return;

        md.Append("### ").AppendLine(title).AppendLine();
        md.AppendLine(string.Join(", ", tags.Select(t => $"`{prefix}{Escape(t.Tag)}{(prefix == "<" ? ">" : string.Empty)}` ×{t.Count}"))).AppendLine();
    }

    private static void Resources(StringBuilder md, string title, IReadOnlyList<ResourceElement> resources, string? heading = null)
    {
        if (resources.Count == 0)
            return;

        md.Append("### ").AppendLine(heading ?? title).AppendLine();
        md.AppendLine("| Element | URL | Load |");
        md.AppendLine("| --- | --- | --- |");
        foreach (var resource in resources.Take(60))
        {
            md.Append("| `").Append(Escape(resource.Element))
                .Append("` | `").Append(Escape(resource.Url))
                .Append("` | ").Append(Escape(resource.Load ?? string.Empty)).AppendLine(" |");
        }

        md.AppendLine();
    }

    private static void Usages(StringBuilder md, string title, IReadOnlyList<CssUsage> usages, string? how)
    {
        if (usages.Count == 0)
            return;

        md.Append("### ").AppendLine(title).AppendLine();
        if (how is not null)
            md.Append('_').Append(how).AppendLine("._").AppendLine();

        md.AppendLine("| Declaration | Count | First seen in |");
        md.AppendLine("| --- | --- | --- |");
        foreach (var usage in usages)
        {
            md.Append("| `").Append(Escape(CssInspector.Describe(usage)))
                .Append("` | ").Append(usage.Count)
                .Append(" | ").Append(Escape(usage.FirstSource)).AppendLine(" |");
        }

        md.AppendLine();
    }

    private static string Link(string? path, string text) => path is null ? string.Empty : $"[{text}]({path}) ";

    private static void Row(StringBuilder md, string name, string value) =>
        md.Append("| ").Append(name).Append(" | ").Append(Escape(value)).AppendLine(" |");

    private static string Ms(double ms) => ms.ToString(ms < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " ms";

    private static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KiB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0:0.#} MiB"),
    };

    /// <summary>Keeps page-supplied text from breaking a table cell or a code span.</summary>
    private static string Escape(string value) =>
        value.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Replace("`", "'", StringComparison.Ordinal);
}
