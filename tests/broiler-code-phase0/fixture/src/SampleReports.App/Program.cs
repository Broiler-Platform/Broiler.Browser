using System;
using SampleReports.Core.Reporting;

namespace SampleReports.App;

internal static class Program
{
    public static int Main()
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

        Console.Write(ReportRunner.Render(report));
        Console.Write(LegacySummary.Render(report));
        return 0;
    }
}
