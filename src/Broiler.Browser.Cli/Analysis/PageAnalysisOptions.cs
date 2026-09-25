namespace Broiler.Cli.Analysis;

/// <summary>What <c>--analyze</c> was asked to do.</summary>
internal sealed record PageAnalysisOptions
{
    /// <summary>The page, as an absolute URL or a <c>file:</c> URL.</summary>
    public required string Url { get; init; }

    /// <summary>Where every file of the analysis goes. Created when missing; files in it are overwritten.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Viewport width, for the layout and the screenshots.</summary>
    public int Width { get; init; } = 1024;

    /// <summary>Viewport height.</summary>
    public int Height { get; init; } = 768;

    /// <summary>How long the document may take from the request to its last byte.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Whether to load the first link on the page and analyse that page instead.</summary>
    public bool FollowFirstLink { get; init; }

    /// <summary>Whether to print each event as it happens, not only each phase.</summary>
    public bool Verbose { get; init; }

    /// <summary>The watchdog's default limit, in seconds.</summary>
    public const int DefaultWatchdogSeconds = 300;

    /// <summary>
    /// How long the whole analysis may take before the watchdog writes what there is and ends the
    /// process, or null for no limit.
    /// </summary>
    public TimeSpan? Watchdog { get; init; } = TimeSpan.FromSeconds(DefaultWatchdogSeconds);

    /// <summary>
    /// Whether to sample the process's stacks with <c>dotnet-stack</c> while a phase runs long
    /// (<see cref="StackSampler"/>).
    /// </summary>
    public bool SampleStacks { get; init; }

    /// <summary>The full-page screenshot's height limit.</summary>
    public int MaxFullPageHeight { get; init; } = RenderProbe.DefaultMaxFullPageHeight;
}
