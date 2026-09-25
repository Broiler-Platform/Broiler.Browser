using System.Globalization;
using System.Net;
using System.Text;

namespace Broiler.Cli.Analysis;

/// <summary>Writes an <see cref="AnalysisReport"/> as <c>report.html</c>: one page with the screenshots and the evidence.</summary>
/// <remarks>
/// <para>
/// <b>Self-contained and static.</b> No script and no external resource: the page opens from disk in
/// any browser, and nothing in it runs. The screenshots are linked by relative path, so the directory
/// can be moved or zipped whole.
/// </para>
/// <para>
/// <b>Everything the page supplied is encoded.</b> URLs, messages, selectors and text excerpts come from
/// the analysed page, which may be hostile; each one goes through <see cref="WebUtility.HtmlEncode"/>
/// before it reaches the report, and links are only ever to the analysis's own files.
/// </para>
/// </remarks>
internal static class HtmlReportPage
{
    public static string Write(AnalysisReport report)
    {
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<title>Broiler analysis — ").Append(E(report.Document?.FinalUrl ?? report.Url)).AppendLine("</title>");
        html.AppendLine(Style);
        html.AppendLine("</head><body><main>");

        html.AppendLine("<h1>Broiler page analysis</h1>");
        html.Append("<p class=\"url\">").Append(E(report.Url)).AppendLine("</p>");
        html.Append("<p class=\"meta\">")
            .Append(E(report.StartedAt.ToString("u", CultureInfo.InvariantCulture))).Append(" · ")
            .Append(E(Ms(report.DurationMs))).Append(" · viewport ").Append(E(report.Viewport)).Append(" · ")
            .Append(E($"{report.Environment.Engine} {report.Environment.EngineVersion}"));
        if (report.Document is { } document)
            html.Append(" · HTTP ").Append(E(document.Status?.ToString(CultureInfo.InvariantCulture) ?? "—")).Append(' ').Append(E(document.ContentType ?? string.Empty));
        if (!report.Completed)
            html.Append(" · <strong class=\"error\">incomplete</strong>");
        html.AppendLine("</p>");

        Screenshots(html, report);
        Findings(html, report);
        Sections(html, report);

        html.AppendLine("<h2>Files</h2><ul class=\"files\">");
        foreach (var file in report.Files)
            html.Append("<li><a href=\"").Append(E(file.Path)).Append("\">").Append(E(file.Path)).Append("</a> — ").Append(E(file.Description)).AppendLine("</li>");
        html.AppendLine("</ul>");

        html.AppendLine("</main></body></html>");
        return html.ToString();
    }

    private static void Screenshots(StringBuilder html, AnalysisReport report)
    {
        var shots = new List<(string Path, string Caption)>();
        if (report.Render?.ViewportImage is { } viewport)
            shots.Add((viewport, "After scripts"));
        if (report.RenderWithoutScripts?.ViewportImage is { } bare)
            shots.Add((bare, "Without scripts"));
        if (report.Render?.BoxesImage is { } boxes)
            shots.Add((boxes, "Layout boxes"));
        if (shots.Count == 0)
            return;

        html.AppendLine("<div class=\"shots\">");
        foreach (var (path, caption) in shots)
        {
            html.Append("<figure><a href=\"").Append(E(path)).Append("\"><img src=\"").Append(E(path))
                .Append("\" alt=\"").Append(E(caption)).Append("\"></a><figcaption>").Append(E(caption)).AppendLine("</figcaption></figure>");
        }

        html.AppendLine("</div>");
        if (report.Render?.FullPageImage is { } full)
            html.Append("<p class=\"meta\"><a href=\"").Append(E(full)).AppendLine("\">Full-page screenshot</a></p>");
        if (report.ScriptVisualEffect is { } effect)
            html.Append("<p class=\"meta\">The scripts changed ").Append(E(effect.ToString("P2", CultureInfo.InvariantCulture))).AppendLine(" of the viewport's pixels.</p>");
    }

