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

    [Fact]
    public void ATextareaValueIsWrittenIntoItsChildText()
    {
        // A textarea has no `value` attribute for a write to land in — its value IS its child text
        // (HTML §4.10.11), so that is what reflection has to change.
        string html = Serialize(
            "<html><body><form><textarea id=\"t\" name=\"t\">preset</textarea></form></body></html>",
            "document.getElementById('t').value = 'written';");

        Assert.Contains("written", html);
        Assert.DoesNotContain("preset", html);
    }

    [Fact]
    public void ASubmittedTextareaCarriesTheValueTheScriptWrote()
    {
        PageRequest? request = SubmitAfter(
            "<html><body><form action=\"/save\" method=\"get\"><textarea id=\"t\" name=\"t\">preset</textarea></form></body></html>",
            """
            document.getElementById('t').value = 'written';
            document.forms[0].submit();
            """);

        Assert.Equal("https://example.test/save?t=written", request!.Url);
    }

    [Fact]
    public void ASelectValueMovesTheSelectedAttribute()
    {
        // A select's value is which option carries `selected`, so reflecting it moves an attribute
        // between siblings rather than writing one on the select.
        string html = Serialize(
            "<html><body><form><select id=\"s\" name=\"s\"><option value=\"a\" selected>A</option><option value=\"b\">B</option></select></form></body></html>",
            "document.getElementById('s').value = 'b';");

        int a = html.IndexOf("value=\"a\"", StringComparison.Ordinal);
        int b = html.IndexOf("value=\"b\"", StringComparison.Ordinal);
        int selected = html.IndexOf("selected", StringComparison.Ordinal);

        Assert.True(selected > b, $"`selected` should sit on the second option: {html}");
        Assert.True(a < b);
    }

    [Fact]
    public void ASubmittedSelectCarriesTheOptionTheScriptChose()
    {
        PageRequest? request = SubmitAfter(
            "<html><body><form action=\"/pick\" method=\"get\"><select id=\"s\" name=\"s\"><option value=\"a\" selected>A</option><option value=\"b\">B</option></select></form></body></html>",
            """
            document.getElementById('s').value = 'b';
            document.forms[0].submit();
            """);

        Assert.Equal("https://example.test/pick?s=b", request!.Url);
    }

    [Fact]
    public void AControlThePageNeverTouchedIsLeftAsAuthored()
    {
        // TryGet answers "did a script set this", and nothing else should move.
        string html = Serialize(
            "<html><body><form><textarea name=\"t\">preset</textarea>"
            + "<select name=\"s\"><option value=\"a\" selected>A</option><option value=\"b\">B</option></select></form></body></html>",
            "var untouched = 1;");

        Assert.Contains("preset", html);
        Assert.Contains("selected", html);
    }
}
