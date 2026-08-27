using SampleReports.Core.Reporting;

namespace SampleReports.App;

/// <summary>
/// The corrected call site, compiled instead of
/// <c>ReportRunner.Broken.cs</c> when <c>FixtureCorrected=true</c>.
/// </summary>
internal static class ReportRunner
{
    public static string Render(Report report)
    {
        return ReportFormatter.Format(report, FormatOptions.Invariant);
    }
}
