namespace Broiler.Layout.Engine;

/// <summary>
/// The directory a <em>root-relative</em> sub-document URL — <c>&lt;frame src="/a/b.html"&gt;</c> —
/// resolves against, when the host knows what the document root of a local render is, together
/// with the hosts (if any) that root is the content of — see <see cref="LocalOriginHosts"/>.
/// </summary>
/// <remarks>
/// <para>
/// HTML §"resolve a URL" resolves a leading <c>/</c> against the document's origin, not against the
/// directory the containing page sits in. Served over HTTP that is the server's document root; in a
/// <c>file://</c> render there is no origin to ask, and
/// <c>FragmentTreeBuilder.TryLoadEmbeddedDocument</c> joined the URL onto the containing directory
/// like any other relative reference. <c>Path.Combine</c> discards the left operand when the right
/// one is rooted, so <c>/resource-timing/resources/green.html</c> came out as an absolute path at
/// the filesystem root, failed <c>File.Exists</c>, and the frame painted empty — WPT
/// <c>resource-timing/initiator-type/frameset</c>, whose whole visible content is one such frame.
/// </para>
/// <para>
/// Null by default, so a host that sets nothing renders exactly as before: an unresolvable
/// root-relative frame stays the empty box it has always been. The WPT runner sets it to the
/// checkout it was pointed at, which is the same root its stylesheet, image and script loaders
/// already resolve <c>/</c>-paths against (<c>WptTestRunner.TryResolveWptRootRelativePath</c>) —
/// this closes the one sub-resource kind that had no such hook.
/// </para>
/// <para>
/// Thread-static and scope-restoring, like the engine's other render levers
/// (<see cref="CanvasBackdrop.Current"/>, <see cref="NativeZoom.Enabled"/>): the load happens
/// synchronously on the thread building the fragment tree, so a thread-local value is visible to the
/// code that reads it, and concurrent renders under different roots do not collide.
/// </para>
/// </remarks>
internal static class DocumentRoot
{
    /// <summary>
    /// The document root directory for this thread's render, or <see langword="null"/> to leave
    /// root-relative sub-document URLs unresolved.
    /// </summary>
    [System.ThreadStatic]
    public static string? Current;

    /// <summary>
    /// Hosts whose content <see cref="Current"/> holds — an absolute URL naming one of them is the
    /// same file as the root-relative URL with its origin removed. Null or empty (the default)
    /// leaves every absolute URL unresolved, which is what a renderer with no network does.
    /// </summary>
    /// <remarks>
    /// Hosts are matched <em>exactly</em>, never by suffix: a caller that also serves subdomains
    /// lists them. The WPT runner is the caller this exists for — WPT serves every one of its
    /// hosts from one checkout, so <c>http://not-web-platform.test:8000/css/x.html</c> and
    /// <c>/css/x.html</c> name one file, which is how a test's cross-origin frame is reachable
    /// offline. Exactness is what keeps that honest: WPT also mints deliberately
    /// <em>unresolvable</em> hosts under the same parent domain (<c>nonexistent.web-platform.test</c>,
    /// for tests about load failure), and a suffix rule would serve those content. The list comes
    /// from the host, so the engine holds no knowledge of WPT either way.
    /// </remarks>
    [System.ThreadStatic]
    public static string[]? LocalOriginHosts;

    /// <summary>
    /// Sets <see cref="Current"/> and <see cref="LocalOriginHosts"/> for the lifetime of the
    /// returned scope and restores the previous values when it is disposed, so nesting is safe.
    /// </summary>
    public static System.IDisposable Pin(string? root, string[]? localOriginHosts = null)
    {
        var scope = new Scope(Current, LocalOriginHosts);
        Current = root;
        LocalOriginHosts = localOriginHosts;
        return scope;
    }

    /// <summary>
    /// The root-relative form of <paramref name="url"/> when it is an absolute http(s) URL on one
    /// of this thread's <see cref="LocalOriginHosts"/>, else <see langword="null"/>.
    /// </summary>
    public static string? TryStripLocalOrigin(string? url) => TryStripLocalOrigin(url, LocalOriginHosts);

    /// <summary>
    /// The root-relative form of <paramref name="url"/> — path, query and fragment — when it is an
    /// absolute <c>http</c>/<c>https</c> URL whose host is exactly one of <paramref name="hosts"/>,
    /// else <see langword="null"/>. Pure, so a caller with its own host list (the WPT runner's
    /// resource loaders) shares this one implementation of the rule.
    /// </summary>
    public static string? TryStripLocalOrigin(string? url, System.Collections.Generic.IReadOnlyList<string>? hosts)
    {
        if (string.IsNullOrEmpty(url) || hosts is not { Count: > 0 })
            return null;

        if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
            return null;

        if (!uri.Scheme.Equals("http", System.StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("https", System.StringComparison.OrdinalIgnoreCase))
            return null;

        // Uri lower-cases and IDNA-encodes the host, so the comparison is against the same form the
        // host list is written in (`xn--lve-6lad.web-platform.test`, never `élève.…`).
        string host = uri.Host;
        bool served = false;
        foreach (var candidate in hosts)
        {
            if (!string.IsNullOrEmpty(candidate)
                && host.Equals(candidate, System.StringComparison.OrdinalIgnoreCase))
            {
                served = true;
                break;
            }
        }

        if (!served)
            return null;

        // PathAndQuery keeps the query a WPT URL routinely carries (`?pipe=`, cache-busters); the
        // loaders downstream strip it before touching the disk, exactly as they do for a URL that
        // arrived root-relative in the first place.
        return uri.PathAndQuery + uri.Fragment;
    }

    private sealed class Scope(string? previous, string[]? previousHosts) : System.IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Current = previous;
            LocalOriginHosts = previousHosts;
        }
    }
}
