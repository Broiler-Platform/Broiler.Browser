using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Broiler.App.Android;

namespace Broiler.Browser.Android;

[Activity(
    Label = "Broiler Browser",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
        ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode |
        ConfigChanges.Density,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : Activity
{
    private AndroidBroilerView? _view;
    private BrowserUiHost? _host;
    private BrowserApp? _app;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetSoftInputMode(SoftInput.AdjustResize);

        _view = new AndroidBroilerView(this, BrowserPalette.Canvas);
        _host = new BrowserUiHost(
            () => _view.ViewportSize,
            () => _view.Scale,
            _view.InvalidateFrame,
            _view.Present,
            _view.PostToUiThread,
            _view.GetClipboardText,
            _view.SetClipboardText,
            _view.NotifyCaretChanged);

        string initialUrl = Intent?.DataString is string url && url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
            ? url
            : "about:blank";
        _app = new BrowserApp(
            _host,
            () => _view.Renderer,
            initialUrl,
            active =>
            {
                _view.AnimationActive = active;
                _view.InvalidateFrame();
            });

        _view.RenderFrame = _app.RenderFrame;
        _view.DispatchInput = _app.Dispatch;
        _view.GetSession = () => _app.Session;
        _view.StepAnimation = _app.StepAnimation;
        _view.ReleaseGraphicsResources = _app.ReleaseGraphicsResources;
        var content = new AndroidInsetLayout(this);
        content.AddView(_view, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));
        SetContentView(content);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _view?.SetResumed(true);
    }

    protected override void OnPause()
    {
        _view?.SetResumed(false);
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _view?.Dispose();
        _app?.Dispose();
        _host?.Dispose();
        _app = null;
        _host = null;
        _view = null;
        base.OnDestroy();
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is not null && _view?.ProcessKeyEvent(e) == true)
            return true;
        return base.DispatchKeyEvent(e);
    }

    public override void OnBackPressed()
    {
        if (_app?.TryGoBack() == true)
        {
            _view?.InvalidateFrame();
            return;
        }

        FinishAfterTransition();
    }
}
