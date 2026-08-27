using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace SampleReports.Core.Reporting;

/// <summary>
/// Renders a <see cref="Report"/> as text. The two-parameter <see cref="Format"/>
/// overload is the one <c>SampleReports.App</c> calls incorrectly on purpose;
/// see <c>tests/broiler-code-phase0/fixture/expected-diagnostics.json</c>.
/// </summary>
public static class ReportFormatter
{
    private const string TemplateResourceName =
        "SampleReports.Core.Resources.report-template.txt";

    public static string Format(Report report, FormatOptions options)
    {
        if (report is null)
            throw new ArgumentNullException(nameof(report));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var builder = new StringBuilder();
        builder.AppendLine(LoadTemplate()
            .Replace("{title}", report.Title)
            .Replace("{stamp}", BuildStamp.Describe()));

        foreach (ReportEntry entry in report.Entries)
        {
            builder.Append(entry.Label.PadRight(options.LabelWidth));
            builder.AppendLine(entry.Amount.ToString("N2", options.Culture));
        }

        if (options.IncludeTotal)
        {
            builder.Append("TOTAL".PadRight(options.LabelWidth));
            builder.AppendLine(report.Total.ToString("N2", options.Culture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Retained so the fixture emits exactly one warning-severity diagnostic
    /// from a call site in another project.
    /// </summary>
    [Obsolete("Call Format(Report, FormatOptions) instead.")]
    public static string FormatLegacy(Report report) => Format(report, FormatOptions.Invariant);

    private static string LoadTemplate()
    {
        Assembly assembly = typeof(ReportFormatter).GetTypeInfo().Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(TemplateResourceName);
        if (stream is null)
            throw new InvalidOperationException($"Missing embedded resource '{TemplateResourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
