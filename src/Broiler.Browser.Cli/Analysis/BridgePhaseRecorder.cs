using Broiler.HtmlBridge.Core.Diagnostics;

namespace Broiler.Cli.Analysis;

/// <summary>Measures DOM construction and binding work, which is outside JavaScript turn timings.</summary>
internal sealed class BridgePhaseRecorder : IDisposable
{
    private readonly bool _previous = BridgePhaseTrace.Enabled;
    private readonly IReadOnlyDictionary<string, (double Ms, int Count)> _before = BridgePhaseTrace.Totals();

    public BridgePhaseRecorder() => BridgePhaseTrace.Enabled = true;

    public object Snapshot() => BridgePhaseTrace.Totals()
        .Select(pair =>
        {
            _before.TryGetValue(pair.Key, out var earlier);
            return new { Phase = pair.Key.Trim(), Ms = Math.Round(pair.Value.Ms - earlier.Ms, 2), Count = pair.Value.Count - earlier.Count };
        })
        .Where(static phase => phase.Count > 0)
        .ToArray();

    public void Dispose() => BridgePhaseTrace.Enabled = _previous;
}
