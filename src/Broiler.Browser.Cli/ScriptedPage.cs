using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Core.Diagnostics;
using Broiler.HtmlBridge.Dom;
using Broiler.HtmlBridge.Logging;
using Broiler.HtmlBridge.Scripting;
using Broiler.JSeal;

namespace Broiler.Cli;

/// <summary>
/// A page whose scripts have run and whose load window has settled: what a capture renders, and
/// what an evaluation runs against.
/// </summary>
internal sealed class ScriptedPage : IDisposable
{
    private readonly InteractiveSession? _session;
    private readonly IDomBridgeRuntime? _bridge;
    private readonly MicroTaskQueue _microTasks;
    private readonly string _url;
    private readonly string _fetchedHtml;
    private readonly int _firstEvaluationLabel;

    /// <param name="session">The page's session, or null for a page that ran no scripts.</param>
    /// <param name="bridge">The bridge the session's scripts ran against.</param>
    /// <param name="microTasks">The engine's microtask queue, which the session drains.</param>
    /// <param name="url">The document's URL.</param>
    /// <param name="fetchedHtml">The document as fetched, which is also what a page with no scripts renders.</param>
    /// <param name="firstEvaluationLabel">
    /// The inline-script index the first evaluation is labelled with — one past the page's own, so an
    /// evaluation's failure is never attributed to one of the page's scripts.
    /// </param>
    internal ScriptedPage(
        InteractiveSession? session,
        IDomBridgeRuntime? bridge,
        MicroTaskQueue microTasks,
        string url,
        string fetchedHtml,
        int firstEvaluationLabel)
    {
        _session = session;
        _bridge = bridge;
        _microTasks = microTasks;
        _url = url;
        _fetchedHtml = fetchedHtml;
        _firstEvaluationLabel = firstEvaluationLabel;
    }

    /// <summary>
    /// Whether the last settle ran out of its iteration budget with work still due at the current
    /// instant — a callback rescheduling itself with no delay.
    /// </summary>
    public bool AsyncDrainLimitExhausted => _session?.AsyncDrainLimitExhausted ?? false;

    /// <summary>Whether the page ran any script at all. A page with none has no session and no realm.</summary>
    public bool RanScripts => _session is not null;

    /// <summary>
    /// Whether timers or animation frames are still queued — after a settle, work the page scheduled
    /// past the load window, such as a <c>setInterval</c>, which always has a next tick.
    /// </summary>
    public bool HasPendingWork => _session?.HasPendingWork ?? false;

    /// <summary>Whether queued work is due inside the load window, which a finished settle leaves false.</summary>
    public bool HasWorkDueInLoadWindow => _session?.HasWorkDueInLoadWindow ?? false;

    /// <summary>
    /// Takes the navigation the page asked for — a script assigning <c>location</c>, a refresh
    /// <c>meta</c> — which the command line does not follow, or null when it asked for none.
    /// </summary>
    public NavigationRequest? TakePendingNavigation() => _session?.TakePendingNavigation();

    /// <summary>
    /// Runs the load window to a fixed point: microtasks, then the timers due within it, bounded as
    /// the window's load worker bounds them.
    /// </summary>
    internal void Settle(CancellationToken cancellationToken = default)
    {
        if (_session is null)
            return;

        using (MicroTaskSynchronizationContext.Install(_microTasks))
            _session.SettleLoadWindow(cancellationToken);

        // After the settle, not before: until the checkpoints have run to quiescence a rejection can
        // still be handled, and reporting it earlier would call a handled rejection unhandled.
        UnhandledRejections.Report();
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/> in the page's realm, records what it produced, then
    /// settles again, so an expression that starts the page's work — a test page's
    /// <c>runTests()</c> — has finished its timers and promises before the next one reads the result.
    /// </summary>
    /// <remarks>
    /// The expression runs as a host script on the page's own global, so an identifier a page script
    /// declared, including a top-level <c>const</c>, resolves exactly as it would in a later script on
    /// the page. The value is recorded when the expression returns, which is why the settle comes
    /// after it rather than before the next one.
    /// </remarks>
    public PageEvaluation Evaluate(int index, string expression, CancellationToken cancellationToken = default)
    {
        if (_session is null || _bridge is not DomBridge bridge)
            throw new InvalidOperationException("The page ran no scripts, so it has no realm to evaluate in.");

        PageEvaluation evaluation;
        using (MicroTaskSynchronizationContext.Install(_microTasks))
        {
            try
            {
                IJsRealm realm = bridge.Realm;
                JsValue value = realm.EvaluateHostScript(expression, ScriptLabel.Inline(_firstEvaluationLabel + index));
                evaluation = Describe(realm, index, expression, value);
            }
            catch (Exception ex)
            {
                evaluation = new PageEvaluation(index, expression, Type: null, Value: null, Error: ex.Message);
                RenderLogger.LogError(LogCategory.JavaScript, "CaptureService.EvaluatePage", $"Evaluation {index} failed: {ex.Message}", ex);
            }

            _session.SettleLoadWindow(cancellationToken);
        }

        UnhandledRejections.Report();
        return evaluation;
    }

    /// <summary>
    /// The document as the page's scripts left it — with its animations sampled at this instant, as a
    /// still image of it needs — or the document as fetched when it ran no scripts.
    /// </summary>
    public string Serialize()
    {
        if (_session is null)
            return _fetchedHtml;

        (_bridge as DomBridge)?.ResolveAnimationSnapshots();
        var html = _session.CurrentHtml();

        // Diffed against the fetched document, which the bundle holds beside it, this is precisely what
        // the page's JavaScript did or failed to do — which no other artefact of the run shows.
        ResourceTrace.RecordBody(ResourceTraceKind.Document, _url, html, DiagnosticSession.AfterScriptsLabel);
        return html;
    }

    /// <summary>
    /// Describes one evaluated value: its JavaScript <c>typeof</c>, and its string form.
    /// </summary>
    /// <remarks>
    /// <c>null</c> and <c>undefined</c> are reported as a type with no value rather than as the strings
    /// "null"/"undefined", so a caller can tell them from a page that genuinely produced that text —
    /// which is also why <c>null</c> reports <c>"null"</c> rather than <c>typeof</c>'s
    /// <c>"object"</c>. Everything else goes through the realm's own string conversion, which runs the
    /// value's <c>toString</c> as <c>String(value)</c> would; <c>JSON.stringify(...)</c>, the
    /// expression a caller reading structured results will use, is already a string.
    /// </remarks>
    private static PageEvaluation Describe(IJsRealm realm, int index, string expression, JsValue value) =>
        value.Kind switch
        {
            JsValueKind.Missing or JsValueKind.Undefined =>
                new PageEvaluation(index, expression, "undefined", Value: null, Error: null),
            JsValueKind.Null =>
                new PageEvaluation(index, expression, "null", Value: null, Error: null),
            _ => new PageEvaluation(index, expression, TypeOf(value.Kind), realm.ToJsString(value), Error: null),
        };

    private static string TypeOf(JsValueKind kind) => kind switch
    {
        JsValueKind.Boolean => "boolean",
        JsValueKind.Number => "number",
        JsValueKind.String => "string",
        JsValueKind.Symbol => "symbol",
        JsValueKind.BigInt => "bigint",
        JsValueKind.Function => "function",
        _ => "object",
    };

    public void Dispose() => _session?.Dispose();
}