    private static void Findings(StringBuilder html, AnalysisReport report)
    {
        html.AppendLine("<h2>Findings</h2>");
        if (report.Findings.Count == 0)
        {
            html.AppendLine("<p>Nothing stood out. The files below hold everything the run produced.</p>");
            return;
        }

        html.AppendLine("<table><thead><tr><th>Severity</th><th>Area</th><th>Finding</th><th>Evidence</th><th>See</th></tr></thead><tbody>");
        foreach (var finding in report.Findings)
        {
            var severity = finding.Severity.ToString().ToLowerInvariant();
            html.Append("<tr><td><span class=\"badge ").Append(severity).Append("\">").Append(severity).Append("</span></td><td>")
                .Append(E(finding.Area)).Append("</td><td>").Append(E(finding.Title)).Append("</td><td>").Append(E(finding.Detail)).Append("</td><td>");
            if (finding.SeeAlso is { } see)
                html.Append("<a href=\"").Append(E(see)).Append("\">").Append(E(see)).Append("</a>");
            html.AppendLine("</td></tr>");
        }

        html.AppendLine("</tbody></table>");
    }

    private static void Sections(StringBuilder html, AnalysisReport report)
    {
        if (report.Scripting is { } js)
        {
            Open(html, "JavaScript", js.Failures > 0);
            Table(html, ["Count", "Where", "Failure"], js.DistinctFailures.Take(40).Select(static g =>
                new[] { g.Count.ToString(CultureInfo.InvariantCulture), g.FirstContext ?? string.Empty, g.Example }));
            Table(html, ["Missing", "Kind", "Failures"], js.MissingApis.Take(40).Select(static m =>
                new[] { m.Name, m.Kind, m.Count.ToString(CultureInfo.InvariantCulture) }));
            Table(html, ["Script", "ms", "Completed"], js.Timings.OrderByDescending(static t => t.Ms).Take(30).Select(static t =>
                new[] { t.Label, Ms(t.Ms), t.Succeeded ? "yes" : "threw" }));
            html.Append("<p>").Append(E($"Console: {(js.Console.Count == 0 ? "silent" : string.Join(", ", js.Console.Select(static c => $"{c.Key} ×{c.Value}")))}. " +
                $"Exceptions thrown inside JavaScript, caught ones included: {js.JavaScriptExceptions}. " +
                $"Navigation not followed: {js.NavigationNotFollowed ?? "none"}. " +
                $"Layouts caused by geometry questions: {js.GeometryLayouts} ({js.GeometryLayoutMs:0} ms).")).AppendLine("</p>");
            Table(html, ["Long turns and idle gaps (turn trace)"], js.LongTurns.Select(static t => new[] { t }));
            Close(html);
        }

        if (report.Network is { } network)
        {
            Open(html, $"Network — {network.Requests} requests, {network.Failed} failed", network.Failed > 0);
            Table(html, ["Destination", "Status", "URL", "Error"], network.Failures.Select(static f =>
                new[] { f.Destination, f.Status?.ToString(CultureInfo.InvariantCulture) ?? "—", f.Url, f.Error ?? f.BodyError ?? f.StatusText ?? string.Empty }));
            Table(html, ["To headers", "To last byte", "Destination", "URL"], network.Slowest.Select(static s =>
                new[] { Ms(s.HeadersMs), s.CompleteMs is { } c ? Ms(c) : "—", s.Destination, s.Url }));
            Close(html);
        }

        if (report.Render is { } render)
        {
            Open(html, "Render", render.Errors.Count > 0);
            html.Append("<p>").Append(E($"Content {render.ContentSize}, canvas {render.CanvasBackground}, {render.DisplayListItems} display-list item(s), " +
                $"{render.EmbeddedDocuments} embedded document(s). Timings: {string.Join(", ", render.Timings.Select(static t => $"{t.Key} {Ms(t.Value)}"))}.")).AppendLine("</p>");
            Table(html, ["Kind", "At", "Detail"], render.Errors.Take(40).Select(static e => new[] { e.Kind, Ms(e.AtMs), e.Detail }));
            Close(html);
        }

        if (report.Layout is { } layout)
        {
            Open(html, "Layout", layout.InvariantViolations.Count + layout.HorizontalOverflow.Count + layout.CollapsedWithText.Count + layout.TallerThanContent.Count > 0);
            html.Append("<p>").Append(E($"{layout.Boxes} box(es), {layout.TextRuns} text run(s), depth {layout.MaxDepth}, content {layout.ContentSize}.")).AppendLine("</p>");
            Table(html, ["Invariant violation"], layout.InvariantViolations.Take(25).Select(static v => new[] { v }));
            LayoutTable(html, "Far taller than their content", layout.TallerThanContent);
            LayoutTable(html, "Reaching past the right edge", layout.HorizontalOverflow);
            LayoutTable(html, "Holding text, laid out with no size", layout.CollapsedWithText);
            LayoutTable(html, "Placed off the page", layout.OffPage);
            Close(html);
        }

        if (report.Html is { } markup)
        {
            Open(html, "HTML", markup.QuirksMode);
            html.Append("<p>").Append(E($"Doctype: {markup.Doctype ?? "none"} ({(markup.QuirksMode ? "quirks mode" : "standards mode")}). " +
                $"Elements: {markup.ElementsAsFetched:N0} as fetched, {markup.ElementsAfterScripts:N0} after scripts. " +
                $"Charset: {markup.DeclaredCharset ?? "not declared"}. Title: {markup.Title ?? "none"}.")).AppendLine("</p>");
            Table(html, ["Parse diagnostic"], markup.ParseDiagnostics.Take(50).Select(static d => new[] { d }));
            Table(html, ["Duplicate id", "Elements"], markup.DuplicateIds.Select(static d => new[] { d.Tag, d.Count.ToString(CultureInfo.InvariantCulture) }));
            Table(html, ["Unknown element", "Count"], markup.UnknownElements.Select(static d => new[] { d.Tag, d.Count.ToString(CultureInfo.InvariantCulture) }));
            Table(html, ["#", "Script", "Source", "Load"], markup.Scripts.Take(100).Select(static s =>
                new[] { s.Index.ToString(CultureInfo.InvariantCulture), s.Kind + (s.Attributes.Length > 0 ? " " + s.Attributes : string.Empty), s.Source, s.Load ?? string.Empty }));
            Close(html);
        }

        if (report.Css is { } css)
        {
            Open(html, "CSS", css.ParseProblems.Count + css.RejectedDuringCascade.Count > 0);
            Table(html, ["Source", "Line:col", "Code", "Message", "Text"], css.ParseProblems.Take(100).Select(static p =>
                new[] { p.Source, $"{p.Line}:{p.Column}", p.Code, p.Message, p.Excerpt }));
            Table(html, ["Dropped while cascading", "Count"], css.RejectedDuringCascade.Select(static u => new[] { CssInspector.Describe(u), u.Count.ToString(CultureInfo.InvariantCulture) }));
            Table(html, ["Value not accepted", "Count", "First in"], css.RejectedValues.Select(static u => new[] { CssInspector.Describe(u), u.Count.ToString(CultureInfo.InvariantCulture), u.FirstSource }));
            Table(html, ["Unknown property", "Count", "First in"], css.UnknownProperties.Select(static u => new[] { u.Property, u.Count.ToString(CultureInfo.InvariantCulture), u.FirstSource }));
            Table(html, ["Font list", "Runs", "Set in", "Unavailable"], css.Fonts.Take(40).Select(static f =>
                new[] { f.Requested, f.TextRuns.ToString(CultureInfo.InvariantCulture), f.ResolvedFamily ?? "a fallback", string.Join(", ", f.Unavailable) }));
            Close(html);
        }

        if (report.Exceptions is { Total: > 0 } exceptions)
        {
            Open(html, $"Exceptions — {exceptions.Total}, first-chance included", false);
            Table(html, ["Count", "Kind", "Type", "Thrown in", "Message"], exceptions.Top.Take(40).Select(static s =>
                new[] { s.Count.ToString(CultureInfo.InvariantCulture), s.Kind, s.Type, s.Site, AnalysisConsole.OneLine(s.FirstMessage, 200) }));
            Close(html);
        }

        if (report.HotSpots.Count > 0)
        {
            Open(html, "Where slow phases spent their time", true);
            Table(html, ["Samples", "Innermost Broiler frame", "Phases"], report.HotSpots.Select(static h =>
                new[] { h.Samples.ToString(CultureInfo.InvariantCulture), h.Frame, string.Join(", ", h.Phases) }));
            Close(html);
        }

        Open(html, "Phases", report.Phases.Any(static p => p.Outcome == PhaseOutcome.Failed || p.DurationMs >= 10_000));
        Table(html, ["Phase", "Took", "Outcome", "Detail"], report.Phases.Select(static p =>
            new[] { p.Name, Ms(p.DurationMs), p.Outcome.ToString(), p.Error ?? p.Detail ?? string.Empty }));
        Close(html);

        Open(html, "Environment", false);
        var environment = report.Environment;
        Table(html, ["", ""], new[]
        {
            new[] { "Runtime", environment.Runtime },
            new[] { "Operating system", $"{environment.OperatingSystem} ({environment.Architecture})" },
            new[] { "Command line build", environment.CliConfiguration },
            new[] { "Command line", environment.CommandLine },
        }.Concat(environment.Components.Select(static c => new[] { c.Key, c.Value })));
        Close(html);
    }

