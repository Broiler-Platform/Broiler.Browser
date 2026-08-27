using SampleReports.Core.Reporting;

namespace SampleReports.App;

/// <summary>
/// Always compiled. Calls an <c>[Obsolete]</c> member declared in
/// <c>SampleReports.Core</c> so both the broken and the corrected fixture emit
/// exactly one warning-severity diagnostic whose declaration site is in a
/// different project. The Problems pane has to keep this distinct from the
/// error, not merge the two into one "build failed" row.
/// </summary>
internal static class LegacySummary
{
    public static string Render(Report report)
    {
        return ReportFormatter.FormatLegacy(report);
    }
}
