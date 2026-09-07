using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Dom;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// Whether a value a script wrote reaches the serialized document — and so reaches a form
/// submission, which is built by re-parsing that document.
/// <para>
/// The IDL <c>value</c> property and the <c>value</c> content attribute are different things in
/// HTML, and a browser keeps them apart on purpose: the attribute is the default, the property is
/// the current value. Here they have to meet, because serializing is how the current value leaves
/// the bridge at all. <c>ReflectRenderState</c> is where that happens.
/// </para>
/// </summary>
public class FormControlValueReflectionTests
{
    private const string PageUrl = "https://example.test/page";

    private static string Serialize(string html, string script) =>
        new ScriptEngine().Execute([script], html, PageUrl) ?? string.Empty;

    private static PageRequest? SubmitAfter(string html, string script)
    {
        using var session = new ScriptEngine().ExecuteInteractive([script], [], html, PageUrl);
        Assert.NotNull(session);

        string settled = session!.SettleLoadWindow(CancellationToken.None);
        NavigationRequest? pending = session.PendingNavigation;
        Assert.NotNull(pending);

        return BrowserApp.ToPageRequest(pending!, settled, PageUrl);
    }

    [Fact]
    public void AValueWrittenIntoAnEmptyFieldIsSerialized()
    {
        string html = Serialize(
            "<html><body><form><input id=\"q\" name=\"q\"></form></body></html>",
            "document.getElementById('q').value = 'written';");

        Assert.Contains("value=\"written\"", html);
    }

    [Fact]
    public void AValueWrittenOverAPresetOneIsSerialized()
    {
        string html = Serialize(
            "<html><body><form><input id=\"q\" name=\"q\" value=\"preset\"></form></body></html>",
            "document.getElementById('q').value = 'written';");

        Assert.Contains("value=\"written\"", html);
        Assert.DoesNotContain("value=\"preset\"", html);
    }

    [Fact]
    public void ClearingAPresetValueIsSerialized()
    {
        string html = Serialize(
            "<html><body><form><input id=\"q\" name=\"q\" value=\"preset\"></form></body></html>",
            "document.getElementById('q').value = '';");

        Assert.DoesNotContain("value=\"preset\"", html);
    }

    [Fact]
    public void ASubmittedFormCarriesTheValueTheScriptWrote()
    {
        // The end of the question: the host re-parses the serialized document, so whatever the two
        // tests above decide is what the server receives.
        PageRequest? request = SubmitAfter(
            "<html><body><form action=\"/search\" method=\"get\"><input id=\"q\" name=\"q\" value=\"preset\"></form></body></html>",
            """
            document.getElementById('q').value = 'written';
            document.forms[0].submit();
            """);

        Assert.Equal("https://example.test/search?q=written", request!.Url);
    }
}
