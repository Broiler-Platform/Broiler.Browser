namespace Broiler.Layout.Engine;

/// <summary>
/// The host's answer to "may this sub-resource URL be fetched over the network at all?", consulted
/// by the loaders before they start a request. Unset by default, so a renderer with a network
/// behaves exactly as it always has.
/// </summary>
/// <remarks>
/// <para>
/// A sub-resource fetch on the render path is synchronous — <c>HtmlRender</c> sets
/// <c>AvoidAsyncImagesLoading</c>, so the loader blocks the layout thread — and a timeout bounds an
/// unreachable host without making it free. A host that already knows a URL cannot resolve to
/// anything pays that bound per URL to be told what it knew: the conformance runner's whole corpus
/// is a directory on disk, and a test naming three unroutable hosts spends fifteen seconds of a
/// thirty-second budget waiting for three failures. Answering here costs none of it, and ends where
/// the fetch was going to end anyway — with the resource not loaded, which is what those tests are
/// checking the engine does with such a URL.
/// </para>
/// <para>
/// It is a <em>predicate over the URL</em> rather than a flag because the distinction the host draws
/// is not "network or no network": the WPT runner serves a corpus host from a checkout and declines
/// only what is off-corpus, and only the host can tell those apart. The engine holds no knowledge of
/// any of it.
/// </para>
/// <para>
/// A plain static, not <c>[ThreadStatic]</c>, unlike the engine's render levers
/// (<see cref="NativeZoom.Enabled"/>, <see cref="DocumentRoot.Current"/>): those scope a render, this
/// scopes a <em>process</em> — a host either has a network to reach or it does not. It has to be
/// process-wide to work at all, because the image prefetch consults it from
/// <c>Parallel.For</c> workers, where a thread-local set on the layout thread would be invisible.
/// </para>
/// </remarks>
internal static class OfflineSubresources
{
    /// <summary>
    /// Returns <see langword="false"/> for a URL this process must not fetch, or
    /// <see langword="null"/> (the default) to allow every one.
    /// </summary>
    public static System.Func<string?, bool>? FetchPolicy;

    /// <summary>
    /// True when <paramref name="url"/> must not be fetched. False whenever no policy is installed,
    /// which is every host but the conformance runner.
    /// </summary>
    public static bool Denies(string? url) => FetchPolicy is { } policy && !policy(url);
}
