using Broiler.Code.Phase0.Prototype.Classification;
using Broiler.Code.Phase0.Prototype.Text;

namespace Broiler.Code.Phase0.Prototype.Analysis;

/// <summary>
/// Keeps a classification result in step with a <see cref="SourceBuffer"/>.
///
/// It follows the generation/snapshot pattern already proven by
/// <c>WriterFormatCodesController</c>: every run takes a generation number and
/// the snapshot it was started against, and a completion may only be published
/// while both still match. Two independent conditions have to hold, because
/// either one alone is insufficient — a generation match does not prove the
/// buffer did not move on, and a snapshot match does not prove a newer run has
/// not already been started against the same snapshot.
/// </summary>
public sealed class ClassificationController : IDisposable
{
    private readonly SourceBuffer _buffer;
    private readonly PortableCSharpClassifier _classifier = new();
    private readonly IAnalysisDispatcher _dispatcher;
    private readonly IAnalysisScheduler _scheduler;
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private bool _disposed;

    public ClassificationController(
        SourceBuffer buffer,
        IAnalysisDispatcher dispatcher,
        IAnalysisScheduler? scheduler = null)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _scheduler = scheduler ?? new ThreadPoolAnalysisScheduler();
        _buffer.Changed += BufferChanged;
        Start(_buffer.Current, change: null);
    }

    public event Action<ClassificationResult>? ResultPublished;

    /// <summary>The newest published result, or null before the first one lands.</summary>
    public ClassificationResult? Result { get; private set; }

    public long Generation => _generation;

    /// <summary>
    /// How many completed runs were discarded because a newer snapshot or a
    /// newer generation had taken over. A non-zero value under a fast typing
    /// trace is the evidence that stale work cannot paint.
    /// </summary>
    public int RejectedResults { get; private set; }

    public int CancelledRuns { get; private set; }

    public bool IsPending { get; private set; }

    public Task? CurrentWork { get; private set; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _buffer.Changed -= BufferChanged;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private void BufferChanged(TextSnapshot snapshot, TextChange change) => Start(snapshot, change);

    private void Start(TextSnapshot snapshot, TextChange? change)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        long generation = ++_generation;
        ClassificationResult? previous = Result;
        CurrentWork = null;

        if (!ClassificationPolicy.RecommendBackgroundClassification(snapshot.LineCount))
        {
            try
            {
                Publish(generation, Run(previous, snapshot, change, cancellation.Token));
            }
            catch (OperationCanceledException)
            {
                CancelledRuns++;
            }

            return;
        }

        IsPending = true;
        CurrentWork = RunInBackground(generation, previous, snapshot, change, cancellation.Token);
    }

    private async Task RunInBackground(
        long generation,
        ClassificationResult? previous,
        TextSnapshot snapshot,
        TextChange? change,
        CancellationToken cancellationToken)
    {
        try
        {
            ClassificationResult result = await _scheduler
                .Schedule(() => Run(previous, snapshot, change, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            _dispatcher.Post(() => Publish(generation, result));
        }
        catch (OperationCanceledException)
        {
            // A superseded run disappears without publishing. It is counted on
            // the dispatcher so the counter stays single-threaded.
            _dispatcher.Post(() => CancelledRuns++);
        }
    }

    private ClassificationResult Run(
        ClassificationResult? previous,
        TextSnapshot snapshot,
        TextChange? change,
        CancellationToken cancellationToken)
    {
        return previous is not null && change is { } actual
            ? _classifier.ClassifyIncremental(previous, snapshot, actual, cancellationToken)
            : _classifier.ClassifyAll(snapshot, cancellationToken);
    }

    private void Publish(long generation, ClassificationResult result)
    {
        if (_disposed)
            return;

        if (generation != _generation || !ReferenceEquals(result.Snapshot, _buffer.Current))
        {
            RejectedResults++;
            return;
        }

        Result = result;
        IsPending = false;
        ResultPublished?.Invoke(result);
    }
}
