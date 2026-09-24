using Broiler.HtmlBridge.Logging;
using Broiler.JavaScript.BuiltIns.Promise;

namespace Broiler.Cli;

/// <summary>
/// Promises rejected with nothing to handle them — a browser's <c>Uncaught (in promise)</c> — which a
/// diagnostics bundle reports and nothing else in a capture would ever hear about: a rejection
/// travels through the promise machinery rather than out of an evaluation, so no catch block in the
/// pipeline sees it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one Broiler.JS type the command line names.</b> The tracker is the engine's own, and JSEAL
/// has no contract for it yet. That still covers every page: under <c>Debug-VM</c>/<c>Release-VM</c>
/// the document-bearing paths are served by Broiler.JS too (<c>BrowserApp.NewScriptEngine</c>), and
/// only those paths run page scripts.
/// </para>
/// <para>
/// The Broiler repository compiled this in only when a patch adding the tracker had been applied to
/// its Broiler.JS checkout. The tracker ships in the Broiler.JS packages now, so it is always here.
/// </para>
/// </remarks>
internal static class UnhandledRejections
{
    /// <summary>Turns the engine's collection of unhandled rejections on or off.</summary>
    public static void Track(bool enabled) => JSPromiseRejectionTracker.Enabled = enabled;

    /// <summary>
    /// Logs every rejection collected since the last report. Empty unless a diagnostics bundle turned
    /// collection on, so an ordinary capture pays a lock and an empty list.
    /// </summary>
    public static void Report()
    {
        foreach (var rejection in JSPromiseRejectionTracker.TakePending())
        {
            RenderLogger.Log(
                LogCategory.JavaScript,
                LogLevel.Error,
                "CaptureService.UnhandledRejection",
                $"Unhandled promise rejection: {rejection.Describe()}");
        }
    }
}
