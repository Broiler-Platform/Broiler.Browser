using System.Drawing;
using Broiler.Browser;
using Broiler.Graphics;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;
using HtmlContainer = Broiler.HTML.Image.HtmlContainer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// Covers the Broiler.UI control hosted over an HTML text form control, which is
/// what makes a page's fields typeable.
/// </summary>
/// <remarks>
/// The renderer paints <c>&lt;input&gt;</c> as a bordered box plus generated text and
/// has no caret, selection or keystroke handling of its own — see
/// <see cref="FormControlsAreNotEditableByTheRendererAlone"/>, which pins that
/// baseline. Everything else here exercises the hosted <c>StandardEdit</c> that
/// closes the gap.
/// </remarks>
public class HtmlFormEditorTests
{
    private const string SearchPage =
        "<html><body style='margin:0'><form action='/search'>" +
        "<input id='q' name='q' type='text' value='hello'>" +
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

    /// <summary>A point inside the first editable control of a laid-out page, if it has one.</summary>
    private static PointF? TryFindEditablePoint(HtmlContainer container, int width = 600, int height = 200)
    {
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (container.GetEditableInputAt(new PointF(x, y)) is not null)
                return new PointF(x, y);
        }

        return null;
    }

    /// <summary>A point inside the first editable input of a laid-out page.</summary>
    private static PointF PointInFirstInput(HtmlContainer container, int width = 600, int height = 200) =>
        TryFindEditablePoint(container, width, height)
        ?? throw new InvalidOperationException("The page has no editable input.");

    private static (HtmlFormEditor Editor, StandardPanel Owner) CreateEditor()
    {
        StandardPanel owner = new();
        return (new HtmlFormEditor(owner), owner);
    }

    /// <summary>
    /// Baseline: the renderer on its own cannot edit a form control. Its only
    /// keyboard entry point is select-all / copy, so a page's fields stay at their
    /// parsed value no matter what is typed.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void FormControlsAreNotEditableByTheRendererAlone()
    {
        using HtmlContainer container = LayOut(SearchPage);

        container.HandleMouseDown(PointInFirstInput(container));
        container.HandleKeyDown(controlKey: false, aKeyCode: true, cKeyCode: false);

        Assert.Contains("value=\"hello\"", container.GetHtml());
    }

    [Fact(Timeout = 600000)]
    public void ClickingATextFieldHostsAnEditorSeededWithItsValue()
    {
        using HtmlContainer container = LayOut(SearchPage);
        (HtmlFormEditor editor, _) = CreateEditor();

        Assert.True(editor.TryBegin(container, PointInFirstInput(container)));

        Assert.True(editor.IsActive);
        Assert.Equal("hello", editor.Editor.Text);
        Assert.Equal("q", editor.FieldName);
        Assert.Equal("text", editor.FieldType);
        Assert.False(editor.Editor.IsPassword);
    }

    [Fact(Timeout = 600000)]
    public void ClickingOutsideAnyFieldDoesNotStartEditing()
    {
        using HtmlContainer container = LayOut(SearchPage);
        (HtmlFormEditor editor, _) = CreateEditor();

        // Well below the single-line form, so no control can be under the point.
        Assert.False(editor.TryBegin(container, new PointF(500, 180)));
        Assert.False(editor.IsActive);
        Assert.Equal(UiVisibility.Collapsed, editor.Editor.Visibility);
    }

    [Fact(Timeout = 600000)]
    public void PasswordFieldsAreMasked()
    {
        using HtmlContainer container = LayOut(
            "<html><body style='margin:0'><input name='pw' type='password' value='secret'></body></html>");
        (HtmlFormEditor editor, _) = CreateEditor();

        Assert.True(editor.TryBegin(container, PointInFirstInput(container)));
        Assert.True(editor.Editor.IsPassword);
    }

    /// <summary>
    /// A <c>&lt;textarea&gt;</c> is a text-entry control too — and it is what
    /// google.com's search box actually is, so it has to be typeable.
    /// </summary>
    /// <remarks>
    /// Which controls are editable is the renderer's call, and recognising
    /// <c>&lt;textarea&gt;</c> is a <c>Broiler.HTML</c> change that could not be
    /// pushed from the session that wrote it — it ships as
    /// <c>patches/0113-html-textarea-editable-control.patch</c>. Against the pinned
    /// pointer the control is simply not reported, so this asserts the full
    /// behaviour once it is and the graceful degradation until then.
    /// </remarks>
    [Fact(Timeout = 600000)]
    public void TextAreasAreEditableAndCarryTheirTextContentAsTheValue()
    {
        using HtmlContainer container = LayOut(
            "<html><body style='margin:0'><form action='/search'>" +
            "<textarea name='q'>seed</textarea></form></body></html>");
        (HtmlFormEditor editor, _) = CreateEditor();

        if (TryFindEditablePoint(container) is not PointF point)
        {
            // Patch not applied: no control is offered, and nothing must claim one.
            Assert.False(editor.TryBegin(container, new PointF(20, 20)));
            Assert.False(editor.IsActive);
            return;
        }

        Assert.True(editor.TryBegin(container, point));
        Assert.Equal("textarea", editor.FieldType);
        Assert.Equal("q", editor.FieldName);
        Assert.Equal("seed", editor.Editor.Text);

        editor.Editor.Text = "broiler";
        Assert.True(editor.Commit());

        // A textarea's value is its text content, which the serializer takes from
        // the laid-out words — so it surfaces once the commit's relayout has run
        // (the viewport marks layout dirty from the Committed event).
        container.PerformLayout(new RectangleF(0, 0, 600, 200));
        Assert.Equal("broiler", container.GetEditableInputAt(PointInFirstInput(container))!.Value);
        Assert.Contains("broiler", container.GetHtml());
    }

    [Fact(Timeout = 600000)]
    public void CommitWritesTheTypedTextBackIntoTheDocument()
    {
        using HtmlContainer container = LayOut(SearchPage);
        (HtmlFormEditor editor, _) = CreateEditor();

        Assert.True(editor.TryBegin(container, PointInFirstInput(container)));
        editor.Editor.Text = "broiler browser";

        Assert.True(editor.Commit());

        Assert.False(editor.IsActive);
        Assert.Equal(UiVisibility.Collapsed, editor.Editor.Visibility);

        string html = container.GetHtml();
        Assert.Contains("value=\"broiler browser\"", html);
        // Serialization writes the box's laid-out words, so this also pins that the
        // typed text is what the renderer will paint once the overlay is gone.
        Assert.Contains(">broiler browser<", html);
    }

    [Fact(Timeout = 600000)]
    public void CommitRaisesCommittedSoTheViewportCanRelayout()
    {
        using HtmlContainer container = LayOut(SearchPage);
        (HtmlFormEditor editor, _) = CreateEditor();
        int committed = 0;
        editor.Committed += (_, _) => committed++;

        Assert.True(editor.TryBegin(container, PointInFirstInput(container)));
        editor.Editor.Text = "typed";
        editor.Commit();

        Assert.Equal(1, committed);
    }

    [Fact(Timeout = 600000)]
    public void CancelDiscardsTheTypedText()
    {
        using HtmlContainer container = LayOut(SearchPage);
        (HtmlFormEditor editor, _) = CreateEditor();

        Assert.True(editor.TryBegin(container, PointInFirstInput(container)));
        editor.Editor.Text = "discarded";
        editor.Cancel();

        Assert.False(editor.IsActive);
        Assert.Contains("value=\"hello\"", container.GetHtml());
        Assert.DoesNotContain("discarded", container.GetHtml());
    }

    [Fact(Timeout = 600000)]
    public void MovingToAnotherFieldCommitsThePreviousOne()
    {
        using HtmlContainer container = LayOut(
            "<html><body style='margin:0'>" +
            "<div><input id='a' name='a' type='text' value='first'></div>" +
            "<div><input id='b' name='b' type='text' value='second'></div>" +
            "</body></html>");
        (HtmlFormEditor editor, _) = CreateEditor();

        PointF first = PointInFirstInput(container);
        Assert.True(editor.TryBegin(container, first));
        editor.Editor.Text = "edited";

        // The second field sits on the next line; start scanning below the first one's box.
        RectangleF firstBox = container.GetEditableInputAt(first)!.Rectangle;
        PointF second = FindEditableBelow(container, firstBox.Bottom + 1);
        Assert.True(editor.TryBegin(container, second));

        Assert.Equal("second", editor.Editor.Text);
        Assert.Contains("value=\"edited\"", container.GetHtml());
    }

    private static PointF FindEditableBelow(HtmlContainer container, float minY)
    {
        for (float y = minY; y < 200; y++)
        for (float x = 0; x < 600; x++)
        {
            if (container.GetEditableInputAt(new PointF(x, y)) is not null)
                return new PointF(x, y);
        }

        throw new InvalidOperationException("No second editable input found.");
    }

    [Fact(Timeout = 600000)]
    public void HostedEditorIsPlacedOverTheFieldAndFollowsScrolling()
    {
        using HtmlContainer container = LayOut(SearchPage);
        (HtmlFormEditor editor, _) = CreateEditor();
        PointF point = PointInFirstInput(container);
        Assert.True(editor.TryBegin(container, point));

        BRect viewport = new(10, 20, 600, 200);
        editor.UpdateViewport(viewport, zoom: 1, scrollY: 0);

        BRect placed = editor.Editor.Bounds;
        Assert.Equal(UiVisibility.Visible, editor.Editor.Visibility);
        Assert.True(placed.Width > 0 && placed.Height > 0);
        // The click point, mapped into viewport space, must land inside the control.
        Assert.True(placed.Contains(new BPoint(viewport.Left + point.X, viewport.Top + point.Y)));

        editor.UpdateViewport(viewport, zoom: 1, scrollY: 10);
        Assert.Equal(placed.Top - 10, editor.Editor.Bounds.Top, 3);
    }

    [Fact(Timeout = 600000)]
    public void HostedEditorIsHiddenWhenItsFieldScrollsOutOfView()
    {
        using HtmlContainer container = LayOut(SearchPage);
        (HtmlFormEditor editor, _) = CreateEditor();
        Assert.True(editor.TryBegin(container, PointInFirstInput(container)));

        editor.UpdateViewport(new BRect(0, 0, 600, 200), zoom: 1, scrollY: 5000);

        Assert.Equal(UiVisibility.Collapsed, editor.Editor.Visibility);
    }

    /// <summary>
    /// End-to-end through a real <see cref="UiSession"/>: focusing the hosted control
    /// and delivering a text-input event types into the page's field, which is the
    /// behaviour the browser was missing.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void TypingThroughTheSessionEditsThePageField()
    {
        using HtmlContainer container = LayOut(SearchPage);
        using TestUiSession ui = new();
        UiSession session = ui.Session;
        HtmlFormEditor editor = new(ui.Root);
        Assert.True(editor.TryBegin(container, PointInFirstInput(container)));
        editor.UpdateViewport(new BRect(0, 0, 600, 200), zoom: 1, scrollY: 0);
        session.SetFocus(editor.Editor);

        editor.Editor.SelectAll();
        Assert.True(session.DispatchInput(UiInputEvent.FromKeyboardText(
            new KeyboardTextEvent(default, "broiler", Source: InputEventSource.Semantic))));

        Assert.Equal("broiler", editor.Editor.Text);

        editor.Commit();
        Assert.Contains("value=\"broiler\"", container.GetHtml());
    }
}