    private static void LayoutTable(StringBuilder html, string title, IReadOnlyList<LayoutFinding> findings)
    {
        if (findings.Count == 0)
            return;

        html.Append("<h3>").Append(E(title)).AppendLine("</h3>");
        Table(html, ["Element", "Inside", "Border box", "Detail"], findings.Select(static f => new[] { f.Element, f.Path, f.Box, f.Detail }));
    }

    private static void Open(StringBuilder html, string title, bool open) =>
        html.Append("<details").Append(open ? " open" : string.Empty).Append("><summary>").Append(E(title)).AppendLine("</summary>");

    private static void Close(StringBuilder html) => html.AppendLine("</details>");

    private static void Table(StringBuilder html, string[] headers, IEnumerable<string[]> rows)
    {
        var materialised = rows.ToArray();
        if (materialised.Length == 0)
            return;

        html.Append("<table><thead><tr>");
        foreach (var header in headers)
            html.Append("<th>").Append(E(header)).Append("</th>");
        html.AppendLine("</tr></thead><tbody>");
        foreach (var row in materialised)
        {
            html.Append("<tr>");
            foreach (var cell in row)
                html.Append("<td>").Append(E(cell)).Append("</td>");
            html.AppendLine("</tr>");
        }

        html.AppendLine("</tbody></table>");
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static string Ms(double ms) => ms.ToString(ms < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " ms";

    private const string Style = """
        <style>
        :root { color-scheme: light dark; --fg: #1d1d1f; --bg: #ffffff; --muted: #6e6e73; --line: #d2d2d7;
                --panel: #f5f5f7; --error: #c62828; --warning: #b26a00; --info: #1565c0; }
        @media (prefers-color-scheme: dark) {
          :root { --fg: #f5f5f7; --bg: #161618; --muted: #a1a1a6; --line: #3a3a3c; --panel: #1f1f22;
                  --error: #ef5350; --warning: #ffb74d; --info: #64b5f6; }
        }
        body { margin: 0; background: var(--bg); color: var(--fg); font: 14px/1.45 system-ui, sans-serif; }
        main { max-width: 1280px; margin: 0 auto; padding: 16px; }
        h1 { font-size: 22px; margin: 8px 0 4px; }
        h2 { font-size: 18px; margin: 24px 0 8px; }
        h3 { font-size: 15px; margin: 16px 0 6px; }
        .url { font-family: ui-monospace, monospace; word-break: break-all; margin: 0; }
        .meta { color: var(--muted); margin: 4px 0; }
        .shots { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 12px; margin: 16px 0 8px; }
        figure { margin: 0; }
        figure img { width: 100%; height: auto; border: 1px solid var(--line); display: block; background: #fff; }
        figcaption { color: var(--muted); font-size: 12px; margin-top: 4px; }
        table { border-collapse: collapse; width: 100%; margin: 8px 0 12px; font-size: 13px; }
        th, td { border: 1px solid var(--line); padding: 4px 6px; text-align: left; vertical-align: top; overflow-wrap: anywhere; }
        th { background: var(--panel); }
        details { border: 1px solid var(--line); border-radius: 6px; padding: 6px 10px; margin: 10px 0; }
        summary { cursor: pointer; font-weight: 600; }
        .badge { font-size: 11px; font-weight: 700; text-transform: uppercase; padding: 1px 6px; border-radius: 4px; color: #fff; }
        .badge.error { background: var(--error); } .badge.warning { background: var(--warning); } .badge.info { background: var(--info); }
        .error { color: var(--error); }
        .files li { margin: 2px 0; }
        a { color: var(--info); }
        </style>
        """;
}
