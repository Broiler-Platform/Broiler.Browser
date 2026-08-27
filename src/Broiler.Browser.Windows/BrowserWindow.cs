using System.Runtime.Versioning;
using Broiler.App;
using Broiler.Graphics;
using Broiler.Graphics.Windows;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Windows;
using Broiler.Input.Mouse;
using Broiler.Input.Mouse.Windows;
using Broiler.Input.Windows;
using Broiler.UI;

namespace Broiler.Browser;

[SupportedOSPlatform("windows7.0")]
internal sealed class BrowserWindow : Direct2DWindow, IWindowsInputHost
{
    private readonly BrowserUiHost _host;
    private readonly BrowserApp _app;
    private readonly WindowsInputMessageDispatcher _inputDispatcher;
    private readonly WindowsKeyboardProvider _keyboardProvider;
    private readonly WindowsMouseProvider _mouseProvider;
    private readonly WindowsKeyboardInputDevice _keyboard;
    private readonly WindowsMouseInputDevice _mouse;
    private readonly WindowsInputMessageSubscription[] _inputSubscriptions;
    private int _hostThreadId = Environment.CurrentManagedThreadId;

    // Created in OnCreated: the Win32 clipboard is addressed by window handle,
    // and there is no handle until the window exists.
    private WindowsClipboard? _clipboard;

    public BrowserWindow(string? initialUrl)
        : base(new BWindowOptions
        {
            Title = "Broiler Browser",
            ClientWidth = 1100,
            ClientHeight = 800,
            ClearColor = BrowserPalette.Canvas,
            RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
        })
    {
        _host = new BrowserUiHost(
            () => ClientSize,
            () => DpiScale,
            Invalidate,
            static _ => { },
            PostToUiThread,
            ReadClipboardText,
            WriteClipboardText);
        _app = new BrowserApp(_host, () => Renderer, initialUrl, SetAnimationActive);
        _inputDispatcher = new WindowsInputMessageDispatcher(this);
        _keyboardProvider = new WindowsKeyboardProvider();
        _mouseProvider = new WindowsMouseProvider();
        _keyboard = OpenDefaultKeyboard(_keyboardProvider);
        _mouse = OpenDefaultMouse(_mouseProvider);
        _inputSubscriptions =
        [
            _inputDispatcher.AddSink(_keyboardProvider),
            _inputDispatcher.AddSink(_mouseProvider),
            _inputDispatcher.AddSink(_keyboard),
            _inputDispatcher.AddSink(_mouse),
        ];

        _keyboard.KeyChanged += OnKeyboardKeyChanged;
        _keyboard.TextInput += OnKeyboardTextInput;
        _mouse.Moved += OnMouseMoved;
        _mouse.ButtonChanged += OnMouseButtonChanged;
        _mouse.WheelChanged += OnMouseWheelChanged;
    }

    public event Action<WindowsInputMessage>? MessageReceived;

    public IntPtr MessageWindowHandle => RenderNativeHandle != IntPtr.Zero ? RenderNativeHandle : NativeHandle;

    public bool IsOnHostThread => Environment.CurrentManagedThreadId == _hostThreadId;

    public bool TryPost(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return PostToUiThread(callback);
    }

    protected override void OnCreated()
    {
        _hostThreadId = Environment.CurrentManagedThreadId;
        _clipboard = new WindowsClipboard(NativeHandle);
    }

    /// <summary>
    /// Null rather than empty when there is no clipboard to read: the host
    /// treats that as "this machine offers none", which is what the window
    /// reports before it exists and if the OS refuses the clipboard.
    /// </summary>
    private string? ReadClipboardText() =>
        _clipboard is not null && _clipboard.TryGetText(out string text) ? text : null;

    private void WriteClipboardText(string text) => _clipboard?.SetText(text);

    protected override BRenderList? BuildRenderList(BSize clientSize) => _app.RenderFrame();

    protected override BFrameContext CreateFrameContext(long frameIndex) =>
        new(_app.ResolveClearColor(), frameIndex, Options.RenderOptions);

    protected override void OnResized(BSize clientSize, double dpiScale) => _app.Invalidate();

    protected override void OnGraphicsResourcesReleasing() => _app.ReleaseGraphicsResources();

    protected override void OnAnimationTick() => _app.StepAnimation();

    protected override void OnNativeWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam) =>
        MessageReceived?.Invoke(new WindowsInputMessage(hwnd, message, wParam, lParam));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keyboard.KeyChanged -= OnKeyboardKeyChanged;
            _keyboard.TextInput -= OnKeyboardTextInput;
            _mouse.Moved -= OnMouseMoved;
            _mouse.ButtonChanged -= OnMouseButtonChanged;
            _mouse.WheelChanged -= OnMouseWheelChanged;

            foreach (WindowsInputMessageSubscription subscription in _inputSubscriptions)
                subscription.Dispose();

            _inputDispatcher.Dispose();
            _keyboard.Dispose();
            _mouse.Dispose();
            _app.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Dispatch(UiInputEvent input) => _app.Dispatch(input);

    private void SetAnimationActive(bool active)
    {
        if (active)
            StartAnimationTimer(16);
        else
            StopAnimationTimer();
    }

    private void OnKeyboardKeyChanged(KeyboardKeyEvent input) =>
        Dispatch(UiInputEvent.FromKeyboardKey(input));

    private void OnKeyboardTextInput(KeyboardTextEvent input) =>
        Dispatch(UiInputEvent.FromKeyboardText(input));

    private void OnMouseMoved(MouseMoveEvent input) =>
        Dispatch(UiInputEvent.FromMouseMove(input with { Position = ToClientDip(input.Position) }));

    private void OnMouseButtonChanged(MouseButtonEvent input) =>
        Dispatch(UiInputEvent.FromMouseButton(input with { Position = ToClientDip(input.Position) }));

    private void OnMouseWheelChanged(MouseWheelEvent input) =>
        Dispatch(UiInputEvent.FromMouseWheel(input with { Position = ToClientDip(input.Position) }));

    private InputPoint ToClientDip(InputPoint point)
    {
        if (string.Equals(point.CoordinateSpace, "client-dip", StringComparison.Ordinal))
            return point;

        if (!string.Equals(point.CoordinateSpace, "client-pixels", StringComparison.Ordinal))
            return point;

        double scale = DpiScale;
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            scale = 1;

        return InputPoint.ClientDeviceIndependentPixels(point.X / scale, point.Y / scale);
    }

    private static WindowsKeyboardInputDevice OpenDefaultKeyboard(WindowsKeyboardProvider provider)
    {
        WindowsKeyboardInputDevice device = provider.OpenDefaultAsync(new KeyboardOpenOptions()).GetAwaiter().GetResult();
        device.StartAsync().GetAwaiter().GetResult();
        return device;
    }

    private static WindowsMouseInputDevice OpenDefaultMouse(WindowsMouseProvider provider)
    {
        WindowsMouseInputDevice device = provider.OpenDefaultAsync(new MouseOpenOptions()).GetAwaiter().GetResult();
        device.StartAsync().GetAwaiter().GetResult();
        return device;
    }
}
