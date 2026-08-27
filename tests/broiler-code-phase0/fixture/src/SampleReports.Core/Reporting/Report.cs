using System;
using System.Collections.Generic;

namespace SampleReports.Core.Reporting;

/// <summary>One row of a report.</summary>
public sealed class ReportEntry
{
    public string Label { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public DateTimeOffset Recorded { get; init; }
}

/// <summary>An immutable report ready to be formatted.</summary>
public sealed class Report
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<ReportEntry> Entries { get; init; } = Array.Empty<ReportEntry>();

    public decimal Total
    {
        get
        {
            decimal total = 0m;
            foreach (ReportEntry entry in Entries)
                total += entry.Amount;
            return total;
        }
    }
}
