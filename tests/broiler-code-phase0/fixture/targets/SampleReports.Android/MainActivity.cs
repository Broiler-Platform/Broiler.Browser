using System;
using Android.App;
using Android.OS;
using Android.Widget;
using SampleReports.Core.Reporting;

namespace SampleReports.Android;

[Activity(Label = "@string/app_name", MainLauncher = true)]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var report = new Report
        {
            Title = "Quarterly summary",
            Entries =
            [
                new ReportEntry { Label = "Licences", Amount = 1240.50m, Recorded = DateTimeOffset.UnixEpoch },
                new ReportEntry { Label = "Support", Amount = 318.25m, Recorded = DateTimeOffset.UnixEpoch },
            ],
        };

        var view = new TextView(this) { Text = ReportFormatter.Format(report, FormatOptions.Invariant) };
        SetContentView(view);
    }
}
