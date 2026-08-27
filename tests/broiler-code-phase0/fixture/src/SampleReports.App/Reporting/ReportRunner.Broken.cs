using SampleReports.Core.Reporting;

namespace SampleReports.App;

/// <summary>
/// The intentional cross-project, cross-file error. <c>ReportFormatter.Format</c>
/// is declared in
/// <c>src/SampleReports.Core/Reporting/ReportFormatter.cs</c> and takes a
/// <see cref="FormatOptions"/> argument that this call site does not pass, so
/// the compiler reports CS7036 here while the symbol it names lives in another
/// project. Nothing in this file is wrong when read on its own — which is the
/// point: a syntax-only classifier cannot find it.
/// </summary>
internal static class ReportRunner
{
    public static string Render(Report report)
    {
        return ReportFormatter.Format(report);
    }
}
