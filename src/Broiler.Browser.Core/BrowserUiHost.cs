using Broiler.Graphics;
using Broiler.UI;

namespace Broiler.Browser;

internal sealed class BrowserUiHost : IUiHost, IUiClipboardHost, IUiTextInputHost, IDisposable
{
    private readonly Func<BSize> _getViewportSize;
    private readonly Func<double> _getScale;
    private readonly Action _invalidate;
    private readonly Action<BRenderList> _present;
    private readonly Func<Action, bool> _post;
    private readonly Func<string?>? _getClipboardText;
    private readonly Action<string>? _setClipboardText;
    private readonly Action<UiTextCaretInfo?>? _caretChanged;

    public BrowserUiHost(
        Func<BSize> getViewportSize,
        Func<double> getScale,
        Action invalidate,
        Action<BRenderList> present,
        Func<Action, bool> post,
        Func<string?>? getClipboardText = null,
        Action<string>? setClipboardText = null,
        Action<UiTextCaretInfo?>? caretChanged = null)
    {
        _getViewportSize = getViewportSize ?? throw new ArgumentNullException(nameof(getViewportSize));
        _getScale = getScale ?? throw new ArgumentNullException(nameof(getScale));
        _invalidate = invalidate ?? throw new ArgumentNullException(nameof(invalidate));
        _present = present ?? throw new ArgumentNullException(nameof(present));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _getClipboardText = getClipboardText;
        _setClipboardText = setClipboardText;
        _caretChanged = caretChanged;
    }

    public BSize ViewportSize => _getViewportSize();

    public double Scale => _getScale();

    public bool IsInvalidated { get; private set; } = true;

    public BRenderList? LastRenderList { get; private set; }

    public UiTextCaretInfo? LastCaret { get; private set; }

    public BRenderList CreateRenderList(int capacity = 0) => new(capacity);

    public bool Post(Action action) => _post(action);

    public void RequestInvalidate()
    {
        IsInvalidated = true;
        _invalidate();
    }

    public void Invalidate(UiInvalidation invalidation) => RequestInvalidate();

    public void Present(BRenderList renderList)
    {
        _present(renderList);
        LastRenderList = renderList;
        IsInvalidated = false;
    }

    /// <summary>
    /// The platform clipboard, or none at all.
    ///
    /// There is deliberately no private string standing in when the shell wired
    /// no accessor: a fallback makes copy and paste appear to work while
    /// interoperating with nothing else on the machine — copy in Broiler, paste
    /// in a terminal, and the terminal produces whatever was copied before. With
    /// none, the editing commands report themselves unavailable, which is true.
    /// </summary>
    public bool TryGetText(out string text)
    {
        text = _getClipboardText?.Invoke() ?? string.Empty;
        return text.Length > 0;
    }

    public void SetText(string text) => _setClipboardText?.Invoke(text ?? string.Empty);

    public void PublishCaret(UiTextCaretInfo caret)
    {
        LastCaret = caret;
        _caretChanged?.Invoke(caret);
    }

    public void ClearCaret(UiElement owner)
    {
        if (LastCaret?.Owner == owner)
        {
            LastCaret = null;
            _caretChanged?.Invoke(null);
        }
    }

    public void Dispose()
    {
    }
}
