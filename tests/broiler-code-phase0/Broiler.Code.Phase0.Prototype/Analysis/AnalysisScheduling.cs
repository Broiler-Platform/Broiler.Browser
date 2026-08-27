namespace Broiler.Code.Phase0.Prototype.Analysis;

/// <summary>
/// Marshals a completion back to the thread that owns the view. Phase 1
/// requires a real host dispatcher here: compiler callbacks arrive on worker
/// threads, and Writer's immediate cross-thread dispatcher would run them
/// wherever they happened to complete.
/// </summary>
public interface IAnalysisDispatcher
{
    void Post(Action action);
}

/// <summary>Runs analysis work off the UI thread.</summary>
public interface IAnalysisScheduler
{
    Task<T> Schedule<T>(Func<T> work, CancellationToken cancellationToken);
}

public sealed class ThreadPoolAnalysisScheduler : IAnalysisScheduler
{
    public Task<T> Schedule<T>(Func<T> work, CancellationToken cancellationToken) =>
        Task.Run(work, cancellationToken);
}

/// <summary>
/// Drains posted work on demand instead of on a message loop, so a test can
/// state exactly when a completion is allowed to reach the view.
/// </summary>
public sealed class DeferredDispatcher : IAnalysisDispatcher
{
    private readonly Queue<Action> _pending = new();

    public int PendingCount
    {
        get
        {
            lock (_pending)
                return _pending.Count;
        }
    }

    public void Post(Action action)
    {
        lock (_pending)
            _pending.Enqueue(action);
    }

    /// <summary>Runs everything queued so far. Returns how many ran.</summary>
    public int Drain()
    {
        int count = 0;
        while (true)
        {
            Action next;
            lock (_pending)
            {
                if (_pending.Count == 0)
                    return count;
                next = _pending.Dequeue();
            }

            next();
            count++;
        }
    }
}

/// <summary>Decides whether a document is big enough to classify off-thread.</summary>
public static class ClassificationPolicy
{
    /// <summary>
    /// Below this, a full classification is cheaper than the scheduling round
    /// trip. Frozen in docs/architecture/broiler-code-budgets.md.
    /// </summary>
    public const int SynchronousLineLimit = 2_000;

    public static bool RecommendBackgroundClassification(int lineCount) =>
        lineCount > SynchronousLineLimit;
}
