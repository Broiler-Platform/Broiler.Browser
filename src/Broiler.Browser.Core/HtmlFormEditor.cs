using System.Drawing;
using Broiler.Graphics;
using Broiler.HTML.Core.Entities;
using Broiler.UI;
using Broiler.UI.Edit.Standard;
using HtmlContainer = Broiler.HTML.Image.HtmlContainer;

namespace Broiler.Browser;

/// <summary>
/// Makes HTML text form controls typeable by hosting a real Broiler.UI
/// <see cref="StandardEdit"/> over the box the HTML renderer paints.
/// </summary>
/// <remarks>
/// <para>
/// The HTML renderer paints <c>&lt;input&gt;</c> as ordinary boxes — a border, a
/// background and the <c>value</c> attribute as generated text. Nothing in that
/// pipeline owns a caret, a selection or a keyboard handler, so before this class
/// existed a page's fields were pixels: clicking one started a text selection and
/// typing went to the viewport's scroll shortcuts.
/// </para>
/// <para>
/// Rather than teach the renderer to edit text, the editable control is *hosted*:
/// clicking a text field places a <see cref="StandardEdit"/> — the same control the
/// address bar uses — over the field's border box and gives it session focus. That
/// buys caret, selection, clipboard, IME and password masking from Broiler.UI
/// instead of reimplementing them. The overlay is opaque and matches the renderer's
/// UA styling (see <c>CssDefaults</c>: 1px solid #767676, 1px 2px padding,
/// 13.3333px Arial), so it reads as the same control the page painted.
/// </para>
/// <para>
/// On commit the text is written back into the document through
/// <see cref="HtmlContainer.SetEditableInputValueAtDocumentPoint"/>, which updates
/// the <c>value</c> attribute and the generated text, so the painted field keeps
/// the typed text after the overlay goes away.
/// </para>
/// <para>
/// Which controls qualify is the renderer's call — this class hosts whatever
/// <see cref="HtmlContainer.GetEditableInputAt"/> reports, today the text-like
/// <c>&lt;input&gt;</c> types (text, search, email, url, tel, number, password).
/// </para>
/// </remarks>
internal sealed class HtmlFormEditor
{
    /// <summary>UA default input border (<c>#767676</c>), so the overlay matches the painted box.</summary>
    private static readonly BColor UaInputBorder = BColor.FromArgb(0xFF, 0x76, 0x76, 0x76);

    private const string UaInputFontFamily = "Arial";
    private const double UaInputFontSize = 13.3333;
    private const double UaInputPaddingX = 2;
    private const double UaInputPaddingY = 1;

    private readonly UiElement _owner;
    private readonly StandardEdit _edit;
    private HtmlContainer? _container;
    private PointF _anchor;
    private RectangleF _documentRect;

    public HtmlFormEditor(UiElement owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _edit = new StandardEdit
        {
            Visibility = UiVisibility.Collapsed,
            Font = new BFontStyle(UaInputFontFamily, UaInputFontSize),
            Background = BColor.White,
            BorderColor = UaInputBorder,
            FocusRing = BrowserPalette.Accent,
            PaddingX = UaInputPaddingX,
            PaddingY = UaInputPaddingY,
            CornerRadius = 0,
        };
        _edit.Submitted += (_, _) => Submit();
        _owner.AddChild(_edit);
    }

    /// <summary>Raised after <see cref="Commit"/> wrote a value back into the document.</summary>
    public event EventHandler? Committed;

    /// <summary>
    /// Raised when the user pressed Enter in the hosted control, after its value has
    /// been committed. Carries the field's <c>id</c> and <c>name</c> so the host can
    /// find and submit the enclosing form.
    /// </summary>
    public event EventHandler<HtmlFormFieldSubmitEventArgs>? SubmitRequested;

    /// <summary>The <c>id</c> of the field being edited, empty when idle or unidentified.</summary>
    public string FieldId { get; private set; } = string.Empty;

    /// <summary>The hosted Broiler.UI control; the session focuses this while a field is being edited.</summary>
    public StandardEdit Editor => _edit;

    /// <summary>Whether a field is currently being edited.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The <c>name</c> of the field being edited, empty when idle or unnamed.</summary>
    public string FieldName { get; private set; } = string.Empty;

    /// <summary>The <c>type</c> of the field being edited, empty when idle.</summary>
    public string FieldType { get; private set; } = string.Empty;

    /// <summary>The text currently in the hosted control.</summary>
    public string Text => _edit.Text;

