using System.Diagnostics;
using System.Drawing;
using Broiler.App;
using Broiler.App.Rendering;
using Broiler.Graphics;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Graphics;
using Broiler.HtmlBridge;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Input.Touch;
using Broiler.Layout.Net;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.FileDialog;
using Broiler.UI.FileDialog.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Window.Standard;
using HtmlContainer = Broiler.HTML.Image.HtmlContainer;

namespace Broiler.Browser;

internal sealed class BrowserApp : IDisposable
{
    private const double AnimationIntervalMs = 16;

    private readonly BrowserUiHost _host;
    private readonly Func<IBroilerRenderer?> _getRenderer;
    private readonly Action<bool> _setAnimationActive;
    private readonly UiSession _session;
    private readonly FavoritesManager _favorites = new();
    private readonly List<PageRequest> _history = [];
    private readonly StandardButton _backButton;
    private readonly StandardButton _forwardButton;
    private readonly StandardButton _refreshButton;
    private readonly StandardButton _stopButton;
    private readonly StandardButton _goButton;
    private readonly StandardButton _starButton;
    private readonly StandardEdit _address;
    private readonly StandardLabel _status;
    private readonly BrowserViewport _viewport;
    private readonly BrowserContent _content;
    private readonly StandardWindow _rootWindow;
    private int _historyIndex = -1;
    private bool _isPageBusy;
    private bool _isShuttingDown;
    private long _navigationGeneration;
    private CancellationTokenSource? _navigationCancellation;
    // The load whose intermediate document is on screen but not yet painted; RenderFrame releases it.
    private LoadProgress? _progressAwaitingPaint;

    public BrowserApp(
        BrowserUiHost host,
        Func<IBroilerRenderer?> getRenderer,
        string? initialUrl,
        Action<bool> setAnimationActive)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _getRenderer = getRenderer ?? throw new ArgumentNullException(nameof(getRenderer));
        _setAnimationActive = setAnimationActive ?? throw new ArgumentNullException(nameof(setAnimationActive));
        _session = new StandardUiSessionBuilder()
            .WithDispatcher(new ImmediateUiDispatcher())
            .Build(_host);

        _backButton = CreateChromeButton("<", "Back");
        _forwardButton = CreateChromeButton(">", "Forward");
        _refreshButton = CreateChromeButton("Reload", "Reload");
        _stopButton = CreateChromeButton("Stop", "Stop");
        _goButton = CreateChromeButton("Go", "Go");
        _starButton = CreateChromeButton("*", "Favorite");
        _address = new StandardEdit
        {
            PreferredSize = new BSize(420, 28),
            PlaceholderText = "about:blank or https://example.com",
            Font = new BFontStyle("Segoe UI", 14),
            Background = BrowserPalette.Surface,
            BorderColor = BrowserPalette.Border,
            FocusRing = BrowserPalette.Accent,
            PaddingX = 10,
            PaddingY = 5,
        };
        _status = new StandardLabel
        {
            Text = "Ready",
            Font = new BFontStyle("Segoe UI", 13),
            Foreground = BrowserPalette.Muted,
            Trimming = UiTextTrimming.CharacterEllipsis,
        };
        _viewport = new BrowserViewport(_getRenderer);
        _content = new BrowserContent(
            _backButton,
            _forwardButton,
            _refreshButton,
            _stopButton,
            _address,
            _starButton,
            _goButton,
            _viewport,
            _status);

        _rootWindow = new StandardWindow
        {
            Title = "Broiler Browser",
            Background = BrowserPalette.Canvas,
            BorderColor = BrowserPalette.Border,
            ActiveBorderColor = BrowserPalette.Accent,
            BorderThickness = 1,
        };
        _rootWindow.AddChild(_content);
        _session.AddRoot(_rootWindow);

        _backButton.Clicked += (_, _) => GoHistory(-1);
        _forwardButton.Clicked += (_, _) => GoHistory(1);
        _refreshButton.Clicked += (_, _) => Reload();
        _stopButton.Clicked += (_, _) => StopLoading();
        _goButton.Clicked += (_, _) => NavigateTo(_address.Text);
        _starButton.Clicked += (_, _) => ToggleFavorite();
        _address.Submitted += (_, _) => NavigateTo(_address.Text);
        _viewport.LinkActivated += OnViewportLinkActivated;
        _viewport.FilePickRequested += OnViewportFilePickRequested;

