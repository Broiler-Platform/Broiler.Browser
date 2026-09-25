using System.Diagnostics;
using System.Globalization;

namespace Broiler.Cli.Analysis;

/// <summary>
/// What an analysis prints while it runs: one line per phase always, and with <c>--verbose</c> one
/// line per event as it happens — each request, script failure, console message, render error and
/// exception.
/// </summary>
/// <remarks>
/// <para>
/// <b>Streamed, because the runs worth watching are the ones that stall.</b> A page that hangs in its
/// load window or loops in layout shows its last line on the console, so the reader knows where it is
/// stuck before the watchdog fires — the files say the same, but only once someone opens them.
/// </para>
/// <para>
/// <b>Every line is stamped with the time since the analysis started</b>, so two events can be
/// ordered and a gap between them read off without a clock. Events arrive from prefetch workers and
/// the thread pool as well as the page's own thread, so writes are serialised.
/// </para>
/// </remarks>
internal sealed class AnalysisConsole
{
    private readonly Lock _sync = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TextWriter _out;

    /// <param name="verbose">Whether events are printed as well as phases.</param>
    /// <param name="output">Where to write; standard output when null.</param>
    public AnalysisConsole(bool verbose, TextWriter? output = null)
    {
        Verbose = verbose;
        _out = output ?? Console.Out;
    }

    /// <summary>Whether each event is printed, not only each phase.</summary>
    public bool Verbose { get; }

    /// <summary>A line the reader always sees: a phase starting or ending, or the result.</summary>
    public void Line(string text) => Write(text);

    /// <summary>A line only <c>--verbose</c> prints: one event of the run.</summary>
    public void Event(string category, string text)
    {
        if (!Verbose)
            return;

        Write(string.Create(CultureInfo.InvariantCulture, $"  {category,-9} {OneLine(text)}"));
    }

    private void Write(string text)
    {
        var stamp = _clock.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
        var line = Plain(text);
        lock (_sync)
        {
            try
            {
                _out.WriteLine($"[{stamp,8}s] {line}");
                _out.Flush();
            }
            catch (IOException)
            {
                // A closed console is not a reason to stop recording to disk.
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>
    /// The typographic characters the reports use, spelled in ASCII for the console. A Windows console
    /// on a legacy code page — and every redirect into a file there — prints <c>×</c> and <c>—</c> as
    /// replacement characters; the files are UTF-8 and keep them.
    /// </summary>
    internal static string Plain(string text) => text
        .Replace('×', 'x')
        .Replace("—", "-", StringComparison.Ordinal)
        .Replace("…", "...", StringComparison.Ordinal)
        .Replace("→", "->", StringComparison.Ordinal)
        .Replace("⏎", "|", StringComparison.Ordinal);

    /// <summary>
    /// Keeps a multi-line message — a stack, a pretty-printed value — to one console line, cut at a
    /// length a terminal shows without wrapping into the next event.
    /// </summary>
    internal static string OneLine(string text, int max = 400)
    {
        var flat = text.ReplaceLineEndings(" ⏎ ");
        return flat.Length <= max ? flat : string.Concat(flat.AsSpan(0, max), "…");
    }
}
