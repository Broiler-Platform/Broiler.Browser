using Broiler.Browser;
using Broiler.CSS;
using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Logging;

namespace Broiler.Cli;

/// <summary>
/// Runs smoke tests for the embedded rendering engines: the CSS model the renderer uses, and the
/// JavaScript engine the browser composes.
/// </summary>
public sealed class EngineTestService
{
    /// <summary>
    /// Result of an individual engine test.
    /// </summary>
    public sealed class EngineTestResult
    {
        /// <summary>Name of the engine tested.</summary>
        public required string EngineName { get; init; }

        /// <summary>Whether the test passed.</summary>
        public required bool Passed { get; init; }

        /// <summary>Error message if the test failed; <c>null</c> on success.</summary>
        public string? Error { get; init; }
    }

    /// <summary>
    /// Runs smoke tests for all embedded engines and returns results.
    /// </summary>
    public IReadOnlyList<EngineTestResult> RunAll() =>
        [
            TestHtmlRenderer(),
            TestJavaScript(),
        ];

    /// <summary>
    /// Tests the renderer CSS dependency through the canonical shared model.
    /// </summary>
    public EngineTestResult TestHtmlRenderer()
    {
        try
        {
            var sheet = new CssParser().ParseStyleSheet(
                "p { color: red; font-size: 14px; color: blue; }");
            if (sheet.Rules.Count != 1 || sheet.Rules[0] is not CssStyleRule rule)
                throw new InvalidOperationException("Shared CSS rule parsing failed.");
            if (rule.Declarations.GetPropertyValue("color") != "blue")
                throw new InvalidOperationException("Shared CSS declaration precedence failed.");

            return new EngineTestResult { EngineName = "HTML-Renderer", Passed = true };
        }
        catch (Exception ex)
        {
            RenderLogger.LogError(LogCategory.HtmlRenderer, "EngineTestService.TestHtmlRenderer", $"Smoke test failed: {ex.Message}", ex);
            return new EngineTestResult { EngineName = "HTML-Renderer", Passed = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Tests the JavaScript engine the browser composes for this build — Broiler.JS, or the Broiler.VM
    /// JavaScript profile under <c>Debug-VM</c>/<c>Release-VM</c> — by running a script that throws
    /// unless its arithmetic comes out right.
    /// </summary>
    /// <remarks>
    /// The Broiler repository's command line evaluated <c>1 + 2</c> on a Broiler.JS context it built
    /// itself and reported it as "YantraJS", the engine Broiler.JS descends from. That tested an
    /// engine, not the one a capture runs on; this asks the same factory the window does.
    /// </remarks>
    public EngineTestResult TestJavaScript()
    {
        IScriptEngine engine = BrowserApp.NewScriptEngine(new DomBridgeFactory());
        string name = EngineName(engine);
        try
        {
            var result = engine.ExecuteDetailed(
                ["if (1 + 2 !== 3) throw new Error('Expected 3 but got ' + (1 + 2) + '.');"]);
            if (!result.Success)
                throw new InvalidOperationException(result.Errors.FirstOrDefault()?.Message ?? "The script failed.");

            return new EngineTestResult { EngineName = name, Passed = true };
        }
        catch (Exception ex)
        {
            RenderLogger.LogError(LogCategory.JavaScript, "EngineTestService.TestJavaScript", $"Smoke test failed: {ex.Message}", ex);
            return new EngineTestResult { EngineName = name, Passed = false, Error = ex.Message };
        }
    }

    /// <summary>The engine's name as a reader knows it, not the type that hosts it.</summary>
    internal static string EngineName(IScriptEngine engine) =>
        engine.GetType().Name == "VmScriptEngine" ? "Broiler.VM" : "Broiler.JS";
}
