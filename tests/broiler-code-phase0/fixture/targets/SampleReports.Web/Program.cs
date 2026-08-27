using System;
using System.Runtime.InteropServices.JavaScript;
using SampleReports.Core.Reporting;

namespace SampleReports.Web;

public static partial class Program
{
    public static void Main()
    {
        // The host module calls Render() once the runtime is up.
    }

    [JSExport]
    internal static string Render()
    {
        var report = new Report
        {
            Title = "Quarterly summary",
            Entries =
            [
                new ReportEntry { Label = "Licences", Amount = 1240.50m, Recorded = DateTimeOffset.UnixEpoch },
                new ReportEntry { Label = "Support", Amount = 318.25m, Recorded = DateTimeOffset.UnixEpoch },
            ],
        };

        return ReportFormatter.Format(report, FormatOptions.Invariant);
    }
}
