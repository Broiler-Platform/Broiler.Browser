using System.Globalization;

namespace SampleReports.Core.Reporting;

/// <summary>Controls how <see cref="ReportFormatter"/> renders a report.</summary>
public sealed class FormatOptions
{
    public static FormatOptions Invariant { get; } = new();

    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    public bool IncludeTotal { get; init; } = true;

    /// <summary>Widest label column the formatter will pad to.</summary>
    public int LabelWidth { get; init; } = 24;
}
