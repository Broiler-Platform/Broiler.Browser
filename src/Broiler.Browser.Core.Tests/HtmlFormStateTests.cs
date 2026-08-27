using System.Drawing;
using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.HtmlBridge;
using HtmlContainer = Broiler.HTML.Image.HtmlContainer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// End-to-end over the path a real submission takes: the renderer's serialized
/// document, the clicked control's attribute bag as the renderer reports it, and the
/// URL the browser then navigates to.
/// </summary>
public class HtmlFormStateTests
{
    private const string SearchPage =
        "<html><body style='margin:0'><form action='/search'>" +
        "<input id='q' name='q' type='text' value='broiler'>" +
        "<input type='hidden' name='src' value='hp'>" +
        "<input id='go' type='submit' name='btnG' value='Search'>" +
        "</form></body></html>";

    private static HtmlContainer LayOut(string html, int width = 600, int height = 200)
    {
        HtmlContainer container = new()
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            Location = PointF.Empty,
            MaxSize = new SizeF(width, height),
        };
        container.SetHtmlWithStyleSet(html);
        container.PerformLayout(new RectangleF(0, 0, width, height));
        return container;
    }

    private static Dictionary<string, string> SubmitAttributes(string id, string name, string value) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["type"] = "submit",
            ["name"] = name,
            ["value"] = value,
        };

    [Fact(Timeout = 600000)]
    public void ClickingSubmitCarriesTheFormsFieldsIntoTheUrl()
    {
        using HtmlContainer container = LayOut(SearchPage);
        HtmlFormState state = new();

        PageRequest? target = state.TryBuildSubmitRequest(
            container.GetHtml(),
            SubmitAttributes("go", "btnG", "Search"),
            "https://example.com/search");

        Assert.Equal("https://example.com/search?q=broiler&src=hp&btnG=Search", target?.Url);
    }

    [Fact(Timeout = 600000)]
    public void TypedTextReachesTheSubmittedUrl()
    {
        using HtmlContainer container = LayOut(SearchPage);
        HtmlFormEditor editor = new(new Broiler.UI.Panel.Standard.StandardPanel());
        HtmlFormState state = new();

        // Type into the field the way the browser does, then submit.
        PointF point = FindEditablePoint(container);
        Assert.True(editor.TryBegin(container, point));
        editor.Editor.Text = "typed query";
        Assert.True(editor.Commit());

        PageRequest? target = state.TryBuildSubmitRequest(
            container.GetHtml(),
            SubmitAttributes("go", "btnG", "Search"),
            "https://example.com/search");

        Assert.Equal("https://example.com/search?q=typed+query&src=hp&btnG=Search", target?.Url);
    }

    private static PointF FindEditablePoint(HtmlContainer container)
    {
        for (int y = 0; y < 200; y++)
        for (int x = 0; x < 600; x++)
        {
            if (container.GetEditableInputAt(new PointF(x, y)) is not null)
                return new PointF(x, y);
        }

        throw new InvalidOperationException("The page has no editable input.");
    }

    [Fact(Timeout = 600000)]
    public void PressingEnterInAFieldSubmitsItsFormWithoutAButton()
    {
        using HtmlContainer container = LayOut(SearchPage);
        HtmlFormState state = new();

        PageRequest? target = state.TryBuildFieldSubmitRequest(
            container.GetHtml(), "q", "q", "https://example.com/");

        // No submitter, so btnG does not contribute; the relative action resolves.
        Assert.Equal("https://example.com/search?q=broiler&src=hp", target?.Url);
    }

    [Fact(Timeout = 600000)]
    public void OrdinaryLinksInsideAFormAreNotTurnedIntoSubmissions()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/search'><a href='/about'>About</a>" +
            "<input name='q' value='x'></form></body></html>");
        HtmlFormState state = new();

        PageRequest? target = state.TryBuildSubmitRequest(
            container.GetHtml(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["href"] = "/about" },
            "https://example.com/about");

        Assert.Null(target);
    }

    [Fact(Timeout = 600000)]
    public void PostFormsCarryTheirFieldsInTheRequestBody()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/login' method='post'>" +
            "<input name='u' value='me'><input id='go' type='submit' name='s' value='Go'>" +
            "</form></body></html>");
        HtmlFormState state = new();

        PageRequest? request = state.TryBuildSubmitRequest(
            container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://example.com/login");

        Assert.NotNull(request);
        Assert.Equal(PageRequest.Post, request.Method);
        // The action keeps no query: a POST's fields go in the body.
        Assert.Equal("https://example.com/login", request.Url);
        Assert.Equal("u=me&s=Go", request.Body);
        Assert.Equal(HtmlFormSerializer.UrlEncoded, request.ContentType);
        Assert.False(request.IsRepeatable);
    }

    [Fact(Timeout = 600000)]
    public void PostFormsHonourTheirDeclaredEncoding()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/p' method='post' enctype='text/plain'>" +
            "<input name='a' value='1'><input id='go' type='submit' name='s' value='Go'>" +
            "</form></body></html>");
        HtmlFormState state = new();

        PageRequest? request = state.TryBuildSubmitRequest(
            container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/p");

        Assert.Equal(HtmlFormSerializer.TextPlain, request?.ContentType);
        Assert.Equal("a=1\r\ns=Go\r\n", request?.Body);
    }

    [Fact(Timeout = 600000)]
    public void AGetFormIgnoresEnctypeBecauseItHasNoBody()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/s' enctype='text/plain'>" +
            "<input name='a' value='1'><input id='go' type='submit' name='s' value='Go'>" +
            "</form></body></html>");
        HtmlFormState state = new();

        PageRequest? request = state.TryBuildSubmitRequest(
            container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/s");

        Assert.Equal(PageRequest.Get, request?.Method);
        Assert.Equal("https://x/s?a=1&s=Go", request?.Url);
        Assert.Null(request?.Body);
    }

    [Fact(Timeout = 600000)]
    public void ControlsOutsideAnyFormAreNotSubmissions()
    {
        using HtmlContainer container = LayOut(
            "<html><body><input id='go' type='submit' name='s' value='Go'></body></html>");
        HtmlFormState state = new();

        Assert.Null(state.TryBuildSubmitRequest(
            container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://example.com/"));
    }

    [Fact(Timeout = 600000)]
    public void ToggledCheckboxStateIsLayeredOverTheMarkup()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/s'>" +
            "<input id='a' type='checkbox' name='a' value='1'>" +
            "<input id='b' type='checkbox' name='b' value='2' checked>" +
            "<input id='go' type='submit' name='s' value='Go'>" +
            "</form></body></html>");
        HtmlFormState state = new();

        // Markup state: only b is checked.
        Assert.Equal(
            "https://x/s?b=2&s=Go",
            state.TryBuildSubmitRequest(container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/s")?.Url);

        state.SetChecked("a", "a", "1", isChecked: true);
        state.SetChecked("b", "b", "2", isChecked: false);

        Assert.Equal(
            "https://x/s?a=1&s=Go",
            state.TryBuildSubmitRequest(container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/s")?.Url);
    }

    [Fact(Timeout = 600000)]
    public void AChangedSelectionIsLayeredOverTheMarkup()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/s'>" +
            "<select id='sel' name='colour'><option value='r'>Red</option>" +
            "<option value='g' selected>Green</option></select>" +
            "<input id='go' type='submit' name='s' value='Go'>" +
            "</form></body></html>");
        HtmlFormState state = new();

        // Markup selection.
        Assert.Equal(
            "https://x/s?colour=g&s=Go",
            state.TryBuildSubmitRequest(container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/s")?.Url);

        state.SetSelectedValue("sel", "colour", "r");

        Assert.Equal(
            "https://x/s?colour=r&s=Go",
            state.TryBuildSubmitRequest(container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/s")?.Url);
    }

    [Fact(Timeout = 600000)]
    public void ResetForgetsPerPageControlState()
    {
        HtmlFormState state = new();
        state.SetChecked("a", "a", "1", isChecked: true);
        state.SetSelectedValue("sel", "colour", "r");

        state.Reset();

        Assert.Null(state.GetChecked("a", "a", "1"));
        Assert.Null(state.GetSelectedValue("sel", "colour"));
    }

    [Fact(Timeout = 600000)]
    public void RelativeAndAbsoluteActionsBothResolve()
    {
        Assert.Equal("https://x/search", HtmlFormState.ResolveAction("/search", "https://x/page"));
        Assert.Equal("https://y/s", HtmlFormState.ResolveAction("https://y/s", "https://x/page"));
        // An empty action submits to the page itself.
        Assert.Equal("https://x/page", HtmlFormState.ResolveAction(null, "https://x/page"));
    }

    [Fact(Timeout = 600000)]
    public void UnparseablePagesFallBackInsteadOfThrowing()
    {
        HtmlFormState state = new();

        Assert.Null(state.TryBuildSubmitRequest(string.Empty, SubmitAttributes("go", "s", "Go"), "https://x/"));
        Assert.Null(state.TryBuildSubmitRequest("<html>", null, "https://x/"));
    }

    /// <summary>
    /// Checkbox and radio geometry is only reachable by id, so the browsing path
    /// stamps one on controls that lack it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void ToggleControlsAreStampedWithIdsForGeometryLookup()
    {
        string stamped = HtmlPostProcessor.StampFormControlIds(
            "<form><input type='checkbox' name='a'><input type='radio' name='b' value='1'></form>");

        Assert.Contains($"id=\"{HtmlPostProcessor.SyntheticIdPrefix}0\"", stamped);
        Assert.Contains($"id=\"{HtmlPostProcessor.SyntheticIdPrefix}1\"", stamped);
    }

    [Fact(Timeout = 600000)]
    public void StampingKeepsExistingIdsAndLeavesOtherControlsAlone()
    {
        string stamped = HtmlPostProcessor.StampFormControlIds(
            "<form><input type='checkbox' id='mine' name='a'>" +
            "<input type='text' name='q'><input type='submit' value='Go'></form>");

        Assert.Contains("id='mine'", stamped);
        Assert.DoesNotContain(HtmlPostProcessor.SyntheticIdPrefix, stamped);
    }

    [Fact(Timeout = 600000)]
    public void StampedControlsKeepTheirAttributesAndSelfClosingForm()
    {
        string stamped = HtmlPostProcessor.StampFormControlIds(
            "<input type=\"checkbox\" name=\"a\" value=\"1\" checked />");

        Assert.Contains("name=\"a\"", stamped);
        Assert.Contains("value=\"1\"", stamped);
        Assert.Contains("checked", stamped);
        Assert.Contains($"id=\"{HtmlPostProcessor.SyntheticIdPrefix}0\"", stamped);
        Assert.EndsWith("/>", stamped);
    }

    [Fact(Timeout = 600000)]
    public void AChosenFileIsPostedAsAMultipartPart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-form-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "file body");
        try
        {
            using HtmlContainer container = LayOut(
                "<html><body><form action='/upload' method='post' enctype='multipart/form-data'>" +
                "<input id='f' type='file' name='doc'>" +
                "<input id='go' type='submit' name='s' value='Go'>" +
                "</form></body></html>");
            HtmlFormState state = new();
            state.SetSelectedFile("f", "doc", path);

            PageRequest? request = state.TryBuildSubmitRequest(
                container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/upload");

            Assert.NotNull(request);
            Assert.Equal(PageRequest.Post, request.Method);
            Assert.StartsWith(HtmlFormSerializer.MultipartFormData + "; boundary=", request.ContentType);
            Assert.NotNull(request.BinaryBody);

            string body = System.Text.Encoding.UTF8.GetString(request.BinaryBody);
            Assert.Contains($"filename=\"{Path.GetFileName(path)}\"", body);
            Assert.Contains("file body", body);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(Timeout = 600000)]
    public void AFileThatHasGoneAwaySubmitsAsNothingChosen()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/upload' method='post' enctype='multipart/form-data'>" +
            "<input id='f' type='file' name='doc'>" +
            "<input id='go' type='submit' name='s' value='Go'>" +
            "</form></body></html>");
        HtmlFormState state = new();
        state.SetSelectedFile("f", "doc", "/no/such/file/anywhere.bin");

        PageRequest? request = state.TryBuildSubmitRequest(
            container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/upload");

        // An unreadable path must not fail the whole submission.
        Assert.NotNull(request);
        string body = System.Text.Encoding.UTF8.GetString(request.BinaryBody!);
        Assert.Contains("filename=\"\"", body);
    }

    [Fact(Timeout = 600000)]
    public void ResetForgetsAChosenFile()
    {
        HtmlFormState state = new();
        state.SetSelectedFile("f", "doc", "/tmp/x");

        state.Reset();

        Assert.Null(state.GetSelectedFile("f", "doc"));
    }

    [Fact(Timeout = 600000)]
    public void AMultipleFileInputSubmitsOnePartPerFile()
    {
        string first = WriteTempFile("one");
        string second = WriteTempFile("two");
        try
        {
            using HtmlContainer container = LayOut(
                "<html><body><form action='/upload' method='post' enctype='multipart/form-data'>" +
                "<input id='f' type='file' name='docs' multiple>" +
                "<input id='go' type='submit' name='s' value='Go'>" +
                "</form></body></html>");
            HtmlFormState state = new();
            state.AddSelectedFile("f", "docs", first);
            state.AddSelectedFile("f", "docs", second);

            PageRequest? request = state.TryBuildSubmitRequest(
                container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/upload");

            string body = System.Text.Encoding.UTF8.GetString(request!.BinaryBody!);
            Assert.Contains($"filename=\"{Path.GetFileName(first)}\"", body);
            Assert.Contains($"filename=\"{Path.GetFileName(second)}\"", body);
            Assert.Contains("one", body);
            Assert.Contains("two", body);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact(Timeout = 600000)]
    public void AFileInputWithoutMultipleSubmitsOnlyItsFirstFile()
    {
        string first = WriteTempFile("one");
        string second = WriteTempFile("two");
        try
        {
            using HtmlContainer container = LayOut(
                "<html><body><form action='/upload' method='post' enctype='multipart/form-data'>" +
                "<input id='f' type='file' name='doc'>" +
                "<input id='go' type='submit' name='s' value='Go'>" +
                "</form></body></html>");
            HtmlFormState state = new();
            state.AddSelectedFile("f", "doc", first);
            state.AddSelectedFile("f", "doc", second);

            PageRequest? request = state.TryBuildSubmitRequest(
                container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/upload");

            string body = System.Text.Encoding.UTF8.GetString(request!.BinaryBody!);
            Assert.Contains("one", body);
            Assert.DoesNotContain("two", body);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact(Timeout = 600000)]
    public void AddingTheSameFileTwiceDoesNotDuplicateIt()
    {
        HtmlFormState state = new();
        state.AddSelectedFile("f", "docs", "/tmp/a");
        state.AddSelectedFile("f", "docs", "/tmp/a");

        Assert.Equal(["/tmp/a"], state.GetSelectedFiles("f", "docs"));
    }

    [Fact(Timeout = 600000)]
    public void PickingASingleFileReplacesThePreviousChoice()
    {
        HtmlFormState state = new();
        state.SetSelectedFile("f", "doc", "/tmp/a");
        state.SetSelectedFile("f", "doc", "/tmp/b");

        Assert.Equal(["/tmp/b"], state.GetSelectedFiles("f", "doc"));
    }

    [Fact(Timeout = 600000)]
    public void UnreadableFilesAreDroppedWithoutLosingTheReadableOnes()
    {
        string good = WriteTempFile("kept");
        try
        {
            using HtmlContainer container = LayOut(
                "<html><body><form action='/upload' method='post' enctype='multipart/form-data'>" +
                "<input id='f' type='file' name='docs' multiple>" +
                "<input id='go' type='submit' name='s' value='Go'>" +
                "</form></body></html>");
            HtmlFormState state = new();
            state.AddSelectedFile("f", "docs", "/no/such/file.bin");
            state.AddSelectedFile("f", "docs", good);

            PageRequest? request = state.TryBuildSubmitRequest(
                container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/upload");

            string body = System.Text.Encoding.UTF8.GetString(request!.BinaryBody!);
            Assert.Contains("kept", body);
            Assert.DoesNotContain("file.bin", body);
        }
        finally
        {
            File.Delete(good);
        }
    }

    private static string WriteTempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-form-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// History gates re-navigation on this: a GET can be replayed freely, a
    /// submission goes through a confirmation first.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void OnlyGetRequestsAreRepeatable()
    {
        Assert.True(PageRequest.ForUrl("https://x/").IsRepeatable);
        Assert.True(new PageRequest("https://x/", PageRequest.Get).IsRepeatable);
        Assert.False(new PageRequest("https://x/", PageRequest.Post, Body: "a=1").IsRepeatable);
    }

    [Fact(Timeout = 600000)]
    public void ARequestCarriesEitherATextOrABinaryBody()
    {
        Assert.False(PageRequest.ForUrl("https://x/").HasBody);
        Assert.True(new PageRequest("https://x/", PageRequest.Post, Body: "a=1").HasBody);
        Assert.True(new PageRequest("https://x/", PageRequest.Post) { BinaryBody = [1, 2] }.HasBody);
    }

    [Fact(Timeout = 600000)]
    public void AMultiSelectSubmitsEveryChosenOption()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/s'>" +
            "<select id='sel' name='colours' multiple><option value='r'>Red</option>" +
            "<option value='g' selected>Green</option><option value='b'>Blue</option></select>" +
            "<input id='go' type='submit' name='s' value='Go'>" +
            "</form></body></html>");
        HtmlFormState state = new();

        // Markup selection.
        Assert.Equal(
            "https://x/s?colours=g&s=Go",
            state.TryBuildSubmitRequest(container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/s")?.Url);

        state.SetSelectedValues("sel", "colours", ["r", "b"]);

        Assert.Equal(
            "https://x/s?colours=r&colours=b&s=Go",
            state.TryBuildSubmitRequest(container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/s")?.Url);
    }

    [Fact(Timeout = 600000)]
    public void AnEmptiedMultiSelectSubmitsNothingForItself()
    {
        using HtmlContainer container = LayOut(
            "<html><body><form action='/s'>" +
            "<select id='sel' name='colours' multiple><option value='g' selected>Green</option></select>" +
            "<input id='go' type='submit' name='s' value='Go'>" +
            "</form></body></html>");
        HtmlFormState state = new();
        state.SetSelectedValues("sel", "colours", []);

        // The user's empty selection wins over the markup's `selected`.
        Assert.Equal(
            "https://x/s?s=Go",
            state.TryBuildSubmitRequest(container.GetHtml(), SubmitAttributes("go", "s", "Go"), "https://x/s")?.Url);
    }
}