    /// <summary>
    /// Starts editing the form control under <paramref name="viewportPoint"/> (a point in the
    /// viewport's own coordinate space, as produced by the viewport's pointer handling).
    /// Returns <c>false</c> — leaving any in-progress edit untouched — when the point is not
    /// over an editable control.
    /// </summary>
    public bool TryBegin(HtmlContainer? container, PointF viewportPoint)
    {
        if (container is null)
            return false;

        FormInputElementData<RectangleF>? field = container.GetEditableInputAt(viewportPoint);
        if (field is null)
            return false;

        // Committing first keeps the previous field's text when the user clicks
        // straight from one input into another.
        if (IsActive)
            Commit();

        RectangleF rect = field.Rectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
            return false;

        _container = container;
        _documentRect = rect;
        // The write-back is addressed by document point, so anchor on the box's
        // centre: it stays inside the control even after the value changes width.
        _anchor = new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));

        FieldId = field.Id;
        FieldName = field.Name;
        FieldType = field.Type;
        _edit.IsPassword = string.Equals(field.Type, "password", StringComparison.OrdinalIgnoreCase);
        _edit.Text = field.Value;
        _edit.SetSelection(_edit.Text.Length, 0);
        _edit.Visibility = UiVisibility.Visible;
        IsActive = true;
        return true;
    }

    /// <summary>
    /// Writes the typed text back into the document and stops editing. Returns
    /// <c>true</c> when a value was written.
    /// </summary>
    public bool Commit()
    {
        if (!IsActive)
            return false;

        HtmlContainer? container = _container;
        string text = _edit.Text;
        PointF anchor = _anchor;
        Reset();

        bool written = container?.SetEditableInputValueAtDocumentPoint(anchor, text) == true;
        if (written)
            Committed?.Invoke(this, EventArgs.Empty);

        return written;
    }

    /// <summary>
    /// Commits the typed text and asks the host to submit the enclosing form —
    /// what pressing Enter in a text control does.
    /// </summary>
    public void Submit()
    {
        if (!IsActive)
            return;

        // Commit resets the field identity, so capture it first.
        string id = FieldId;
        string name = FieldName;
        Commit();
        SubmitRequested?.Invoke(this, new HtmlFormFieldSubmitEventArgs(id, name));
    }

    /// <summary>Stops editing and discards the typed text.</summary>
    public void Cancel() => Reset();

    /// <summary>
    /// Places the hosted control over its field for the current viewport transform.
    /// <paramref name="scrollY"/> is the viewport's scroll position in viewport
    /// units, matching the transform the renderer's display list is replayed under.
    /// </summary>
    public void UpdateViewport(BRect viewportBounds, double zoom, double scrollY)
    {
        if (!IsActive)
            return;

        if (zoom <= 0 || viewportBounds.IsEmpty)
        {
            Hide();
            return;
        }

        BRect target = new(
            viewportBounds.Left + (_documentRect.X * zoom),
            viewportBounds.Top + (_documentRect.Y * zoom) - scrollY,
            _documentRect.Width * zoom,
            _documentRect.Height * zoom);

        // A field scrolled out of view must neither paint nor swallow pointer input.
        if (target.Intersect(viewportBounds).IsEmpty)
        {
            Hide();
            return;
        }

        _edit.Visibility = UiVisibility.Visible;
        _edit.Font = new BFontStyle(UaInputFontFamily, UaInputFontSize * zoom);
        _edit.PaddingX = UaInputPaddingX * zoom;
        _edit.PaddingY = UaInputPaddingY * zoom;
        _edit.Measure(target.Size);
        _edit.Arrange(target);
    }

    private void Reset()
    {
        IsActive = false;
        _container = null;
        _documentRect = RectangleF.Empty;
        _anchor = PointF.Empty;
        FieldId = string.Empty;
        FieldName = string.Empty;
        FieldType = string.Empty;
        _edit.Text = string.Empty;
        _edit.IsPassword = false;
        Hide();
    }

    /// <summary>
    /// Takes the overlay out of the tree's visible set. An open context menu goes
    /// with it: a hidden control receives no input, so a menu left open would hold
    /// the session's input capture with nothing able to dismiss it.
    /// </summary>
    private void Hide()
    {
        _edit.CloseContextMenu();
        _edit.Visibility = UiVisibility.Collapsed;
    }
}

/// <summary>
/// Identifies the field a submission was requested from, so the host can locate the
/// enclosing form in the page's document.
/// </summary>
internal sealed class HtmlFormFieldSubmitEventArgs(string fieldId, string fieldName) : EventArgs
{
    public string FieldId { get; } = fieldId ?? string.Empty;

    public string FieldName { get; } = fieldName ?? string.Empty;
}
