using System.Collections.Concurrent;
using Broiler.Layout.Diagnostics;

namespace Broiler.Cli.Analysis;

/// <summary>
/// Listens to Broiler.Layout while a render runs, for the CSS it does not apply as written: a
/// property it does not model, which it ignores, and a feature it lays out as something simpler.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only while listening.</b> <see cref="LayoutDiagnostics"/> is process-wide, and the layout
/// engine runs for script geometry questions and for the second, script-free render too; listening
/// around the one render the report describes keeps the counts about that render. The handlers that
/// were installed before are chained and put back.
/// </para>
/// <para>
/// <b>Counted by name.</b> The engine reports once per box it styles, so a property on a thousand
/// elements is a thousand reports. They are counted by property (or feature), keeping the first few
/// distinct values as examples, which bounds the memory a large page can take.
/// </para>
/// </remarks>
internal sealed class LayoutGapRecorder
{
    private const int MaxKeys = 500;
    private const int ExamplesPerKey = 3;

    private readonly Tally _notModeled = new();
    private readonly Tally _fallbacks = new();

    /// <summary>Starts listening; disposing the result stops and restores the previous handlers.</summary>
    public IDisposable Listen()
    {
        var previousNotModeled = LayoutDiagnostics.PropertyNotModeled;
        var previousFallback = LayoutDiagnostics.FallbackTaken;
        LayoutDiagnostics.PropertyNotModeled = (property, value) =>
        {
            _notModeled.Add(property, value);
            previousNotModeled?.Invoke(property, value);
        };
        LayoutDiagnostics.FallbackTaken = (feature, detail) =>
        {
            _fallbacks.Add(feature, detail);
            previousFallback?.Invoke(feature, detail);
        };

        return new Restore(() =>
        {
            LayoutDiagnostics.PropertyNotModeled = previousNotModeled;
            LayoutDiagnostics.FallbackTaken = previousFallback;
        });
    }

    /// <summary>The properties the layout engine ignored, most reported first, each with example values.</summary>
    public IReadOnlyList<CssUsage> NotModeled => _notModeled.Snapshot("layout");

    /// <summary>The features the layout engine laid out as something simpler, most reported first.</summary>
    public IReadOnlyList<CssUsage> Fallbacks => _fallbacks.Snapshot("layout");

    private sealed class Tally
    {
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string key, string detail)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                if (_entries.Count >= MaxKeys)
                    return;
                entry = _entries.GetOrAdd(key, static _ => new Entry());
            }

            entry.Add(detail);
        }

        public IReadOnlyList<CssUsage> Snapshot(string source) =>
            [.. _entries
                .Select(pair => (pair.Key, Count: pair.Value.Count, Examples: pair.Value.Examples()))
                .OrderByDescending(static e => e.Count)
                .ThenBy(static e => e.Key, StringComparer.Ordinal)
                .Select(e => new CssUsage(e.Key, e.Examples.Count == 0 ? null : string.Join(" | ", e.Examples), e.Count, source))];
    }

    private sealed class Entry
    {
        private readonly List<string> _examples = [];
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add(string detail)
        {
            Interlocked.Increment(ref _count);
            lock (_examples)
            {
                if (_examples.Count < ExamplesPerKey && !_examples.Contains(detail, StringComparer.Ordinal))
                    _examples.Add(detail);
            }
        }

        public IReadOnlyList<string> Examples()
        {
            lock (_examples)
                return [.. _examples];
        }
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                restore();
        }
    }
}