        _favorites.Load();
        RefreshFavoritesBar();
        UpdateNavigationButtons();
        SetBusy(false);
        _session.SetFocus(_address);
        NavigateTo(initialUrl ?? "about:blank");
    }

    public UiSession Session => _session;

    public bool HasPendingWork => _viewport.HasPendingWork;

    public bool IsBusy => _isPageBusy;

    public string Status => _status.Text;

    /// <summary>
    /// Paints a frame, and lets a load in flight publish its next one.
    /// </summary>
    /// <remarks>
    /// The load worker holds one intermediate document at a time (see <see cref="LoadProgress"/>),
    /// and this is where that hold is released — after the frame it produced has actually been
    /// painted, not merely queued. Pacing the settle on the paint is what keeps the two ends
    /// honest: a page whose document lays out in milliseconds gets a frame per batch and animates,
    /// while one that costs a second a frame gets the next batch only when the last is on screen,
    /// so the UI thread is never handed work faster than it can finish.
    /// </remarks>
    public BRenderList RenderFrame()
    {
        BRenderList frame = _session.RenderFrame();

        LoadProgress? painted = _progressAwaitingPaint;
        if (painted is not null)
        {
            _progressAwaitingPaint = null;
            painted.FramePainted();
        }

        return frame;
    }

    public void Dispatch(UiInputEvent input)
    {
        if (HandleGlobalShortcut(input))
        {
            _host.RequestInvalidate();
            return;
        }

        if (_session.DispatchInput(input))
            _host.RequestInvalidate();
    }

    public void Invalidate() => _host.RequestInvalidate();

    public void ReleaseGraphicsResources()
    {
        _viewport.ReleaseGraphicsResources();
        _host.RequestInvalidate();
    }

    public bool TryGoBack()
    {
        if (_historyIndex <= 0)
            return false;

        GoHistory(-1);
        return true;
    }

    public BColor ResolveClearColor() => BrowserPalette.Canvas;

    public void StepAnimation()
    {
        if (_isShuttingDown)
            return;

        if (!_viewport.HasPendingWork)
        {
            SetBusy(false);
            _setAnimationActive(false);
            return;
        }

        if (_viewport.StepAnimation())
            _host.RequestInvalidate();

        if (!_viewport.HasPendingWork)
        {
            SetBusy(false);
            SetStatus("Done");
            _setAnimationActive(false);
        }
    }

    public void Dispose()
    {
        BeginShutdown();
        _viewport.LinkActivated -= OnViewportLinkActivated;
        _viewport.FilePickRequested -= OnViewportFilePickRequested;
        _session.Dispose();
    }

    /// <summary>
    /// Prepares fetched or script-produced HTML for the rendering surface: the shared
    /// replaced-element passes, plus synthetic ids on checkbox, radio and select controls so
    /// Broiler.UI controls can be hosted over them (their geometry is only reachable
    /// by id). Applies to the renderer's copy only — scripts run on the original.
    /// </summary>
    private static string PrepareForBrowsing(string html) =>
        HtmlPostProcessor.StampFormControlIds(HtmlPostProcessor.ProcessForBrowsing(html));

    private static StandardButton CreateChromeButton(string text, string semanticName) =>
        new()
        {
            Text = text,
            PreferredSize = new BSize(semanticName.Length <= 7 ? 38 : 64, 28),
            Font = new BFontStyle("Segoe UI", 13, BFontWeight.SemiBold),
            Background = BrowserPalette.Surface,
            BorderColor = BrowserPalette.Border,
            Foreground = BrowserPalette.Text,
            HoverBackground = BrowserPalette.AccentSoft,
            PressedBackground = BrowserPalette.Accent,
            CornerRadius = 5,
            PaddingX = 10,
            PaddingY = 5,
        };

    private bool HandleGlobalShortcut(UiInputEvent input)
    {
        if (input.Kind != UiInputEventKind.KeyboardKey ||
            input.KeyTransition != KeyboardKeyTransition.Down)
        {
            return false;
        }

        if (IsKey(input, BVirtualKey.F5, "F5"))
        {
            Reload();
            return true;
        }

        if (input.KeyModifiers.HasFlag(KeyboardModifierState.Alt))
        {
            if (IsKey(input, BVirtualKey.Left, "Left"))
            {
                GoHistory(-1);
                return true;
            }

            if (IsKey(input, BVirtualKey.Right, "Right"))
            {
                GoHistory(1);
                return true;
            }
        }

        return false;
    }

    private void NavigateTo(string url) => NavigateTo(PageRequest.ForUrl(url));

    private void NavigateTo(PageRequest request)
    {
        if (_isShuttingDown || string.IsNullOrWhiteSpace(request.Url))
            return;

        request = request with { Url = NormalizeInput(request.Url) };
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        // History holds the whole request, so revisiting a POST can re-issue it —
        // behind a confirmation, since repeating a submission is not free.
        _history.Add(request);
        _historyIndex = _history.Count - 1;
        LoadUrl(request);
    }

    private void GoHistory(int delta)
    {
        if (_isShuttingDown)
            return;

        int target = _historyIndex + delta;
        if (target < 0 || target >= _history.Count)
            return;

        PageRequest request = _history[target];
        if (!request.IsRepeatable)
        {
            // Re-issuing a submission can charge a card twice. Ask first, and only
            // move the history cursor if the user agrees.
            ConfirmResubmission(() =>
            {
                _historyIndex = target;
                LoadUrl(request);
            });
            return;
        }

        _historyIndex = target;
        LoadUrl(request);
    }

    private void Reload()
    {
        if (_isShuttingDown)
            return;

        if (_historyIndex < 0 || _historyIndex >= _history.Count)
            return;

        PageRequest request = _history[_historyIndex];
        if (request.IsRepeatable)
        {
            LoadUrl(request);
            return;
        }

        ConfirmResubmission(() => LoadUrl(request));
    }

    /// <summary>
    /// Asks before repeating a form submission, and runs <paramref name="resubmit"/>
    /// only if the user agrees. A POST is not safe to replay on its own — reloading a
    /// checkout would place the order twice — so revisiting one goes through here.
    /// </summary>
    private void ConfirmResubmission(Action resubmit)
    {
        StandardDialog dialog = new()
        {
            Title = "Confirm resubmission",
            PreferredSize = ResubmitDialogSize,
        };

        StandardLabel message = new()
        {
            Text = "This page was the result of a form submission. Sending it again may repeat the action.",
            Font = new BFontStyle("Segoe UI", 13),
            Foreground = BrowserPalette.Text,
            Trimming = UiTextTrimming.None,
        };
        StandardButton resend = CreateChromeButton("Resend", "Resend");
        StandardButton cancel = CreateChromeButton("Cancel", "Cancel");

        resend.Clicked += (_, _) => dialog.Accept();
        cancel.Clicked += (_, _) => dialog.Cancel();

        dialog.AddChild(new ResubmitPrompt(message, resend, cancel));
        dialog.ResultCompleted += (_, e) =>
        {
            if (e.Result.Kind == UiDialogResultKind.Accepted)
                resubmit();

            _host.RequestInvalidate();
        };

        dialog.ShowModal(_rootWindow, GetDialogPlacement(ResubmitDialogSize));
        _host.RequestInvalidate();
    }

    private static readonly BSize ResubmitDialogSize = new(420, 170);

    private void StopLoading()
    {
        if (_isShuttingDown || !_isPageBusy)
            return;

        CancelPendingNavigation();
        _viewport.StopSession();
        _navigationGeneration++;
        _setAnimationActive(false);
        SetBusy(false);
        SetStatus("Stopped");
        _host.RequestInvalidate();
    }

    private void LoadUrl(string url) => LoadUrl(PageRequest.ForUrl(url));

    private void LoadUrl(PageRequest request)
    {
        if (_isShuttingDown)
            return;

        string url = request.Url;
        long navigationGeneration = BeginNavigation();
        SetUrlText(url);
        UpdateNavigationButtons();
        UpdateStarButton();

        if (string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            _viewport.ReplacePage(BrowserViewport.CreateContentContainer(WelcomePage, string.Empty), null, string.Empty);
            SetBusy(false);
            SetStatus("Ready");
            _host.RequestInvalidate();
            return;
        }

        ShowLoadingPage(url);

        var cancellation = new CancellationTokenSource();
        _navigationCancellation = cancellation;

        // Task.Run, not a bare call: an async method runs on the calling thread until it first
        // suspends, and the load only suspends if the fetch does. A file:// navigation reads the
        // page without ever yielding, so calling it here ran the fetch, the scripts and the whole
        // load-window settle on the UI thread — the freeze the settle was moved off it to avoid,
        // reappearing for local pages, and with it the intermediate frames, since the thread that
        // was supposed to paint them was the one doing the settling.
        _ = Task.Run(
            () => LoadUrlInBackgroundAsync(navigationGeneration, request, new LoadProgress(this, navigationGeneration), cancellation),
            CancellationToken.None);
    }

    private long BeginNavigation()
    {
        CancelPendingNavigation();
        _viewport.StopSession();
        _setAnimationActive(false);
        return ++_navigationGeneration;
    }

    private async Task LoadUrlInBackgroundAsync(
        long navigationGeneration,
        PageRequest request,
        LoadProgress progress,
        CancellationTokenSource cancellation)
    {
        NavigationLoadResult? result = null;
        try
        {
            result = await LoadUrlOnWorkerAsync(request, progress, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            result = NavigationLoadResult.FromError(ex);
        }

        if (!_host.Post(() => CompleteBackgroundLoad(navigationGeneration, cancellation, result)))
        {
            result?.Dispose();
            cancellation.Dispose();
        }
    }

    // One connection pool for the whole browser session rather than one per navigation.
    // An HttpClient *is* a connection pool, so a per-navigation client reconnects to a host
    // the previous page already had a keep-alive connection to, and — because the pipeline's
    // `using` disposed it at the end of the load — tore that pool down again immediately.
    // Disposing a pool closes its pooled connections, and any connection the pool's scavenger
    // has already armed with a zero-byte read-ahead fails that pending read with
    // SocketError.OperationAborted, reported as `IOException: Unable to read data from the
    // transport connection` (Windows spells 995 as "the I/O operation has been aborted because
    // of either a thread exit or an application request"). A browser window is long-lived, so
    // unlike the CLI's one-shot capture it stays alive to see it. See
    // docs/browser-connection-pool-aborts.md.
    //
    // Never disposed: it is owned by the process, and outliving every navigation is the point.
    private static readonly HttpClient PageHttpClient = CreatePageHttpClient();

    internal static HttpClient CreatePageHttpClient()
    {
        SocketsHttpHandler handler = new()
        {
            // A browser window can stay open for days; recycling a pooled connection
            // periodically keeps it from pinning a DNS answer for that long.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),

            // A host that never completes the handshake otherwise holds the navigation for
            // the whole request timeout with nothing to show for it.
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };

        // Without this the client sends no User-Agent at all, and a server whose policy rejects an
        // unidentified request answers the navigation itself — mediawiki.org replies 403 Forbidden
        // before the first byte of the page. See Broiler.Layout.Net.BroilerUserAgent.
        return BroilerUserAgent.Apply(new HttpClient(handler));
    }

    private static async Task<NavigationLoadResult> LoadUrlOnWorkerAsync(PageRequest request, LoadProgress progress, CancellationToken cancellationToken)
    {
        using var pipeline = new RenderingPipeline(
            new PageLoader(PageHttpClient),
            new ScriptEngine());

        var (normalisedUrl, content) = await pipeline.LoadPageAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        string html = PrepareForBrowsing(content.Html);
        InteractiveSession? session = null;
        try
        {
            session = pipeline.ExecuteScriptsInteractive(content);
            cancellationToken.ThrowIfCancellationRequested();

            if (session is not null)
            {
                // Settle the load window here, on the load worker. ExecuteScriptsInteractive drains
                // only microtasks, so without this every timer the page scheduled during load is
                // left for the viewport to step from the UI thread — inside the WndProc, one
                // callback batch per animation tick, and a batch of a page like google.com is
                // measured in seconds. That is the freeze; the CLI never had it because its drain
                // runs bounded and off any message pump. See docs/browser-load-window-pump.md.
                //
                // The settle reports each batch to `progress`, which paints what it can keep up
                // with. Settling silently is what made a page that animates while loading arrive
                // already finished: Acid3 advances its score one test per setTimeout, so the whole
                // count ran here, before the first paint, and the browser showed only the total.
                string initial = session.SettleLoadWindow(
                    serialize => progress.PublishFrame(serialize, normalisedUrl),
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(initial))
                    html = PrepareForBrowsing(initial);

                // Same bounded question the viewport pumps on: a page whose only remaining work is
                // an interval's later ticks is finished loading, and carrying its session forward
                // would hand the viewport a live JS context it is never going to step.
                if (!session.HasWorkDueInLoadWindow)
                {
                    session.Dispose();
                    session = null;
                }
            }

            HtmlContainer container = BrowserViewport.CreateContentContainer(html, normalisedUrl);
            return NavigationLoadResult.FromSuccess(normalisedUrl, container, session);
        }
        catch
        {
            session?.Dispose();
            throw;
        }
    }

    private void CompleteBackgroundLoad(
        long navigationGeneration,
        CancellationTokenSource cancellation,
        NavigationLoadResult? result)
    {
        try
        {
            if (ReferenceEquals(_navigationCancellation, cancellation))
                _navigationCancellation = null;

            if (_isShuttingDown ||
                cancellation.IsCancellationRequested ||
                navigationGeneration != _navigationGeneration)
            {
                result?.Dispose();
                return;
            }

            if (result is null)
                return;

            if (result.Error is { } error)
            {
                ShowErrorPage(error);
                return;
            }

            ApplyLoadedPage(result);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Publishes the load window's intermediate documents from the load worker to the UI thread,
    /// one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The settle runs off the UI thread precisely so the page's callbacks are not paid inside the
    /// message pump (<c>docs/browser-load-window-pump.md</c>), and that must stay true: this class
    /// does the parse — <see cref="BrowserViewport.CreateContentContainer"/> is the expensive half —
    /// on the worker as well, and posts the finished container across. The UI thread swaps it in
    /// and lays it out; it runs no script.
    /// </para>
    /// <para>
    /// One frame is in flight at a time, released only once it has been painted
    /// (<see cref="BrowserApp.RenderFrame"/>). That self-paces: the settle is never further ahead
    /// of the screen than one frame, so a cheap page animates and an expensive one simply publishes
    /// fewer frames instead of queueing work the UI thread cannot keep up with. A frame the host
    /// refuses to post, or one that arrives for a navigation that has been superseded, releases the
    /// hold too — a dropped frame must not stop the settle from reporting the next one.
    /// </para>
    /// </remarks>
    private sealed class LoadProgress(BrowserApp app, long navigationGeneration)
    {
        /// <summary>
        /// The share of the settle's own running time that may go on producing frames.
        /// </summary>
        /// <remarks>
        /// A frame costs a serialise and a full parse, and a parse re-fetches the document's
        /// stylesheets and web fonts every time (<c>docs/browser-load-window-pump.md</c>, "what this
        /// does not fix"), which on a page carrying several is seconds. Paying that per batch took
        /// mediawiki.org from 33 s to 86 s — the page arrived sooner but finished much later.
        /// Holding frame work to a quarter of the settle bounds the whole cost at a third: a
        /// document that is cheap to parse still gets a frame per batch and animates, while an
        /// expensive one buys a few frames instead of a hundred.
        /// </remarks>
        private const double FrameWorkBudget = 0.25;

        private const int Idle = 0;
        private const int InFlight = 1;

        // Only _state crosses threads; the rest belong to the settling thread, which is the sole
        // caller of PublishFrame.
        private int _state = Idle;
        private readonly Stopwatch _settling = new();
        private TimeSpan _frameWork;
        private string? _lastPublishedHtml;

        /// <summary>
        /// Offers the document reached after one batch of the load window. Called on the load
        /// worker; returns without serialising when the previous frame has not been painted yet or
        /// when frames have used up their share of the settle.
        /// </summary>
        public void PublishFrame(Func<string> serialize, string url)
        {
            if (Interlocked.CompareExchange(ref _state, InFlight, Idle) != Idle)
                return;

            // The first frame is what replaces "Loading..." with the page, so it is always worth
            // its cost; the budget governs the ones after it.
            if (!_settling.IsRunning)
            {
                _settling.Start();
            }
            else if (_frameWork > _settling.Elapsed * FrameWorkBudget)
            {
                Volatile.Write(ref _state, Idle);
                return;
            }

            TimeSpan startedAt = _settling.Elapsed;
            HtmlContainer container;
            try
            {
                string html = serialize();

                // A batch that ran callbacks without touching the DOM — a timer that only reads,
                // measures or reschedules, which busy pages run many of — leaves the document
                // exactly as the last frame had it, and re-parsing it would buy nothing.
                if (string.IsNullOrWhiteSpace(html) || string.Equals(html, _lastPublishedHtml, StringComparison.Ordinal))
                {
                    Volatile.Write(ref _state, Idle);
                    return;
                }

                _lastPublishedHtml = html;
                container = BrowserViewport.CreateContentContainer(PrepareForBrowsing(html), url);
            }
            catch
            {
                // An intermediate frame is a courtesy; a page whose half-built document does not
                // serialise or parse must still finish loading.
                Volatile.Write(ref _state, Idle);
                return;
            }
            finally
            {
                _frameWork += _settling.Elapsed - startedAt;
            }

            if (!app._host.Post(() => app.ApplyLoadProgress(navigationGeneration, container, url, this)))
            {
                container.Dispose();
                Volatile.Write(ref _state, Idle);
            }
        }

        /// <summary>Releases the hold once the published frame has been painted.</summary>
        public void FramePainted() => Volatile.Write(ref _state, Idle);
    }

    /// <summary>
    /// Shows one of a load's intermediate documents. Runs on the UI thread; the container was
    /// already parsed on the load worker, so this is a swap and a layout, never script.
    /// </summary>
    private void ApplyLoadProgress(long navigationGeneration, HtmlContainer container, string url, LoadProgress progress)
    {
        if (_isShuttingDown || navigationGeneration != _navigationGeneration)
        {
            container.Dispose();
            progress.FramePainted();
            return;
        }

        // No session: the settle owns the page's JavaScript until it finishes, and handing the
        // viewport one here would let the animation tick step a context the worker is running.
        _viewport.ReplacePage(container, null, url);

        // A frame from a load that has since been superseded may still be waiting on a paint that
        // is never coming; release it rather than leaving that settle held forever.
        _progressAwaitingPaint?.FramePainted();
        _progressAwaitingPaint = progress;
        _host.RequestInvalidate();
    }

    private void ApplyLoadedPage(NavigationLoadResult result)
    {
        SetUrlText(result.NormalisedUrl);
        _viewport.ReplacePage(result.TakeContainer(), result.TakeSession(), result.NormalisedUrl);

        if (_viewport.HasPendingWork)
        {
            SetBusy(true);
            SetStatus("Rendering...");
            _setAnimationActive(true);
        }
        else
        {
            SetBusy(false);
            SetStatus("Done");
        }

        _host.RequestInvalidate();
    }

    private void ShowLoadingPage(string url)
    {
        SetBusy(true);
        SetStatus("Loading " + url + "...");
        _viewport.ReplacePage(BrowserViewport.CreateContentContainer($"""
<html>
<body style='font-family: Segoe UI, Arial, sans-serif; margin: 40px; color: #333;'>
    <p>Loading {System.Net.WebUtility.HtmlEncode(url)}...</p>
</body>
</html>
""", string.Empty), null, string.Empty);
        _host.RequestInvalidate();
    }

    private void ShowErrorPage(Exception ex)
    {
        SetBusy(false);
        SetStatus("Error loading page");
        _viewport.ReplacePage(BrowserViewport.CreateContentContainer(
            "<html><body><h1>Error</h1><p>" + System.Net.WebUtility.HtmlEncode(ex.Message) + "</p></body></html>",
            string.Empty),
            null,
            string.Empty);
        _host.RequestInvalidate();
    }

    /// <summary>
    /// Opens a file dialog for a page's <c>&lt;input type="file"&gt;</c>. The viewport
    /// hosts the control but has no window to parent a modal to, so the shell does
    /// this half and hands the chosen path back.
    /// </summary>
    private void OnViewportFilePickRequested(object? sender, HtmlFilePickEventArgs e)
    {
        if (_isShuttingDown)
            return;

        StandardFileDialog dialog = new()
        {
            Mode = UiFileDialogMode.Open,
            CurrentDirectory = Environment.CurrentDirectory,
            PreferredSize = FileDialogPreferredSize,
            Title = e.AllowsMultiple ? "Add a file" : "Choose a file",
        };

        dialog.ResultCompleted += (_, result) =>
        {
            if (result.Result.Kind == UiDialogResultKind.Accepted &&
                !string.IsNullOrWhiteSpace(result.Result.Value))
            {
                _viewport.RecordPickedFile(e.ControlId, e.ControlName, result.Result.Value, e.AllowsMultiple);
            }

            _host.RequestInvalidate();
        };

        dialog.ShowOpenModal(_rootWindow, GetDialogPlacement(FileDialogPreferredSize));
        _host.RequestInvalidate();
    }

    private static readonly BSize FileDialogPreferredSize = new(560, 380);

    private BRect GetDialogPlacement(BSize preferred)
    {
        BSize viewport = _host.ViewportSize;
        double width = Math.Min(preferred.Width, Math.Max(280, viewport.Width - 24));
        double height = Math.Min(preferred.Height, Math.Max(160, viewport.Height - 64));
        return new BRect(
            Math.Max(12, (viewport.Width - width) / 2),
            Math.Max(42, (viewport.Height - height) / 2),
            width,
            height);
    }

    private void OnViewportLinkActivated(object? sender, BrowserLinkEventArgs e)
    {
        if (_isShuttingDown)
            return;

        // Only the URL is resolved against the page; a submission's body travels as-is.
        NavigateTo(e.Request with { Url = ResolveLinkUrl(e.Link) });
    }

    private string ResolveLinkUrl(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return link;

        link = link.Trim();
        if (link.StartsWith('#'))
        {
            string current = CurrentHistoryUrl();
            if (!string.IsNullOrWhiteSpace(current))
            {
                int hash = current.IndexOf('#');
                if (hash >= 0)
                    current = current[..hash];
                return current + link;
            }
        }

        if (Uri.TryCreate(link, UriKind.Absolute, out _))
            return link;

        string baseUrl = !string.IsNullOrWhiteSpace(_viewport.BaseUrl)
            ? _viewport.BaseUrl
            : CurrentHistoryUrl();

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri)
            && Uri.TryCreate(baseUri, link, out Uri? resolved))
        {
            return resolved.AbsoluteUri;
        }

        return link;
    }

    private string CurrentHistoryUrl() =>
        _historyIndex >= 0 && _historyIndex < _history.Count
            ? _history[_historyIndex].Url
            : string.Empty;

    private void ToggleFavorite()
    {
        string url = _address.Text;
        if (string.IsNullOrWhiteSpace(url) || string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
            return;

        if (_favorites.Contains(url))
            _favorites.Remove(url);
        else
            _favorites.Add(url);

        _favorites.Save();
        UpdateStarButton();
        RefreshFavoritesBar();
        _host.RequestInvalidate();
    }

    private void RefreshFavoritesBar()
    {
        var buttons = new List<StandardButton>();
        foreach (string url in _favorites.Favorites)
        {
            string favUrl = url;
            StandardButton button = CreateChromeButton(FavoriteLabel(url), "Favorite");
            button.PreferredSize = new BSize(EstimateFavoriteWidth(button.Text), 28);
            button.Clicked += (_, _) => NavigateTo(favUrl);
            buttons.Add(button);
        }

        _content.ReplaceFavorites(buttons);
    }

    private void UpdateNavigationButtons()
    {
        _backButton.IsEnabled = _historyIndex > 0;
        _forwardButton.IsEnabled = _historyIndex < _history.Count - 1;
    }

    private void UpdateStarButton() =>
        _starButton.Text = _favorites.Contains(_address.Text) ? "Saved" : "*";

    private void SetUrlText(string url)
    {
        if (!string.Equals(_address.Text, url, StringComparison.Ordinal))
            _address.Text = url;
        UpdateStarButton();
    }

    private void SetStatus(string status)
    {
        if (!string.Equals(_status.Text, status, StringComparison.Ordinal))
            _status.Text = status;
    }

    private void SetBusy(bool busy)
    {
        _isPageBusy = busy;
        _stopButton.IsEnabled = busy;
    }

    private void CancelPendingNavigation()
    {
        CancellationTokenSource? cancellation = _navigationCancellation;
        _navigationCancellation = null;
        cancellation?.Cancel();
    }

    private void BeginShutdown()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        SetBusy(false);
        CancelPendingNavigation();
        _viewport.StopSession();
        _setAnimationActive(false);
    }

    private static bool IsKey(UiInputEvent input, int nativeKeyCode, string name) =>
        input.NativeKeyCode == nativeKeyCode ||
        string.Equals(input.KeyName, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(input.KeyName, "VirtualKey:" + nativeKeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string NormalizeInput(string input)
    {
        input = input.Trim();
        if (File.Exists(input))
            return new Uri(Path.GetFullPath(input)).AbsoluteUri;
        return input;
    }

    private static double EstimateFavoriteWidth(string label) =>
        Math.Clamp(label.Length * 8 + 18, 52, 170);

    private static string FavoriteLabel(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.Host))
        {
            string host = uri.Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host[4..];
            return host;
        }

        return url.Length > 24 ? url[..21] + "..." : url;
    }

    private sealed class NavigationLoadResult : IDisposable
    {
        private HtmlContainer? _container;
        private InteractiveSession? _session;

        private NavigationLoadResult(
            string normalisedUrl,
            HtmlContainer? container,
            InteractiveSession? session,
            Exception? error)
        {
            NormalisedUrl = normalisedUrl;
            _container = container;
            _session = session;
            Error = error;
        }

        public string NormalisedUrl { get; }

        public Exception? Error { get; }

        public static NavigationLoadResult FromSuccess(
            string normalisedUrl,
            HtmlContainer container,
            InteractiveSession? session) =>
            new(normalisedUrl, container, session, null);

        public static NavigationLoadResult FromError(Exception error) =>
            new(string.Empty, null, null, error);

        public HtmlContainer TakeContainer()
        {
            HtmlContainer container = _container ?? throw new InvalidOperationException("Navigation result has no container.");
            _container = null;
            return container;
        }

        public InteractiveSession? TakeSession()
        {
            InteractiveSession? session = _session;
            _session = null;
            return session;
        }

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
            _container?.Dispose();
            _container = null;
        }
    }

    private sealed class BrowserContent : UiElement
    {
        private const double ToolbarHeight = 42;
        private const double FavoritesBarHeight = 30;
        private const double StatusBarHeight = 24;
        private const double Margin = 8;
        private const double ControlHeight = 28;
        private const double NavButtonWidth = 38;
        private const double StopButtonWidth = 58;
        private const double GoButtonWidth = 54;
        private const double StarWidth = 62;
        private const double MinWidth = 720;
        private const double MinHeight = 480;

        private readonly StandardButton _backButton;
        private readonly StandardButton _forwardButton;
        private readonly StandardButton _refreshButton;
        private readonly StandardButton _stopButton;
        private readonly StandardEdit _address;
        private readonly StandardButton _starButton;
        private readonly StandardButton _goButton;
        private readonly BrowserViewport _viewport;
        private readonly StandardLabel _status;
        private readonly List<StandardButton> _favorites = [];
        private bool _isCompact;

        public BrowserContent(
            StandardButton backButton,
            StandardButton forwardButton,
            StandardButton refreshButton,
            StandardButton stopButton,
            StandardEdit address,
            StandardButton starButton,
            StandardButton goButton,
            BrowserViewport viewport,
            StandardLabel status)
        {
            _backButton = backButton;
            _forwardButton = forwardButton;
            _refreshButton = refreshButton;
            _stopButton = stopButton;
            _address = address;
            _starButton = starButton;
            _goButton = goButton;
            _viewport = viewport;
            _status = status;

            AddChild(_backButton);
            AddChild(_forwardButton);
            AddChild(_refreshButton);
            AddChild(_stopButton);
            AddChild(_address);
            AddChild(_starButton);
            AddChild(_goButton);
            AddChild(_viewport);
            AddChild(_status);
        }

        public void ReplaceFavorites(IEnumerable<StandardButton> buttons)
        {
            foreach (StandardButton button in _favorites.ToArray())
            {
                RemoveChild(button);
                button.Dispose();
            }

            _favorites.Clear();
            foreach (StandardButton button in buttons)
            {
                _favorites.Add(button);
                AddChild(button);
            }

            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        }

        protected override BSize MeasureCore(BSize availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? MinWidth : Math.Max(0, availableSize.Width);
            double height = double.IsInfinity(availableSize.Height) ? MinHeight : Math.Max(0, availableSize.Height);
            double addressWidth = Math.Max(90, width - 4 * NavButtonWidth - StopButtonWidth - StarWidth - GoButtonWidth - 9 * Margin);
            BSize controlSize = new(double.PositiveInfinity, ControlHeight);

            _backButton.Measure(new BSize(NavButtonWidth, ControlHeight));
            _forwardButton.Measure(new BSize(NavButtonWidth, ControlHeight));
            _refreshButton.Measure(new BSize(NavButtonWidth + 24, ControlHeight));
            _stopButton.Measure(new BSize(StopButtonWidth, ControlHeight));
            _address.Measure(new BSize(addressWidth, ControlHeight));
            _starButton.Measure(new BSize(StarWidth, ControlHeight));
            _goButton.Measure(new BSize(GoButtonWidth, ControlHeight));
            foreach (StandardButton button in _favorites)
                button.Measure(controlSize);

            double viewportHeight = Math.Max(120, height - ToolbarHeight - FavoritesBarHeight - StatusBarHeight);
            _viewport.Measure(new BSize(width, viewportHeight));
            _status.Measure(new BSize(Math.Max(0, width - 2 * Margin), StatusBarHeight));
            return new BSize(width, height);
        }

        protected override void ArrangeCore(BRect finalRect)
        {
            bool compact = finalRect.Width < 600;
            _isCompact = compact;
            _forwardButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;
            _refreshButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;
            _stopButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;
            _starButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;

            double x = finalRect.Left + Margin;
            double y = finalRect.Top + (ToolbarHeight - ControlHeight) / 2;

            double navWidth = compact ? 44 : NavButtonWidth;
            double controlHeight = compact ? 36 : ControlHeight;
            y = finalRect.Top + (ToolbarHeight - controlHeight) / 2;
            _backButton.Arrange(new BRect(x, y, navWidth, controlHeight));
            x += navWidth + Margin;
            if (!compact)
            {
                _forwardButton.Arrange(new BRect(x, y, NavButtonWidth, ControlHeight));
                x += NavButtonWidth + Margin;
                _refreshButton.Arrange(new BRect(x, y, NavButtonWidth + 24, ControlHeight));
                x += NavButtonWidth + 24 + Margin;
                _stopButton.Arrange(new BRect(x, y, StopButtonWidth, ControlHeight));
                x += StopButtonWidth + Margin;
            }

            double rightControls = (compact ? 0 : StarWidth + Margin) + GoButtonWidth + Margin;
            double addressWidth = Math.Max(90, finalRect.Right - Margin - x - rightControls);
            _address.Arrange(new BRect(x, y, addressWidth, controlHeight));
            x += addressWidth + Margin;
            if (!compact)
            {
                _starButton.Arrange(new BRect(x, y, StarWidth, ControlHeight));
                x += StarWidth + Margin;
            }
            _goButton.Arrange(new BRect(x, y, GoButtonWidth, controlHeight));

            double favoriteX = finalRect.Left + Margin;
            double favoriteY = finalRect.Top + ToolbarHeight + (FavoritesBarHeight - ControlHeight) / 2;
            foreach (StandardButton button in _favorites)
            {
                if (compact)
                {
                    button.Visibility = UiVisibility.Collapsed;
                    continue;
                }

                double width = Math.Min(button.DesiredSize.Width, Math.Max(0, finalRect.Right - Margin - favoriteX));
                if (width < 24)
                {
                    button.Visibility = UiVisibility.Collapsed;
                    continue;
                }

                button.Visibility = UiVisibility.Visible;
                button.Arrange(new BRect(favoriteX, favoriteY, width, ControlHeight));
                favoriteX += width + Margin;
            }

            double favoritesHeight = compact ? 0 : FavoritesBarHeight;
            double contentTop = finalRect.Top + ToolbarHeight + favoritesHeight;
            double statusTop = Math.Max(contentTop, finalRect.Bottom - StatusBarHeight);
            _viewport.Arrange(new BRect(finalRect.Left, contentTop, finalRect.Width, Math.Max(0, statusTop - contentTop)));
            _status.Arrange(new BRect(finalRect.Left + Margin, statusTop, Math.Max(0, finalRect.Width - 2 * Margin), StatusBarHeight));
        }

        protected override void RenderCore(UiRenderContext context)
        {
            double favoritesHeight = _isCompact ? 0 : FavoritesBarHeight;
            context.RenderList.FillRect(Bounds, BrowserPalette.Canvas);
            context.RenderList.FillRect(new BRect(Bounds.Left, Bounds.Top, Bounds.Width, ToolbarHeight), BrowserPalette.Toolbar);
            if (favoritesHeight > 0)
                context.RenderList.FillRect(new BRect(Bounds.Left, Bounds.Top + ToolbarHeight, Bounds.Width, favoritesHeight), BrowserPalette.Canvas);
            context.RenderList.FillRect(new BRect(Bounds.Left, Bounds.Top + ToolbarHeight - 1, Bounds.Width, 1), BrowserPalette.ToolbarRule);
            if (favoritesHeight > 0)
                context.RenderList.FillRect(new BRect(Bounds.Left, Bounds.Top + ToolbarHeight + favoritesHeight - 1, Bounds.Width, 1), BrowserPalette.ToolbarRule);
            context.RenderList.FillRect(new BRect(Bounds.Left, Math.Max(Bounds.Top, Bounds.Bottom - StatusBarHeight), Bounds.Width, StatusBarHeight), BrowserPalette.Status);
            context.RenderList.FillRect(new BRect(Bounds.Left, Math.Max(Bounds.Top, Bounds.Bottom - StatusBarHeight), Bounds.Width, 1), BrowserPalette.ToolbarRule);
            base.RenderCore(context);
        }
    }

    private sealed class BrowserViewport : UiElement
    {
        private const double WheelScrollStep = 60;
        private const double KeyScrollStep = 48;

        private readonly Func<IBroilerRenderer?> _getRenderer;
        private readonly HtmlFormEditor _formEditor;
        private readonly HtmlFormState _formState = new();
        private readonly HtmlFormControlHost _controlHost;
        private bool _controlsDirty = true;
        private HtmlContainer _container = CreateContentContainer(WelcomePage, string.Empty);
        private HtmlGraphicsRenderList? _renderList;
        private InteractiveSession? _interactiveSession;
        private string? _lastAppliedHtml;
        private bool _layoutDirty = true;
        private bool _renderDirty = true;
        private bool _suppressNavigation;
        private float _contentHeight;
        private float _scrollY;
        private readonly Dictionary<long, BPoint> _touches = [];
        private BPoint _touchStart;
        private BPoint _touchLast;
        private bool _isTouchPanning;
        private double _lastPinchDistance;
        private float _viewportZoom = 1f;
        private BSize _lastLayoutSize;

        private const double TouchPanThreshold = 6;

        public BrowserViewport(Func<IBroilerRenderer?> getRenderer)
        {
            _getRenderer = getRenderer ?? throw new ArgumentNullException(nameof(getRenderer));
            _container.LinkClicked += OnLinkClicked;
            _formEditor = new HtmlFormEditor(this);
            _formEditor.Committed += (_, _) => MarkLayoutDirty();
            _formEditor.SubmitRequested += (_, e) => SubmitHostedField(e.FieldId, e.FieldName);
            _controlHost = new HtmlFormControlHost(this, _formState);
            _controlHost.Changed += (_, _) => Invalidate(UiInvalidationKind.Render);
            _controlHost.FilePickRequested += (_, e) => FilePickRequested?.Invoke(this, e);
        }

        public event EventHandler<BrowserLinkEventArgs>? LinkActivated;

        /// <summary>Raised when a hosted file control was activated; the shell shows the dialog.</summary>
        public event EventHandler<HtmlFilePickEventArgs>? FilePickRequested;

        /// <summary>
        /// Records the file the shell's dialog returned and refreshes the control. A
        /// <c>multiple</c> input accumulates, so picking again adds to its selection
        /// instead of replacing it — the dialog chooses one file at a time.
        /// </summary>
        public void RecordPickedFile(string controlId, string controlName, string path, bool allowsMultiple)
        {
            if (allowsMultiple)
                _formState.AddSelectedFile(controlId, controlName, path);
            else
                _formState.SetSelectedFile(controlId, controlName, path);

            _controlHost.RefreshFileLabels();
        }

        public string BaseUrl { get; private set; } = string.Empty;

        // The load window, not "are any timers queued at all" — see
        // InteractiveSession.HasWorkDueInLoadWindow. This drives the busy state, the 16 ms
        // animation tick and StopSession, and on a page holding an interval the unbounded
        // question never goes false: the browser would step JS and re-lay out the document on
        // the UI thread for as long as the window stayed open, which is what made google.com
        // hang while the CLI (which asks the bounded question) rendered it.
        public bool HasPendingWork => _interactiveSession?.HasWorkDueInLoadWindow == true;

        public void ReplacePage(HtmlContainer container, InteractiveSession? interactiveSession, string baseUrl)
        {
            ArgumentNullException.ThrowIfNull(container);
            StopSession();
            _formEditor.Cancel();
            _formState.Reset();
            _controlHost.Clear();
            _controlsDirty = true;
            DisposeRenderList();
            _container.LinkClicked -= OnLinkClicked;
            _container.Dispose();

            _container = container;
            _container.LinkClicked += OnLinkClicked;
            _interactiveSession = interactiveSession;
            _lastAppliedHtml = null;
            BaseUrl = baseUrl ?? string.Empty;
            _scrollY = 0;
            _viewportZoom = 1f;
            MarkLayoutDirty();
        }

        public bool StepAnimation()
        {
            if (_interactiveSession is null || !_interactiveSession.HasWorkDueInLoadWindow)
                return false;

            string? html = _interactiveSession.Step();

            // Re-parsing costs a full parse and layout of the document, so it is worth paying only
            // when the step actually changed it. A callback batch that touched no DOM — a timer
            // that only reads, schedules, or measures — still returns the serialised document, and
            // google.com runs many of those.
            if (!string.IsNullOrWhiteSpace(html) && !string.Equals(html, _lastAppliedHtml, StringComparison.Ordinal))
            {
                _lastAppliedHtml = html;
                _suppressNavigation = true;
                try
                {
                    _container.SetHtmlWithStyleSet(PrepareForBrowsing(html), baseUrl: BaseUrl);
                }
                finally
                {
                    _suppressNavigation = false;
                }

                MarkLayoutDirty();
            }

            if (!_interactiveSession.HasWorkDueInLoadWindow)
                StopSession();

            return html is not null;
        }

        public void StopSession()
        {
            _interactiveSession?.Dispose();
            _interactiveSession = null;
        }

        public void ReleaseGraphicsResources()
        {
            DisposeRenderList();
            _layoutDirty = true;
            _renderDirty = true;
        }

        public static HtmlContainer CreateContentContainer(string html, string baseUrl)
        {
            HtmlContainer container = new()
            {
                AvoidAsyncImagesLoading = true,
                AvoidImagesLateLoading = true,
                BaseUrl = baseUrl,
            };
            container.SetHtmlWithStyleSet(html, baseUrl: baseUrl);
            return container;
        }

        protected override BSize MeasureCore(BSize availableSize) =>
            new(
                double.IsInfinity(availableSize.Width) ? 640 : Math.Max(0, availableSize.Width),
                double.IsInfinity(availableSize.Height) ? 360 : Math.Max(0, availableSize.Height));

        protected override void RenderCore(UiRenderContext context)
        {
            context.RenderList.FillRect(Bounds, ResolveClearColor());
            if (Bounds.IsEmpty)
                return;

            IBroilerRenderer? renderer = _getRenderer();
            if (renderer is null)
            {
                context.RenderList.DrawText(
                    new BTextRun("Renderer unavailable", new BFontStyle("Segoe UI", 14), BrowserPalette.Muted),
                    new BPoint(Bounds.Left + 24, Bounds.Top + 24));
                return;
            }

            BRenderList? htmlList = BuildHtmlRenderList(renderer);
            if (htmlList is null)
                return;

            context.RenderList.PushClip(Bounds);
            context.RenderList.PushTransform(
                BMatrix3x2.Scale(_viewportZoom, _viewportZoom) *
                BMatrix3x2.Translation(Bounds.Left, Bounds.Top));
            ReplayCommands(context.RenderList, htmlList.Commands);
            context.RenderList.PopTransform();

            // Hosted form controls paint over the page, in viewport coordinates,
            // so they are placed and drawn outside the document transform.
            RebuildHostedControlsIfNeeded();
            PlaceHostedControls(Bounds);
            base.RenderCore(context);
            context.RenderList.PopClip();
        }

        /// <summary>
        /// Positions the Broiler.UI controls hosted over the page's form fields.
        /// The viewport arranges them itself — the default child arrangement would
        /// stretch each one across the whole viewport and swallow every click.
        /// </summary>
        protected override void ArrangeCore(BRect finalRect) => PlaceHostedControls(finalRect);

        private void PlaceHostedControls(BRect viewportBounds)
        {
            _formEditor.UpdateViewport(viewportBounds, _viewportZoom, _scrollY);
            _controlHost.UpdateViewport(_container, viewportBounds, _viewportZoom, _scrollY);
        }

        /// <summary>
        /// Discovers the page's checkbox, radio and select controls once per page. The parse is
        /// the expensive half, so it is deferred to the first render after the page
        /// changed rather than repeated per layout.
        /// </summary>
        private void RebuildHostedControlsIfNeeded()
        {
            if (!_controlsDirty)
                return;

            _controlsDirty = false;
            _controlHost.Rebuild(GetPageHtml());
        }

        protected override bool OnInput(UiInputEvent input)
        {
            switch (input.Kind)
            {
                case UiInputEventKind.PointerButton:
                    return HandlePointerButton(input);
                case UiInputEventKind.PointerMove:
                    return HandlePointerMove(input);
                case UiInputEventKind.PointerWheel:
                    return HandleWheel(input);
                case UiInputEventKind.TouchContact:
                    return HandleTouch(input);
                case UiInputEventKind.KeyboardKey:
                    return HandleKeyboard(input);
                default:
                    return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopSession();
                DisposeRenderList();
                _container.LinkClicked -= OnLinkClicked;
                _container.Dispose();
            }

            base.Dispose(disposing);
        }

        private BRenderList? BuildHtmlRenderList(IBroilerRenderer renderer)
        {
            if (Bounds.IsEmpty)
                return null;

            float viewportWidth = (float)Math.Max(0, Bounds.Width);
            float viewportHeight = (float)Math.Max(0, Bounds.Height);
            BSize viewportSize = new(viewportWidth, viewportHeight);
            if (_layoutDirty || viewportSize != _lastLayoutSize)
            {
                _container.Location = PointF.Empty;
                _container.MaxSize = new SizeF(viewportWidth, viewportHeight);
                _container.PerformLayout(new RectangleF(0, 0, viewportWidth, viewportHeight));
                _contentHeight = _container.ActualSize.Height * _viewportZoom;
                _layoutDirty = false;
                _renderDirty = true;
                _lastLayoutSize = viewportSize;
            }

            ClampScroll(viewportHeight);
            if (_renderDirty || _renderList is null)
            {
                _container.ScrollOffset = new PointF(0, -_scrollY / _viewportZoom);
                DisposeRenderList();
                _renderList = HtmlGraphicsRenderListBuilder.Build(
                    renderer,
                    _container.CreateDisplayList(),
                    new RectangleF(0, 0, viewportWidth, viewportHeight));
                _renderDirty = false;
            }

            return _renderList.RenderList;
        }

        private bool HandlePointerButton(UiInputEvent input)
        {
            PointF point = ToLocalPoint(input.Position);
            bool left = input.MouseButton == MouseButton.Left;
            bool right = input.MouseButton == MouseButton.Right;

            if (input.MouseButtonTransition == MouseButtonTransition.Down)
            {
                // A click on a text field starts editing instead of a text selection.
                if (left && BeginFormEdit(point))
                    return true;

                _formEditor.Commit();
                Session?.SetFocus(this);
                _container.HandleMouseDown(point, left, right);
                InvalidateRenderedContent();
                return true;
            }

            if (input.MouseButtonTransition == MouseButtonTransition.Up)
            {
                _container.HandleMouseUp(point, left, right);
                InvalidateRenderedContent();
                return true;
            }

            return false;
        }

        private bool BeginFormEdit(PointF viewportPoint)
        {
            if (!_formEditor.TryBegin(_container, viewportPoint))
                return false;

            Session?.SetFocus(_formEditor.Editor);
            PlaceHostedControls(Bounds);
            Invalidate(UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            return true;
        }

        private bool HandlePointerMove(UiInputEvent input)
        {
            _container.HandleMouseMove(ToLocalPoint(input.Position), false, false);
            InvalidateRenderedContent();
            return true;
        }

        private bool HandleWheel(UiInputEvent input)
        {
            if (input.WheelAxis != MouseWheelAxis.Vertical)
                return false;

            ScrollBy(-(float)(input.WheelDeltaNotches * WheelScrollStep));
            return true;
        }

        private bool HandleTouch(UiInputEvent input)
        {
            if (input.TouchContactState is not TouchContactState state)
                return false;

            if (state == TouchContactState.Pressed)
            {
                _touches[input.ContactId] = input.Position;
                if (_touches.Count == 1)
                {
                    _touchStart = input.Position;
                    _touchLast = input.Position;
                    _isTouchPanning = false;
                    return false;
                }

                _lastPinchDistance = ActiveTouchDistance();
                _isTouchPanning = true;
                return true;
            }

            if (!_touches.ContainsKey(input.ContactId))
                return false;

            if (state == TouchContactState.Moved)
            {
                _touches[input.ContactId] = input.Position;
                if (_touches.Count >= 2)
                {
                    double distance = ActiveTouchDistance();
                    if (_lastPinchDistance > 0 && distance > 0)
                    {
                        float nextZoom = (float)Math.Clamp(_viewportZoom * (distance / _lastPinchDistance), 0.5, 4.0);
                        if (Math.Abs(nextZoom - _viewportZoom) > 0.001f)
                        {
                            _viewportZoom = nextZoom;
                            _contentHeight = _container.ActualSize.Height * nextZoom;
                            InvalidateRenderedContent();
                        }
                    }

                    _lastPinchDistance = distance;
                    return true;
                }

                double totalX = input.Position.X - _touchStart.X;
                double totalY = input.Position.Y - _touchStart.Y;
                if (!_isTouchPanning && Math.Sqrt((totalX * totalX) + (totalY * totalY)) >= TouchPanThreshold)
                    _isTouchPanning = true;
                if (!_isTouchPanning)
                {
                    _touchLast = input.Position;
                    return false;
                }

                ScrollBy((float)(_touchLast.Y - input.Position.Y));
                _touchLast = input.Position;
                return true;
            }

            if (state is TouchContactState.Released or TouchContactState.Cancelled)
            {
                bool handled = _isTouchPanning || _touches.Count > 1;
                _touches.Remove(input.ContactId);
                _lastPinchDistance = _touches.Count >= 2 ? ActiveTouchDistance() : 0;
                if (_touches.Count == 1)
                {
                    _touchStart = _touches.Values.First();
                    _touchLast = _touchStart;
                }
                else if (_touches.Count == 0)
                {
                    _isTouchPanning = false;
                }

                return handled;
            }

            return false;
        }

        private double ActiveTouchDistance()
        {
            using IEnumerator<BPoint> points = _touches.Values.GetEnumerator();
            if (!points.MoveNext())
                return 0;
            BPoint first = points.Current;
            if (!points.MoveNext())
                return 0;
            BPoint second = points.Current;
            double x = second.X - first.X;
            double y = second.Y - first.Y;
            return Math.Sqrt((x * x) + (y * y));
        }

        private bool HandleKeyboard(UiInputEvent input)
        {
            if (input.KeyTransition != KeyboardKeyTransition.Down)
                return false;

            bool control = input.KeyModifiers.HasFlag(KeyboardModifierState.Control);
            _container.HandleKeyDown(
                control,
                IsKey(input, BVirtualKey.A, "A"),
                IsKey(input, BVirtualKey.C, "C"));

            if (IsKey(input, BVirtualKey.Down, "Down"))
                ScrollBy((float)KeyScrollStep);
            else if (IsKey(input, BVirtualKey.Up, "Up"))
                ScrollBy(-(float)KeyScrollStep);
            else if (IsKey(input, BVirtualKey.PageDown, "PageDown"))
                ScrollBy(Math.Max(1, (float)Bounds.Height - 40));
            else if (IsKey(input, BVirtualKey.PageUp, "PageUp"))
                ScrollBy(-Math.Max(1, (float)Bounds.Height - 40));
            else if (IsKey(input, BVirtualKey.Home, "Home"))
                SetScroll(0);
            else if (IsKey(input, BVirtualKey.End, "End"))
                SetScroll(float.MaxValue);
            else
                InvalidateRenderedContent();

            return true;
        }

        private void ScrollBy(float delta) => SetScroll(_scrollY + delta);

        private void SetScroll(float value)
        {
            _scrollY = value;
            _renderDirty = true;
            Invalidate(UiInvalidationKind.Render);
        }

        private void ClampScroll(float viewportHeight)
        {
            float maxScroll = Math.Max(0, _contentHeight - viewportHeight);
            float clamped = Math.Clamp(_scrollY, 0, maxScroll);
            if (Math.Abs(clamped - _scrollY) > 0.01f)
            {
                _scrollY = clamped;
                _renderDirty = true;
            }
        }

        private void InvalidateRenderedContent()
        {
            _renderDirty = true;
            Invalidate(UiInvalidationKind.Render);
        }

        private void MarkLayoutDirty()
        {
            _layoutDirty = true;
            _renderDirty = true;
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        }

        private BColor ResolveClearColor()
        {
            BColor background = _container.GetRootBackgroundColor();
            return !background.IsEmpty && background.A > 0
                ? new BColor(background.R, background.G, background.B, background.A)
                : BColor.White;
        }

        private void OnLinkClicked(object? sender, HtmlLinkClickedEventArgs e)
        {
            e.Handled = true;
            if (_suppressNavigation)
                return;

            // The renderer resolves a submit control to its form's action and nothing
            // more; serialize the form's fields so the submission actually carries them.
            PageRequest request = _formState.TryBuildSubmitRequest(GetPageHtml(), e.Attributes, e.Link)
                ?? PageRequest.ForUrl(e.Link);
            LinkActivated?.Invoke(this, new BrowserLinkEventArgs(request, e.Attributes));
        }

        /// <summary>
        /// The live document as the renderer serializes it — values the user has
        /// committed are already in it. Empty when the page cannot be serialized,
        /// which only costs the caller its fallback.
        /// </summary>
        private string GetPageHtml()
        {
            try
            {
                return _container.GetHtml();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Submits the form containing a hosted field, which is what pressing Enter in
        /// a text control does.
        /// </summary>
        private void SubmitHostedField(string fieldId, string fieldName)
        {
            if (_suppressNavigation)
                return;

            PageRequest? request = _formState.TryBuildFieldSubmitRequest(GetPageHtml(), fieldId, fieldName, BaseUrl);
            if (request is not null)
                LinkActivated?.Invoke(this, new BrowserLinkEventArgs(request, new Dictionary<string, string>()));
        }

        private void DisposeRenderList()
        {
            HtmlGraphicsRenderList? renderList = _renderList;
            _renderList = null;
            _renderDirty = true;
            renderList?.Dispose();
        }

        private PointF ToLocalPoint(BPoint point) =>
            new((float)((point.X - Bounds.Left) / _viewportZoom), (float)((point.Y - Bounds.Top) / _viewportZoom));

        private static void ReplayCommands(BRenderList target, IReadOnlyList<BRenderCommand> commands)
        {
            foreach (BRenderCommand command in commands)
            {
                switch (command)
                {
                    case BRenderCommand.FillRect fill:
                        target.FillRect(fill.Rect, fill.Color);
                        break;
                    case BRenderCommand.StrokeRect stroke:
                        target.StrokeRect(stroke.Rect, stroke.Color, stroke.Thickness);
                        break;
                    case BRenderCommand.FillRoundedRect fillRounded:
                        target.FillRoundedRect(fillRounded.Rect, fillRounded.Color, fillRounded.RadiusX, fillRounded.RadiusY);
                        break;
                    case BRenderCommand.StrokeRoundedRect strokeRounded:
                        target.StrokeRoundedRect(strokeRounded.Rect, strokeRounded.Color, strokeRounded.RadiusX, strokeRounded.RadiusY, strokeRounded.Thickness);
                        break;
                    case BRenderCommand.DrawText text:
                        target.DrawText(text.Text, text.Origin);
                        break;
                    case BRenderCommand.DrawImage image:
                        target.DrawImage(image.Image, image.Source, image.Destination, image.Opacity);
                        break;
                    case BRenderCommand.PushClip clip:
                        target.PushClip(clip.Rect);
                        break;
                    case BRenderCommand.PopClip:
                        target.PopClip();
                        break;
                    case BRenderCommand.PushTransform transform:
                        target.PushTransform(transform.Transform);
                        break;
                    case BRenderCommand.PopTransform:
                        target.PopTransform();
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Lays out the resubmission prompt: the message across the top, its two buttons
    /// on a row at the bottom right. A dialog's default child arrangement stretches
    /// every child over the whole surface, so this places them itself.
    /// </summary>
    private sealed class ResubmitPrompt : UiElement
    {
        private const double Margin = 16;
        private const double ButtonHeight = 28;
        private const double ButtonWidth = 84;
        private const double ButtonGap = 8;

        private readonly StandardLabel _message;
        private readonly StandardButton _accept;
        private readonly StandardButton _cancel;

        public ResubmitPrompt(StandardLabel message, StandardButton accept, StandardButton cancel)
        {
            _message = message;
            _accept = accept;
            _cancel = cancel;
            AddChild(_message);
            AddChild(_cancel);
            AddChild(_accept);
        }

        protected override BSize MeasureCore(BSize availableSize)
        {
            foreach (UiElement child in Children)
                child.Measure(availableSize);

            return availableSize;
        }

        protected override void ArrangeCore(BRect finalRect)
        {
            double buttonTop = Math.Max(finalRect.Top, finalRect.Bottom - Margin - ButtonHeight);
            _message.Arrange(new BRect(
                finalRect.Left + Margin,
                finalRect.Top + Margin,
                Math.Max(0, finalRect.Width - (2 * Margin)),
                Math.Max(0, buttonTop - finalRect.Top - (2 * Margin))));

            double acceptLeft = finalRect.Right - Margin - ButtonWidth;
            _accept.Arrange(new BRect(acceptLeft, buttonTop, ButtonWidth, ButtonHeight));
            _cancel.Arrange(new BRect(acceptLeft - ButtonGap - ButtonWidth, buttonTop, ButtonWidth, ButtonHeight));
        }
    }

    private sealed class BrowserLinkEventArgs : EventArgs
    {
        public BrowserLinkEventArgs(string link, IReadOnlyDictionary<string, string> attributes)
            : this(PageRequest.ForUrl(link), attributes)
        {
        }

        public BrowserLinkEventArgs(PageRequest request, IReadOnlyDictionary<string, string> attributes)
        {
            Request = request;
            Link = request.Url;
            Attributes = attributes;
        }

        /// <summary>The navigation to perform — a plain URL, or a form submission with a body.</summary>
        public PageRequest Request { get; }

        public string Link { get; }

        public IReadOnlyDictionary<string, string> Attributes { get; }
    }

    private const string WelcomePage = """
<html>
<head>
    <style>
        body { font-family: Segoe UI, Arial, sans-serif; margin: 40px; background: #fafafa; color: #333; }
        h1 { color: #2c3e50; }
        p { line-height: 1.6; }
        .info { background: #ecf0f1; padding: 16px; border-radius: 4px; margin-top: 20px; }
    </style>
</head>
<body>
    <h1>Welcome to Broiler</h1>
    <p>This is the Broiler browser running with shared Broiler.UI controls.</p>
    <div class='info'>
        <p><strong>Getting Started:</strong> type a URL in the address bar and press Enter or click Go.</p>
        <p><strong>Features:</strong></p>
        <ul>
            <li>Shared Win32/Linux browser toolbar and status bar</li>
            <li>HTML &amp; CSS rendering via Broiler.HTML.Graphics</li>
            <li>JavaScript execution with interactive animation stepping</li>
            <li>Navigation history, favorites, links, mouse-wheel and keyboard scrolling</li>
        </ul>
    </div>
</body>
</html>
""";
}
