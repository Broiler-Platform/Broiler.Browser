using System.Diagnostics;
using System.Drawing;
using Broiler.Layout;
using BDom = Broiler.Dom;

namespace Broiler.Cli.Analysis;

/// <summary>One layout a script's geometry question caused.</summary>
/// <param name="AtMs">Milliseconds into the analysis when it started.</param>
/// <param name="Ms">How long the layout took.</param>
/// <param name="Elements">How many elements it measured.</param>
/// <param name="Phase">The analysis phase that was running.</param>
/// <param name="Error">What it threw, when it did.</param>
internal sealed record LayoutQuery(double AtMs, double Ms, int Elements, string Phase, string? Error);

/// <summary>
/// Times every layout the page's scripts cause by asking a geometry question —
/// <c>getBoundingClientRect</c>, <c>offsetWidth</c>, <c>scrollHeight</c> and the rest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is worth a recorder of its own.</b> The script bridge answers those questions from a
/// real layout of the whole document (<c>HeadlessLayoutView</c>), made on the thread the asking script
/// runs on. On a page whose layout is slow, every such question stalls the script for the length of a
/// full layout, and from the outside that looks like the engine being slow or a timer hanging. The
/// time is not the script's, and this is the only place it can be told apart.
/// </para>
/// <para>
/// It wraps the bridge's layout view and changes nothing about it: the same view answers, and a
/// layout that throws still throws to the bridge, which degrades as it always does.
/// </para>
/// </remarks>
internal sealed class LayoutQueryRecorder(Func<double> clock, Func<string> phase)
{
    private readonly Lock _sync = new();
    private readonly List<LayoutQuery> _queries = [];

    /// <summary>Raised after each layout, outside the recorder's lock.</summary>
    public event Action<LayoutQuery>? Completed;

    private double Now => clock();

    private string Phase => phase();

    /// <summary>Wraps <paramref name="inner"/> so each geometry request through it is timed.</summary>
    public ILayoutView Wrap(ILayoutView inner) => new TimedLayoutView(inner, this);

    /// <summary>The layouts so far, in the order they started.</summary>
    public IReadOnlyList<LayoutQuery> Snapshot()
    {
        lock (_sync)
            return [.. _queries];
    }

    private void Record(LayoutQuery query)
    {
        lock (_sync)
            _queries.Add(query);

        Completed?.Invoke(query);
    }

    private sealed class TimedLayoutView(ILayoutView inner, LayoutQueryRecorder recorder) : ILayoutView
    {
        public IReadOnlyDictionary<BDom.DomElement, BoxGeometry> GetGeometry(
            BDom.DomDocument document,
            SizeF viewport,
            string baseUrl,
            Func<BDom.DomElement, BDom.DomDocument?>? contentDocumentResolver = null)
        {
            var at = recorder.Now;
            var started = Stopwatch.GetTimestamp();
            try
            {
                var geometry = inner.GetGeometry(document, viewport, baseUrl, contentDocumentResolver);
                recorder.Record(new LayoutQuery(Math.Round(at, 1), Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1), geometry.Count, recorder.Phase, Error: null));
                return geometry;
            }
            catch (Exception ex)
            {
                recorder.Record(new LayoutQuery(Math.Round(at, 1), Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1), 0, recorder.Phase, $"{ex.GetType().FullName}: {ExceptionText.SafeMessage(ex)}"));
                throw;
            }
        }

        public void Dispose() => inner.Dispose();
    }
}
