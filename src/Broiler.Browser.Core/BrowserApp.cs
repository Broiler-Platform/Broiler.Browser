using System.Diagnostics;
using System.Drawing;
using Broiler.App;
using Broiler.App.Rendering;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Graphics.Windowing;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Graphics;
using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Dom;
using Broiler.HtmlBridge.Logging;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Input.Text;
using Broiler.Input.Touch;
using Broiler.Net.Http;
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
using Broiler.HTML.Image;

namespace Broiler.Browser;

internal sealed partial class BrowserApp : IDisposable
{
    private const double AnimationIntervalMs = 16;

    private readonly BrowserUiHost _host;
    private readonly Func<IBroilerRenderer?> _getRenderer;
    private readonly Action<bool> _setAnimationActive;
    private readonly UiSession _session;
    private readonly BrowserProfile _profile;
    private readonly IBrowserRequestTransport _network;
    private readonly bool _ownsProfile;
    private readonly FavoritesManager _favorites;
    private readonly List<PageRequest> _history = [];
    private readonly StandardButton _backButton;
    private readonly StandardButton _forwardButton;
    private readonly StandardButton _refreshButton;
    private readonly StandardButton _stopButton;
    private readonly StandardButton _goButton;
    private readonly StandardButton _starButton;

#if BROILER_VM_JS
    /// <summary>Clears the VM code cache. Present only where that cache can exist.</summary>
    private readonly StandardButton _clearCacheButton;
#endif

    private readonly StandardEdit _address;
    private readonly StandardLabel _status;
    private readonly BrowserViewport _viewport;
    private readonly BrowserContent _content;
    private readonly StandardWindow _rootWindow;
    private int _historyIndex = -1;

    // The run of history entries that are the document on screen's: the one it was loaded at, and those
    // its fragment navigations and pushState added. Back and forward among them are the page's to make
    // (BrowserViewport.TraverseHistory); anywhere else loads a document.
    // Where a back or forward to another document puts the scroll once that document has loaded.
    private float? _pendingScrollRestore;

    private int _documentFirstEntry = -1;
    private int _documentLastEntry = -1;
    private bool _isPageBusy;
    private bool _isShuttingDown;
    private long _navigationGeneration;
    private CancellationTokenSource? _navigationCancellation;
    // The load whose intermediate document is on screen but not yet painted; RenderFrame releases it.
    private LoadProgress? _progressAwaitingPaint;

    /// <summary>
    /// A browser window with a private, ephemeral profile of its own: nothing it stores outlives it,
    /// and nothing is shared with any other window. Composition roots pass the user's profile through
    /// the overload that takes one.
    /// </summary>
    public BrowserApp(
        BrowserUiHost host,
        Func<IBroilerRenderer?> getRenderer,
        string? initialUrl,
        Action<bool> setAnimationActive)
        : this(host, getRenderer, initialUrl, setAnimationActive, BrowserProfile.CreateEphemeral(), ownsProfile: true)
    {
    }

    /// <summary>
    /// A browser window on <paramref name="profile"/>: its cookies, its network session and its
    /// favorites. The profile belongs to the caller and must outlive the window.
    /// </summary>
    public BrowserApp(
        BrowserUiHost host,
        Func<IBroilerRenderer?> getRenderer,
        string? initialUrl,
        Action<bool> setAnimationActive,
        BrowserProfile profile,
        Func<IBrowserRequestTransport, IBrowserRequestTransport>? wrapNetwork = null,
        PageRequest? initialRequest = null)
        : this(host, getRenderer, initialUrl, setAnimationActive, profile ?? throw new ArgumentNullException(nameof(profile)), ownsProfile: false, wrapNetwork, initialRequest)
    {
    }

    private BrowserApp(
        BrowserUiHost host,
        Func<IBroilerRenderer?> getRenderer,
        string? initialUrl,
        Action<bool> setAnimationActive,
        BrowserProfile profile,
        bool ownsProfile,
        Func<IBrowserRequestTransport, IBrowserRequestTransport>? wrapNetwork = null,
        PageRequest? initialRequest = null)
    {
        _profile = profile;
        _network = wrapNetwork?.Invoke(profile.Network) ?? profile.Network;
        _ownsProfile = ownsProfile;
        _favorites = new FavoritesManager(profile.FavoritesPath);
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
#if BROILER_VM_JS
        _clearCacheButton = CreateChromeButton("Cache", "Clear code cache");
#endif
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
        _viewport = new BrowserViewport(_getRenderer) { VisitedLinkPredicate = IsVisited };
        _content = new BrowserContent(
            _backButton,
            _forwardButton,
            _refreshButton,
            _stopButton,
            _address,
            _starButton,
            _goButton,
#if BROILER_VM_JS
            _clearCacheButton,
#else
            null,
#endif
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
#if BROILER_VM_JS
        _clearCacheButton.Clicked += (_, _) => ConfirmClearCodeCache();
#endif
        _address.Submitted += (_, _) => NavigateTo(_address.Text);
        _viewport.LinkActivated += OnViewportLinkActivated;
        _viewport.FilePickRequested += OnViewportFilePickRequested;

        _favorites.Load();
        RefreshFavoritesBar();
        UpdateNavigationButtons();
        SetBusy(false);
        _session.SetFocus(_address);

        // Before the first navigation, whose page may finish loading before the window's first
        // layout: its media queries need a viewport, and every layout after replaces this estimate.
        if (_host.ViewportSize is { Width: > 0, Height: > 0 } window)
            _viewport.SeedPageArea(BrowserContent.PageAreaFor(window));

        NavigateTo(initialRequest ?? PageRequest.ForUrl(initialUrl ?? "about:blank"));
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

        // The frame told the page where the user scrolled the view; the page hears its scroll event in a
        // task, which the tick steps. Nothing else would step it before the next input.
        if (_viewport.TakeReportedScroll() && _viewport.HasPendingWork)
            _setAnimationActive(true);

        return frame;
    }

    /// <summary>
    /// The window size whose page area is <paramref name="pageWidth"/>×<paramref name="pageHeight"/>:
    /// what to open a window at to show a page at a given viewport, as <c>--analyze</c> does.
    /// </summary>
    internal static BSize WindowSizeFor(double pageWidth, double pageHeight) =>
        BrowserContent.WindowSizeFor(pageWidth, pageHeight);

    /// <summary>Where the window draws the page, in window coordinates.</summary>
    internal BRect PageArea => _viewport.Bounds;

    /// <summary>The controls the window draws over the page's selects, check boxes, radio buttons and file inputs.</summary>
    internal IReadOnlyList<UiElement> HostedControls => _viewport.HostedControls;

    /// <summary>The address the window shows.</summary>
    internal string AddressText => _address.Text;

    public void Dispatch(UiInputEvent input)
    {
        if (HandleGlobalShortcut(input))
        {
            _host.RequestInvalidate();
            return;
        }

        // Input meant for the page -- a key or typed text while the page has focus, a press on a control
        // the window hosts over the page -- reaches the page's scripts before the window acts on it.
        bool handled = _viewport.TryDispatchThroughPage(input, _session, out bool handledThroughPage)
            ? handledThroughPage
            : _session.DispatchInput(input);
        if (handled)
            _host.RequestInvalidate();

        // A move is page input too: a hover handler can start a timer, or navigate; and so is a key.
        if (input.Kind is UiInputEventKind.PointerButton or UiInputEventKind.PointerMove
            or UiInputEventKind.KeyboardKey or UiInputEventKind.TextInput)
        {
            AfterPageInput();
        }
    }

    /// <summary>
    /// What a page's scripts asked for when the user clicked it: a navigation -- a click handler
    /// setting <c>location.href</c>, or submitting a form -- or work to step, a spinner turning or a
    /// request's answer arriving, which the animation tick steps as it steps a page loading.
    /// </summary>
    private void AfterPageInput()
    {
        if (_isShuttingDown)
            return;

        if (ApplyPageHistory())
            return;

        if (_viewport.TakePendingNavigation() is { } requested
            && !(requested.IsRepeatable && requested.InlineDocument is null
                && string.Equals(requested.Url, CurrentHistoryUrl(), StringComparison.OrdinalIgnoreCase)))
        {
            NavigateTo(requested);
            return;
        }

        if (_viewport.HasPendingWork)
            _setAnimationActive(true);
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

        // A loaded page can still decide to leave — a click handler calling form.submit(), a timer
        // reaching location.href. The load loop reads that question while a page is loading; this
        // is the same question afterwards, and it is asked here because this is the UI thread, the
        // one place NavigateTo can be called from.
        //
        // It goes through NavigateTo rather than the load loop's follow, and the difference is
        // deliberate: this is a new navigation the way a link click is, not another hop in a
        // redirect chain, so it gets a history entry and a fresh set of loop budgets.
        // A request for the page already shown is refused only when repeating it would change
        // nothing: a GET of the same URL is a timer asking for what is on screen. A POST to the same
        // URL is a submission — the body is the difference, and refusing it would lose the form.
        //
        // The request names the page that asked (the bridge's initiator, or the document on screen),
        // so the transport judges it as that document's navigation rather than the user's own.
        //
        // The page's session history is read first: a pushState before a location.href leaves an entry
        // behind the page it navigates to, as it does in a browser.
        if (ApplyPageHistory())
            return;

        if (_viewport.TakePendingNavigation() is { } requested
            && !(requested.IsRepeatable && requested.InlineDocument is null
                && string.Equals(requested.Url, CurrentHistoryUrl(), StringComparison.OrdinalIgnoreCase)))
        {
            NavigateTo(requested);
            return;
        }

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

        // After the session, which takes the viewport and its containers with it: nothing left can
        // start a request on the network being disposed. A load still in flight fails, and its result
        // is dropped because the window is shutting down.
        if (_ownsProfile)
            _profile.Dispose();
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

        // A javascript: URL runs its script in the page on screen -- if its scripts run at all -- and
        // loads nothing: navigating to one would replace the page with an error (HTML "navigate to a
        // javascript: URL").
        if (request.Url.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            if (_viewport.RunJavaScriptUrl(request.Url.Trim()))
                AfterPageInput();
            return;
        }

        request = request with { Url = NormalizeInput(request.Url) };
        _pendingScrollRestore = null;

        // A javascript: URL's string is the page's document at its URL, shown in place of the one on
        // screen, whose history entry it keeps (HTML "navigate to a javascript: URL").
        if (request.InlineDocument is not null)
        {
            LoadUrl(request);
            return;
        }

        // A link to the fragment the page is already at is no new entry, as it is none in a browser.
        if (FragmentWithinCurrentDocument(request) is { } sameFragment &&
            string.Equals(request.Url, CurrentHistoryUrl(), StringComparison.Ordinal))
        {
            _viewport.ScrollToFragment(sameFragment);
            _host.RequestInvalidate();
            return;
        }

        RememberScroll();
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        // History holds the whole request, so revisiting a POST can re-issue it —
        // behind a confirmation, since repeating a submission is not free.
        _history.Add(request);
        _historyIndex = _history.Count - 1;

        // HTML §7.4.2.3.3, navigate to a fragment: a link into the document on screen scrolls it and
        // loads nothing. Its entry is the document's.
        if (FragmentWithinCurrentDocument(request) is { } fragment)
        {
            _documentLastEntry = _historyIndex;
            ShowFragment(request.Url, fragment);
            return;
        }

        LoadUrl(request);
    }

    /// <summary>
    /// The fragment of a request that only moves within the document on screen, or null for one
    /// that has to load: a GET without a body, while nothing is loading, whose URL is the document's
    /// but for its fragment. With <paramref name="orTop"/>, a URL with no fragment at all moves to
    /// the top (an empty fragment), which is what going back to it in history does.
    /// </summary>
    private string? FragmentWithinCurrentDocument(PageRequest request, bool orTop = false)
    {
        if (!request.IsRepeatable || request.Body is not null || request.BinaryBody is not null
            || _navigationCancellation is not null
            || !Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? target)
            || !Uri.TryCreate(_viewport.BaseUrl, UriKind.Absolute, out Uri? current)
            || !string.Equals(target.GetLeftPart(UriPartial.Query), current.GetLeftPart(UriPartial.Query), StringComparison.Ordinal))
        {
            return null;
        }

        return FragmentOf(request.Url) ?? (orTop ? string.Empty : null);
    }

    /// <summary>
    /// Applies what the page's session history did since the window last asked: an entry its pushState or
    /// a fragment navigation added -- after which the forward entries are gone -- or replaced, and its
    /// traversals among its own entries, which move the window's place in its history and the address it
    /// shows. A traversal to another document's entry is the window's to make; answers whether it made one,
    /// which leaves the page.
    /// </summary>
    private bool ApplyPageHistory()
    {
        var changes = _viewport.TakeHistoryChanges();
        foreach (HistoryChange change in changes)
        {
            switch (change.Kind)
            {
                case HistoryChangeKind.Push:
                    RememberScroll();
                    if (_historyIndex < _history.Count - 1)
                        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                    _history.Add(PageRequest.ForUrl(change.Url) with { Initiator = _viewport.DocumentContext });
                    _historyIndex = _history.Count - 1;
                    _documentLastEntry = _historyIndex;
                    NoteVisited(change.Url);
                    break;

                case HistoryChangeKind.Replace when _historyIndex >= 0 && _historyIndex < _history.Count:
                    _history[_historyIndex] = _history[_historyIndex] with { Url = change.Url };
                    NoteVisited(change.Url);
                    break;

                case HistoryChangeKind.Traverse:
                    _historyIndex = Math.Clamp(_historyIndex + change.Delta, _documentFirstEntry, _documentLastEntry);
                    break;

                case HistoryChangeKind.TraverseAway:
                    GoHistory(change.Delta);
                    return true;
            }
        }

        if (changes.Count > 0)
        {
            SetUrlText(CurrentHistoryUrl());
            UpdateNavigationButtons();
            UpdateStarButton();
        }

        return false;
    }

    /// <summary>The current entry of the window's history keeps where the view is scrolled, which going back to it restores.</summary>
    private void RememberScroll()
    {
        if (_historyIndex >= 0 && _historyIndex < _history.Count)
            _history[_historyIndex] = _history[_historyIndex] with { LeftAtScrollY = _viewport.ScrollY };
    }

    /// <summary>What follows the <c>#</c> of <paramref name="url"/>, or null when it has none.</summary>
    private static string? FragmentOf(string url)
    {
        int hash = url.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 ? url[(hash + 1)..] : null;
    }

    private void ShowFragment(string url, string fragment)
    {
        SetUrlText(url);
        UpdateNavigationButtons();
        UpdateStarButton();
        NoteVisited(url);
        _viewport.ShowTarget(url, fragment);
        _viewport.ScrollToFragment(fragment);
        _host.RequestInvalidate();
    }

    /// <summary>
    /// The pages this window has shown, by URL, fragment included: what <c>:visited</c> asks of a link.
    /// Kept for as long as the window is open and never written anywhere, so it is a history of this
    /// session alone. Read by page loads on worker threads, so a concurrent set.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _visitedUrls = new(StringComparer.Ordinal);

    /// <summary>Whether this window has shown <paramref name="url"/>, for <c>:visited</c>.</summary>
    internal bool IsVisited(Uri url) => _visitedUrls.ContainsKey(url.AbsoluteUri);

    private void NoteVisited(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? visited) && (visited.Scheme == Uri.UriSchemeHttp || visited.Scheme == Uri.UriSchemeHttps || visited.IsFile))
            _visitedUrls.TryAdd(visited.AbsoluteUri, 0);
    }

    private void GoHistory(int delta)
    {
        if (_isShuttingDown)
            return;

        int target = _historyIndex + delta;
        if (target < 0 || target >= _history.Count)
            return;

        RememberScroll();

        // An entry of the page on screen -- one its pushState or a fragment made -- is the page's to go
        // to: it moves its URL and state and hears popstate, and nothing loads.
        if (target >= _documentFirstEntry && target <= _documentLastEntry && _viewport.TraverseHistory(delta))
        {
            // The page puts the scroll back where the entry was left, as Chromium does, rather than at
            // its fragment (DomBridge/SessionHistory.cs).
            _historyIndex = target;
            SetUrlText(_history[target].Url);
            UpdateNavigationButtons();
            UpdateStarButton();
            AfterPageInput();
            _host.RequestInvalidate();
            return;
        }

        // Re-issued as the entry's own navigation: the initiator it was created with, if any, still
        // started it.
        PageRequest request = _history[target] with { NavigationType = PageNavigationType.BackForward };

        // Back or forward between fragments of the document on screen scrolls it, as following them
        // did.
        if (FragmentWithinCurrentDocument(request, orTop: true) is { } fragment)
        {
            _historyIndex = target;
            ShowFragment(request.Url, fragment);
            if (request.LeftAtScrollY is { } left)
                _viewport.RestoreScroll(left);
            return;
        }

        // Another document: once it has loaded, the scroll goes back where the entry was left.
        _pendingScrollRestore = request.LeftAtScrollY;
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

        // A UI reload: the loader replays the same-site status recorded for the document on screen
        // (PageLoader.NavigationContext), because the reload itself has no initiator to judge by.
        PageRequest request = _history[_historyIndex] with { NavigationType = PageNavigationType.Reload };
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

        dialog.AddChild(new ConfirmPrompt(message, resend, cancel));
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

#if BROILER_VM_JS
    private static readonly BSize ClearCacheDialogSize = new(460, 190);

    /// <summary>
    /// Asks before deleting the compiled artifacts the VM code cache has written to disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the control that has to exist before the on-disk store may be turned on.</b> The
    /// artifacts hold a page's string literals and interned names as text, so the directory is a
    /// readable record of what has been browsed; a browser that can write that and not erase it is
    /// one whose users cannot undo a visit. docs/vm-javascript-profile.md carries the measurement
    /// and the reasoning.
    /// </para>
    /// <para>
    /// <b>It clears the default directory whether or not caching is switched on</b>, because the
    /// files that most need deleting are the ones left behind by a run when it WAS on. A store
    /// constructed here for the purpose is not the one the cache holds and does not enable
    /// anything.
    /// </para>
    /// </remarks>
    private void ConfirmClearCodeCache()
    {
        StandardDialog dialog = new()
        {
            Title = "Clear code cache",
            PreferredSize = ClearCacheDialogSize,
        };

        StandardLabel message = new()
        {
            Text =
                "Delete the compiled scripts saved on this computer? They record the text of pages " +
                "that have been opened. Pages will load as usual afterwards.",
            Font = new BFontStyle("Segoe UI", 13),
            Foreground = BrowserPalette.Text,
            Trimming = UiTextTrimming.None,
        };

        StandardButton clear = CreateChromeButton("Delete", "Delete");
        StandardButton cancel = CreateChromeButton("Cancel", "Cancel");

        clear.Clicked += (_, _) => dialog.Accept();
        cancel.Clicked += (_, _) => dialog.Cancel();

        dialog.AddChild(new ConfirmPrompt(message, clear, cancel));
        dialog.ResultCompleted += (_, e) =>
        {
            if (e.Result.Kind == UiDialogResultKind.Accepted)
                ClearCodeCache();

            _host.RequestInvalidate();
        };

        dialog.ShowModal(_rootWindow, GetDialogPlacement(ClearCacheDialogSize));
        _host.RequestInvalidate();
    }

    /// <summary>Deletes the on-disk artifacts and says so.</summary>
    private void ClearCodeCache()
    {
        new HtmlBridge.VmArtifactStore(HtmlBridge.VmArtifactStore.DefaultDirectory, 0).Clear();
        SetStatus("Code cache cleared.");
    }
#endif

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
            result = await LoadUrlOnWorkerAsync(_profile, _network, request, progress, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Stopped or superseded: nothing to show. A cancellation the navigation did not ask for —
            // the network session's own timeout — is a failed load and takes the error path below.
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

    // Page requests go through the profile's network session (BrowserProfile.Network), which is also
    // the connection pool: one for the profile rather than one per navigation, for the reasons in
    // docs/browser-connection-pool-aborts.md. It replaced a process-wide HttpClient whose handler kept
    // an automatic cookie jar of its own; the session sends and stores the profile's cookies per hop,
    // and every other loader of the page uses the same store.

    /// <summary>
    /// How many script-initiated navigations one user-initiated navigation will follow before it
    /// stops and shows the document it has.
    /// </summary>
    /// <remarks>
    /// A page that navigates on load is ordinary — a search form submitting through
    /// <c>location.replace</c>, a consent interstitial handing over to the page behind it — and
    /// following it is what a browser does. A page that navigates to itself on every load is also
    /// ordinary, and following that one forever is a hang with nothing on screen to explain it. The
    /// cap separates the two without having to tell them apart: ten is well past any real redirect
    /// chain and still bounded.
    /// </remarks>
    internal const int MaxScriptNavigations = 10;

    /// <summary>
    /// How many times one URL path may be loaded within a single navigation before its further
    /// requests to itself stop being followed.
    /// </summary>
    /// <remarks>
    /// The global cap counts hops; this counts repeats, and repeats are the failure that actually
    /// happens. A page re-submitting itself with a fresh token each round has a different URL every
    /// hop and the same path every hop, so only this notices.
    /// <para>
    /// Two loads, so one re-submission. That is the shape of the handshake that works — load, take a
    /// token or a cookie, ask once more — and a round still going after it is one that is collecting
    /// rather than converging. Google's search bootstrap is the measured case: the second hop adds a
    /// <c>sei</c>, the third a <c>sg_ss</c> signal blob, and no number of further hops satisfies it.
    /// Each one costs a request and buys nothing.
    /// </para>
    /// </remarks>
    internal const int SamePathLoadLimit = 2;

    /// <summary>
    /// The longest wait a <c>&lt;meta http-equiv="refresh"&gt;</c> may state and still be followed
    /// straight away.
    /// </summary>
    /// <remarks>
    /// The threshold is a judgement, and the honest version of one: there is no scheduler here, so
    /// the choice is between acting now and not acting, and the delay is the only evidence of which
    /// the author wanted. Two seconds is about where a wait stops reading as "you are being
    /// redirected" and starts reading as "here is something to look at first".
    /// </remarks>
    internal static readonly TimeSpan MetaRefreshFollowLimit = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The browser's script engine: the one place a configuration decides which one runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Debug-VM</c> and <c>Release-VM</c> define <c>BROILER_VM_JS</c>
    /// (eng/Broiler.Configurations.props) and add the PackageReference that puts
    /// <c>VmScriptEngine</c> in reach. Every other configuration compiles the second branch and
    /// links nothing of Broiler.VM at all.
    /// </para>
    /// <para>
    /// <b>The Broiler.JS engine is constructed on both paths, and under <c>-VM</c> it is handed to
    /// the VM engine rather than replaced.</b> The VM's JavaScript profile runs the script-only
    /// execution paths; the ones that need a live document are served by the engine that has one.
    /// <c>VmScriptEngine</c>'s remarks say why that is a property of the VM's host boundary rather
    /// than an unfinished port, and docs/vm-javascript-profile.md says what would have to change.
    /// </para>
    /// <para>
    /// <b>The command line composes its engine here too</b> (src/Broiler.Browser.Cli), so a capture
    /// runs a page's scripts on the engine this window would have run them on, under every
    /// configuration.
    /// </para>
    /// </remarks>
    internal static IScriptEngine NewScriptEngine(IDomBridgeRuntimeFactory bridges)
    {
#if BROILER_VM_JS
        RenderLogger.LogDebug(
            LogCategory.JavaScript,
            nameof(BrowserApp),
            "Script runs on the Broiler.VM JavaScript profile; document-bearing execution is served by Broiler.JS.");

        return new VmScriptEngine(new ScriptEngine(bridges));
#else
        return new ScriptEngine(bridges);
#endif
    }

    /// <summary>
    /// The options the bridge of every document <paramref name="profile"/> loads is created with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both engines run the document on the profile's network.</b> The bridge every hop of the
    /// navigation creates sends its loaders — module imports, inserted scripts, stylesheets, frames,
    /// <c>fetch()</c>, XHR and <c>sendBeacon</c> — through <paramref name="profile"/>'s session, and backs
    /// <c>document.cookie</c> with the profile's store. Its document's identity comes from
    /// <paramref name="documents"/>, which answers with the context the pipeline built for the hop, so the
    /// renderer, the extractor and the bridge all speak for one document object.
    /// </para>
    /// <para>
    /// <b>A script's geometry questions are answered from a real layout.</b>
    /// <c>getBoundingClientRect</c>, <c>offsetWidth</c> and the rest go to a
    /// <see cref="HeadlessLayoutView"/>, which lays the document out on the same network, as the same
    /// document. Without one the bridge answers from its null layout view, which says 0 to all of
    /// them — and until the view was added here, that is what the window's scripts got.
    /// </para>
    /// </remarks>
    internal static DomBridgeSessionOptions BridgeOptions(
        BrowserProfile profile,
        Func<Uri, DocumentRequestContext> documents) =>
        BridgeOptions(profile.Network, profile.DocumentCookies, documents);

    /// <summary>
    /// As <see cref="BridgeOptions(BrowserProfile, Func{Uri, DocumentRequestContext})"/>, over a
    /// <paramref name="network"/> that stands in for the profile's own — the command line's
    /// <c>--analyze</c> passes the profile's session wrapped in a recorder, so that the bridge's loads
    /// and its layout view's are recorded with everything else the page sent.
    /// </summary>
    /// <param name="network">The transport the bridge and its layout view load on.</param>
    /// <param name="cookies">The <c>document.cookie</c> view of the profile's store.</param>
    /// <param name="documents">The request context of the document at a URL.</param>
    /// <param name="wrapLayoutView">
    /// Wraps each layout view the bridge makes — <c>--analyze</c> times the layouts a script's geometry
    /// questions cause — or null for the view as it is.
    /// </param>
    /// <param name="viewport">
    /// The size the window shows the page at, asked as each document's bridge is made, or null for the
    /// bridge's default. The window passes its page area, so the page's scripts measure the page on
    /// screen and a click is hit-tested against the layout the user sees.
    /// </param>
    internal static DomBridgeSessionOptions BridgeOptions(
        IBrowserRequestTransport network,
        IDocumentCookieAccess cookies,
        Func<Uri, DocumentRequestContext> documents,
        Func<Broiler.Layout.ILayoutView, Broiler.Layout.ILayoutView>? wrapLayoutView = null,
        Func<Size?>? viewport = null) =>
        new()
        {
            Network = network,
            Cookies = cookies,
            DocumentContextFactory = documents,
            LayoutViewFactory = wrapLayoutView is null
                ? () => new HeadlessLayoutView(network, documents)
                : () => wrapLayoutView(new HeadlessLayoutView(network, documents)),
            Viewport = viewport,
        };

    private static async Task<NavigationLoadResult> LoadUrlOnWorkerAsync(
        BrowserProfile profile,
        IBrowserRequestTransport network,
        PageRequest request,
        LoadProgress progress,
        CancellationToken cancellationToken)
    {
        // The document the current hop loaded, for the bridge the engine attaches to it. One engine
        // serves every hop, so the bridge asks by URL rather than being told once. The hops run one
        // after another, and the bridge asks while its hop's scripts are being set up.
        DocumentRequestContext? currentDocument = null;
        DocumentRequestContext DocumentFor(Uri url) =>
            currentDocument is { } document && document.DocumentUrl == url
                ? document
                : DocumentRequestContext.CreateTopLevel(url);

        using var pipeline = new RenderingPipeline(
            new PageLoader(network),
            NewScriptEngine(new DomBridgeFactory(BridgeOptions(
                network, profile.DocumentCookies, DocumentFor, viewport: () => ViewportOf(progress)))),
            network);

        // Keyed by everything ahead of the query, because that is what separates a chain moving on
        // from a page re-submitting itself. See TryFollowNavigation.
        var loadsPerPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int hop = 0; ; hop++)
        {
            LoadedPage page = await pipeline.LoadAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Everything from here on is about the document the response came from: its final URL is
            // the base of its links, the key of the loop budget, the URL a script navigation is compared
            // against and what history records; its context is what its requests carry.
            string normalisedUrl = page.FinalUrl;
            DocumentRequestContext document = page.Document;
            var content = page.Content;
            currentDocument = document;

            // Every URL the response passed through counts towards the same-path budget: the one asked
            // for, each redirect and the final one. A script asking for any of them again — `/app`
            // redirecting to `/login`, whose script replaces the location with `/app` — is allowed its
            // retry, and keyed by the final URL alone the loop it can become was never counted.
            IReadOnlyList<string> hopUrls = HopUrls(page.Response, normalisedUrl);
            foreach (string loadedPath in hopUrls.Select(NavigationPathKey).Distinct(StringComparer.OrdinalIgnoreCase))
                loadsPerPath[loadedPath] = loadsPerPath.TryGetValue(loadedPath, out int loaded) ? loaded + 1 : 1;

            // Read off the fetched markup, before any script runs and whether or not there is any:
            // ExecuteScriptsInteractive returns null for a page with no scripts, and a refresh
            // interstitial is very often exactly that. A script that navigates later supersedes it
            // below, which is the same last-one-wins the bridge applies among script navigations.
            NavigationRequest? pending = MetaRefreshDiscovery.Find(content.Html, normalisedUrl, document);

            string html = PrepareForBrowsing(content.Html);
            string? documentHtml = null;
            InteractiveSession? session = null;
            try
            {
                session = pipeline.ExecuteScriptsInteractive(WithRealmForInlineHandlers(content), page.InheritedPolicy, page);
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
                        serialize => progress.PublishFrame(serialize, normalisedUrl, document),
                        cancellationToken);
                    if (!string.IsNullOrWhiteSpace(initial))
                    {
                        html = PrepareForBrowsing(initial);
                        documentHtml = initial;
                    }

                    // After the settle, because the script that decides to leave usually runs on a
                    // timer rather than inline — asking before it would miss exactly the pages that
                    // navigate. Before the dispose below, because the request lives on the bridge and
                    // disposal takes the bridge with it.
                    //
                    // A script navigation supersedes a refresh meta the same markup declared: both
                    // are this document asking to leave, and the script asked second.
                    //
                    // Taken, not read: whatever is decided below, this document has now had its
                    // answer. Left in place, a request declined here was picked up again by the
                    // post-load path a few seconds later and performed with a fresh set of budgets —
                    // the refusal undone by the code that was supposed to catch what came after it.
                    pending = session.TakePendingNavigation() ?? pending;

                    // The session is carried forward whether or not work is left in the load window: it
                    // is the page's scripts, and what the user does to the page -- a click -- is
                    // delivered to them through it. A page whose only remaining work is an interval's
                    // later ticks is still finished loading; the viewport steps only what is due
                    // (HasWorkDueInLoadWindow), and from then on only what a click makes due.
                }

                // The next hop is this document's navigation: the bridge names the document whose
                // script asked (a frame's, when a frame's script navigated the top window), and a
                // refresh meta or a request that names nobody falls back to this document.
                if (ShouldFollow(pending, normalisedUrl, hop, loadsPerPath)
                    && ToPageRequest(pending!, html, normalisedUrl, document) is { } next)
                {
                    // The page asked to leave before this document was ever shown, so it is not the
                    // document to show. Frames already published for it stay on screen until the next
                    // load publishes its own — the alternative is a blank pane for the length of
                    // another fetch, which is worse than a stale one.
                    session?.Dispose();
                    request = next;
                    continue;
                }

                // The frames the settle painted were this document too: their images, stylesheets and
                // fonts are already in hand, and the finished page reuses them instead of fetching
                // every one again, synchronously, on the UI thread's first layout.
                // A page whose scripts run has the bridge mark its :target; one without has the renderer
                // find it from the fragment it was opened at.
                HtmlContainer container = BrowserViewport.CreateContentContainer(
                    html, normalisedUrl, network, document, progress.LastFrame, progress.Viewport,
                    progress.VisitedLinks, session is null ? FragmentOf(normalisedUrl) ?? FragmentOf(request.Url) : null);
                return NavigationLoadResult.FromSuccess(
                    normalisedUrl,
                    container,
                    session,
                    documentHtml,
                    hop > 0,
                    request.ForLoadedDocument(page.Response));
            }
            catch
            {
                session?.Dispose();
                throw;
            }
        }
    }

    /// <summary>The window's page area as whole CSS pixels, or null before the window has one.</summary>
    private static Size? ViewportOf(LoadProgress progress) =>
        progress.Viewport is { Width: >= 1, Height: >= 1 } area
            ? new Size((int)Math.Round(area.Width), (int)Math.Round(area.Height))
            : null;

    /// <summary>
    /// <paramref name="content"/>, given a realm when it has no scripts of its own but still has script
    /// to run: the event handler attributes the user's input fires -- an <c>onclick</c>, an
    /// <c>onmouseenter</c>, an <c>onfocus</c> -- or frames, whose own scripts run only in a page that
    /// has one. Without a session nothing the user does reaches either. A form gets one too: a press on
    /// its submit button submits through the page (the renderer reports no submit control as a link),
    /// so on a page with no script at all clicking one did nothing.
    /// </summary>
    internal static Broiler.HtmlBridge.Scripting.PageContent WithRealmForInlineHandlers(Broiler.HtmlBridge.Scripting.PageContent content) =>
        content.Scripts.Count == 0 && content.DeferredScripts.Count == 0 && content.ModuleRoots.Count == 0 &&
        ScriptWithoutScripts().IsMatch(content.Html)
            ? new Broiler.HtmlBridge.Scripting.PageContent(content.Html, [string.Empty], content.Url, [], [])
            : content;

    [System.Text.RegularExpressions.GeneratedRegex(
        @"<[^>]*\son(?:click|dblclick|auxclick|contextmenu|mouse[a-z]+|pointer[a-z]+|focus(?:in|out)?|blur|change|input)\s*=|<i?frame[\s>]|<form[\s>]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ScriptWithoutScripts();

    /// <summary>
    /// Whether a document's navigation request should be followed.
    /// </summary>
    /// <remarks>
    /// Following is the default because not following is what made a search render as the search
    /// box: Google's results page is reached by <c>location.replace</c>, so a browser that only
    /// logged the request showed the form the query was typed into. See
    /// <c>docs/script-initiated-navigation.md</c>.
    /// <para>
    /// Deciding <i>whether</i> is separate from building <i>what</i> (<see cref="ToPageRequest"/>)
    /// because only the second needs the document: a form submission has to be serialized out of it,
    /// and the rules below would otherwise be untestable without one.
    /// </para>
    /// <para>
    /// Only <paramref name="currentUrl"/> -- where the load landed -- is the document already loaded.
    /// A URL the load was redirected away from is not: no document was loaded there, it answered with a
    /// redirect. Going back to it once is the "load, take a token or a cookie, ask again" handshake the
    /// same-path budget below allows for (a JS cookie challenge reached by a redirect, or a refresh
    /// interstitial), and every URL of the redirect chain already counts towards that budget, which is
    /// what stops a real loop.
    /// </para>
    /// </remarks>
    internal static bool ShouldFollow(
        NavigationRequest? navigation,
        string currentUrl,
        int hop,
        Dictionary<string, int> loadsPerPath)
    {
        if (navigation is null)
            return false;

        // A refresh meta states a wait, and the length of it is the author saying what the page is
        // for. A second or two is a redirect with a courtesy message; half a minute is a notice
        // meant to be read, and replacing it immediately would take away the thing it exists to
        // show. Nothing here schedules a navigation for later, so the long ones are declined rather
        // than deferred — a browser would honour them on a timer, and that is the piece not built.
        if (navigation.Delay > MetaRefreshFollowLimit)
        {
            RenderLogger.LogDebug(LogCategory.JavaScript, "Browser.navigation",
                $"{navigation.Url} not followed: it asks to wait {navigation.Delay.TotalSeconds:0.#} s, which is a notice to read rather than a redirect");
            return false;
        }

        if (hop >= MaxScriptNavigations)
        {
            RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.navigation",
                $"{navigation.Url} not followed: {MaxScriptNavigations} script navigations already followed for this one, which is a page navigating in a loop rather than a redirect chain");
            return false;
        }

        // A request for the URL already loaded means different things from different methods. From
        // reload() it is what the page asked for and is followed. From a form submission it is the
        // ordinary case — a form with no action submits to its own page, and the request that
        // results is not the one already made: a GET carries a new query and a POST a body. From
        // assign/replace it is a page re-stating where it is, and following it would fetch the same
        // bytes to run the same script to ask again.
        if (navigation.Kind is not (NavigationKind.Reload or NavigationKind.FormSubmit)
            && navigation.Document is null
            && string.Equals(navigation.Url, currentUrl, StringComparison.OrdinalIgnoreCase))
        {
            RenderLogger.LogDebug(LogCategory.JavaScript, "Browser.navigation",
                $"{navigation.Url} not followed: it is the document already loaded");
            return false;
        }

        // The same test one level up, because exact equality is not the shape the loop actually
        // takes. Google's search bootstrap re-navigates to the page it is already on with one more
        // token in the query each round — `sei`, then a `sg_ss` signal blob — so every hop has a
        // URL nobody has seen before and the check above never fires. What repeats is the path.
        //
        // Following that is useless rather than merely expensive: the round never converges for this
        // engine, so every extra hop costs a request and buys nothing. google.de answers 429 to it,
        // and — measured — answers 429 to three hops just as it did to ten, so the number was never
        // what it was objecting to. The budget is here to stop paying for a conversation that is not
        // going anywhere, not to stay under a rate limit.
        string targetPath = NavigationPathKey(navigation.Url);
        if (loadsPerPath.TryGetValue(targetPath, out int loaded) && loaded >= SamePathLoadLimit)
        {
            RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.navigation",
                $"{navigation.Url} not followed: {targetPath} has been loaded {loaded} times already, so this is a page re-submitting itself rather than a chain moving on");
            return false;
        }

        RenderLogger.LogDebug(LogCategory.JavaScript, "Browser.navigation",
            $"following {navigation.Kind} to {navigation.Url} from {currentUrl}");
        return true;
    }

    /// <summary>
    /// The request a navigation actually makes. Everything but a form submission is a GET of the URL
    /// it names; a form submission has to be serialized out of <paramref name="pageHtml"/> first, and
    /// yields <c>null</c> when the named form is not in it.
    /// </summary>
    /// <remarks>
    /// A fresh <see cref="HtmlFormState"/> per call, and correct because of when this runs: control
    /// overrides hold what a user typed, and during a load nobody has typed anything — the page is
    /// not interactive yet, and the previous page's state was reset at navigation. The viewport's
    /// live state is used for a submission that happens after load, where it does hold something.
    /// </remarks>
    internal static PageRequest? ToPageRequest(NavigationRequest navigation, string pageHtml, string baseUrl) =>
        ToPageRequest(navigation, pageHtml, baseUrl, document: null);

    /// <summary>
    /// <see cref="ToPageRequest(NavigationRequest, string, string)"/>, for a navigation of
    /// <paramref name="document"/>: the request is initiated by the document the bridge recorded as
    /// asking (<see cref="NavigationRequest.Initiator"/>), or by <paramref name="document"/> when the
    /// navigation names none.
    /// </summary>
    internal static PageRequest? ToPageRequest(
        NavigationRequest navigation,
        string pageHtml,
        string baseUrl,
        DocumentRequestContext? document) =>
        BuildRequest(navigation, new HtmlFormState(), pageHtml, baseUrl, document);

    private static PageRequest? BuildRequest(
        NavigationRequest navigation,
        HtmlFormState formState,
        string pageHtml,
        string baseUrl,
        DocumentRequestContext? document)
    {
        DocumentRequestContext? initiator = navigation.Initiator ?? document;
        if (!MayNavigateTo(initiator, navigation.Url))
        {
            RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.navigation",
                $"{navigation.Url} not followed: a web page may not open a local file");
            return null;
        }

        PageRequest? request;
        if (navigation.Kind == NavigationKind.FormSubmit && navigation.Submission is { } submission)
        {
            // The page's own form, as the page encoded it from what the form holds: the files its inputs have, its
            // selects' options, its checkboxes, what a script changed after the user (NavigationRequest.Submission).
            // Built again here from the markup and the window's record of the user's choices, it sent the files
            // read from disk where the user picked them and the option the user chose, whatever the page did since.
            request = submission.Body is { } submitted
                ? new PageRequest(submission.Url, PageRequest.Post, submission.BodyContentType) { BinaryBody = submitted }
                : PageRequest.ForUrl(submission.Url);
        }
        else if (navigation.Kind != NavigationKind.FormSubmit || navigation.FormIndex < 0)
        {
            // A javascript: URL's string is the document itself, under the policy of the document it replaces; a
            // frame's form the bridge encoded is a request for the URL it built -- a GET with its entries in it, or
            // a POST of the body it encoded (DomBridge/FrameSubmission.cs).
            request = navigation.Body is { } body
                ? new PageRequest(navigation.Url, PageRequest.Post, navigation.BodyContentType) { BinaryBody = body }
                : PageRequest.ForUrl(navigation.Url) with
                {
                    InlineDocument = navigation.Document,
                    InheritedPolicy = navigation.Document is null ? null : navigation.InheritedPolicy,
                };
        }
        else
        {
            request = formState.TryBuildScriptSubmitRequest(
                pageHtml, navigation.FormIndex, baseUrl, navigation.Url, navigation.SubmitterIndex,
                (navigation.SubmitterX, navigation.SubmitterY), navigation.FormDataEdits);
            if (request is null)
            {
                RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.navigation",
                    $"form.submit() not followed: form {navigation.FormIndex} is not in the document the host has");
                return null;
            }
        }

        // A page's own navigation, never the user's: the document that asked is its initiator, which
        // is what keeps a cross-site page from sending the target's Strict cookies by navigating.
        return request with
        {
            Initiator = initiator,
            NavigationType = navigation.Kind switch
            {
                NavigationKind.FormSubmit => PageNavigationType.FormSubmission,
                NavigationKind.MetaRefresh => PageNavigationType.MetaRefresh,
                _ => PageNavigationType.Script,
            },
        };
    }

    /// <summary>
    /// Whether a navigation <paramref name="initiator"/> started may go to <paramref name="targetUrl"/>:
    /// anything may, except a document that is not itself a local file opening a <c>file:</c> URL.
    /// </summary>
    /// <remarks>
    /// A browser never lets a web page navigate to a local file. Here a page's script, refresh meta,
    /// link or form could: the local page then ran with a <c>file:</c> document's privileges (which may
    /// read local files), and a <c>file://host/share</c> URL made Windows open an SMB session to that
    /// host with the user's credentials. The user's own navigations -- typed, a bookmark, the command
    /// line -- have no initiator and are unaffected.
    /// </remarks>
    internal static bool MayNavigateTo(DocumentRequestContext? initiator, string targetUrl) =>
        initiator is null ||
        initiator.DocumentUrl.IsFile ||
        !(Uri.TryCreate(targetUrl, UriKind.Absolute, out Uri? target) && target.IsFile);

    /// <summary>
    /// The URLs a load passed through, in order: its redirect chain, or <paramref name="finalUrl"/>
    /// alone for a load that recorded none.
    /// </summary>
    internal static IReadOnlyList<string> HopUrls(PageLoadResult response, string finalUrl)
    {
        var urls = response.RedirectChain.Select(url => url.AbsoluteUri).ToList();
        if (!urls.Contains(finalUrl, StringComparer.OrdinalIgnoreCase))
            urls.Add(finalUrl);
        return urls;
    }

    /// <summary>
    /// A URL reduced to everything ahead of its query: the part that stays the same while a page
    /// re-submits itself, and changes when a chain actually moves on.
    /// </summary>
    internal static string NavigationPathKey(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? uri.GetLeftPart(UriPartial.Path)
            : url;

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
        /// <summary>The window's history, for the <c>:visited</c> of the loaded page's links.</summary>
        internal Func<Uri, bool> VisitedLinks => app.IsVisited;

        /// <summary>
        /// The share of the settle's own running time that may go on producing frames.
        /// </summary>
        /// <remarks>
        /// A frame costs a serialise and a full parse. A parse used to re-fetch the document's
        /// stylesheets and web fonts every time (<c>docs/browser-load-window-pump.md</c>, "what this
        /// does not fix"), which on a page carrying several is seconds; the frames now share one
        /// subresource cache (<see cref="LastFrame"/>), so what remains is the parse itself. Paying that per batch took
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
        /// The container of the last frame published, whose subresource cache the next frame and the
        /// finished page share (<see cref="HtmlContainer.ShareSubresourceCacheWith"/>): every one of
        /// them is the same document, and a container of its own would fetch each image again, with
        /// its cookies, during a layout on the UI thread. Sharing is refused for another document, so
        /// a frame of an earlier hop of the navigation shares nothing.
        /// </summary>
        public HtmlContainer? LastFrame { get; private set; }

        /// <summary>
        /// The window's page area, which a container parsed on the load worker resolves its media
        /// queries against (<see cref="BrowserViewport.PageAreaSize"/>).
        /// </summary>
        public SizeF? Viewport => app._viewport.PageAreaSize;

        /// <summary>
        /// Offers the document reached after one batch of the load window. Called on the load
        /// worker; returns without serialising when the previous frame has not been painted yet or
        /// when frames have used up their share of the settle.
        /// </summary>
        /// <remarks>
        /// The frame is the same document as the final one, so its container loads its stylesheets,
        /// fonts and images as that document's requests, through the profile's network.
        /// </remarks>
        public void PublishFrame(Func<string> serialize, string url, DocumentRequestContext document)
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
                container = BrowserViewport.CreateContentContainer(
                    PrepareForBrowsing(html), url, app._network, document, LastFrame, Viewport, app.IsVisited);
                LastFrame = container;
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
        // The history entry was written by NavigateTo before the load, so it names where the
        // navigation started, not where a redirect or a script sent it. Point it at the document
        // actually loaded: reload and back/forward re-issue that entry, and re-issuing the start would
        // walk the user through the interstitial again instead of back past it. One followed chain is
        // one navigation, which is also why the hops in between get no entries of their own.
        //
        // The entry is the loaded document's request (PageRequest.ForLoadedDocument): at the final
        // URL, with the same-site status a UI reload replays. It keeps a POST body only while the
        // request that produced the document was still a POST — a submission answered with a redirect
        // to a GET leaves a GET, and one answered in place is still the submission, which revisiting
        // asks before repeating.
        string startedAt = CurrentHistoryUrl();
        if (result.HistoryEntry is { } loaded
            && _historyIndex >= 0
            && _historyIndex < _history.Count)
        {
            // A javascript: URL's document keeps the entry its URL's: reloading it fetches the URL.
            _history[_historyIndex] = loaded with { InlineDocument = null, InheritedPolicy = null };
        }

        SetUrlText(result.NormalisedUrl);
        _viewport.ReplacePage(result.TakeContainer(), result.TakeSession(), result.NormalisedUrl, result.DocumentHtml);
        NoteVisited(result.NormalisedUrl);
        NoteVisited(startedAt);

        // The document's entry is the current one; the page counts the window's entries around it in its
        // history.length, and may go back or forward to them.
        _documentFirstEntry = _documentLastEntry = _historyIndex;
        _viewport.SetSessionHistory(_historyIndex, _history.Count - 1 - _historyIndex);

        // HTML §7.4.6.4: a page navigated to with a fragment opens scrolled to it. The request the
        // navigation started with carries the fragment when the loaded URL lost it on the way.
        if ((FragmentOf(result.NormalisedUrl) ?? FragmentOf(startedAt)) is { } fragment)
            _viewport.ScrollToFragment(fragment);

        // Back or forward to this document: where its entry was left, as Chromium restores it, fragment
        // or not.
        if (_pendingScrollRestore is { } restore)
        {
            _pendingScrollRestore = null;
            _viewport.RestoreScroll(restore);
        }

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

        if (ChooseFile is { } choose)
        {
            CompleteFilePick(e, choose(e));
            return;
        }

        StandardFileDialog dialog = new()
        {
            Mode = UiFileDialogMode.Open,
            CurrentDirectory = Environment.CurrentDirectory,
            PreferredSize = FileDialogPreferredSize,
            Title = e.AllowsMultiple ? "Add a file" : "Choose a file",
        };

        dialog.ResultCompleted += (_, result) =>
        {
            CompleteFilePick(e, result.Result.Kind == UiDialogResultKind.Accepted &&
                                !string.IsNullOrWhiteSpace(result.Result.Value)
                ? result.Result.Value
                : null);
        };

        dialog.ShowOpenModal(_rootWindow, GetDialogPlacement(FileDialogPreferredSize));
        _host.RequestInvalidate();
    }

    private static readonly BSize FileDialogPreferredSize = new(560, 380);

    /// <summary>
    /// What the file dialog answered for <paramref name="pick"/>: the path chosen, which the window records for
    /// its submission and the page's input takes with its events; or null for a dialog closed without one,
    /// which the page's input hears as <c>cancel</c>.
    /// </summary>
    private void CompleteFilePick(HtmlFilePickEventArgs pick, string? path)
    {
        if (path is null)
            _viewport.CancelFilePick(pick.FileInputIndex);
        else
            _viewport.RecordPickedFile(pick.ControlId, pick.ControlName, path, pick.AllowsMultiple, pick.FileInputIndex);

        _host.RequestInvalidate();
    }

    /// <summary>
    /// Stands in for the file dialog when set: answers the path chosen for a pick, or null for one the user
    /// cancelled. The shell's own dialog when unset, as it always is outside a test.
    /// </summary>
    internal Func<HtmlFilePickEventArgs, string?>? ChooseFile { get; set; }

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

        // Only the URL is resolved against the page; a submission's body travels as-is, and so do
        // the initiator and navigation type the viewport stamped on it.
        PageRequest request = e.Request with { Url = ResolveLinkUrl(e.Link) };
        if (!MayNavigateTo(request.Initiator, request.Url))
        {
            RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.navigation",
                $"{request.Url} not followed: a web page may not open a local file");
            return;
        }

        NavigateTo(request);
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

        // A link with a scheme is absolute as it stands. Uri's own test is not enough: on Unix it takes a
        // root-relative "/next" for a rooted file path (file:///next) and "//host/a" for a UNC one, so on Linux
        // such a link stayed unresolved and was then refused as a web page opening a local file.
        if (HasUrlScheme(link) && Uri.TryCreate(link, UriKind.Absolute, out _))
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

    /// <summary>
    /// Whether <paramref name="reference"/> starts with a URL scheme and its colon (RFC 3986: a letter, then
    /// letters, digits, <c>+</c>, <c>-</c> or <c>.</c>), as an absolute URL does. A root-relative <c>/path</c>
    /// and a protocol-relative <c>//host/path</c> have none, though <see cref="Uri"/> takes both for files on
    /// Unix.
    /// </summary>
    internal static bool HasUrlScheme(string reference)
    {
        int colon = reference.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(reference[0]))
            return false;

        for (int i = 1; i < colon; i++)
        {
            char c = reference[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                return false;
        }

        return true;
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
            button.Clicked += (_, _) => NavigateTo(PageRequest.ForUrl(favUrl) with { NavigationType = PageNavigationType.Bookmark });
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
            string? documentHtml,
            bool followedNavigation,
            PageRequest? historyEntry,
            Exception? error)
        {
            NormalisedUrl = normalisedUrl;
            _container = container;
            _session = session;
            DocumentHtml = documentHtml;
            FollowedNavigation = followedNavigation;
            HistoryEntry = historyEntry;
            Error = error;
        }

        /// <summary>
        /// The document the container shows, as the session serialised it, or null when the page has no
        /// session: what the viewport compares the session's document with before it parses it again.
        /// </summary>
        public string? DocumentHtml { get; }

        /// <summary>The URL the document was served from, after every redirect and followed navigation.</summary>
        public string NormalisedUrl { get; }

        /// <summary>
        /// The request that names the loaded document, for the history entry the navigation made:
        /// see <see cref="PageRequest.ForLoadedDocument"/>.
        /// </summary>
        public PageRequest? HistoryEntry { get; }

        /// <summary>
        /// Whether the document loaded is somewhere a script sent the browser rather than where the
        /// navigation started. The history entry was written before the load and names the start, so
        /// this is what says it needs correcting.
        /// </summary>
        public bool FollowedNavigation { get; }

        public Exception? Error { get; }

        public static NavigationLoadResult FromSuccess(
            string normalisedUrl,
            HtmlContainer container,
            InteractiveSession? session,
            string? documentHtml,
            bool followedNavigation,
            PageRequest historyEntry) =>
            new(normalisedUrl, container, session, documentHtml, followedNavigation, historyEntry, null);

        public static NavigationLoadResult FromError(Exception error) =>
            new(string.Empty, null, null, null, false, null, error);

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

        /// <summary>Below this width the window drops its favorites bar and the buttons it can spare.</summary>
        private const double CompactWidth = 600;

        /// <summary>
        /// The page area a window of size <paramref name="window"/> leaves between its toolbars and its
        /// status bar, as <see cref="ArrangeCore"/> gives it.
        /// </summary>
        internal static SizeF PageAreaFor(BSize window)
        {
            double favoritesHeight = window.Width < CompactWidth ? 0 : FavoritesBarHeight;
            return new((float)window.Width, (float)Math.Max(0, window.Height - ToolbarHeight - favoritesHeight - StatusBarHeight));
        }

        /// <summary>The inverse of <see cref="PageAreaFor"/>: the window that leaves this page area.</summary>
        internal static BSize WindowSizeFor(double pageWidth, double pageHeight) =>
            new(pageWidth, pageHeight + ToolbarHeight + (pageWidth < CompactWidth ? 0 : FavoritesBarHeight) + StatusBarHeight);

        private const double Margin = 8;
        private const double ControlHeight = 28;
        private const double NavButtonWidth = 38;
        private const double StopButtonWidth = 58;
        private const double GoButtonWidth = 54;
        private const double StarWidth = 62;
        private const double CacheButtonWidth = 62;
        private const double MinWidth = 720;
        private const double MinHeight = 480;

        private readonly StandardButton _backButton;
        private readonly StandardButton _forwardButton;
        private readonly StandardButton _refreshButton;
        private readonly StandardButton _stopButton;
        private readonly StandardEdit _address;
        private readonly StandardButton _starButton;
        private readonly StandardButton _goButton;

        /// <summary>
        /// The optional maintenance control, present only in builds that have something to
        /// maintain.
        /// </summary>
        /// <remarks>
        /// <b>Nullable rather than conditionally compiled.</b> The clear-code-cache command exists
        /// only under Debug-VM/Release-VM, where the VM script engine and its on-disk store are in
        /// the graph at all. Wrapping a constructor parameter and four layout sites in
        /// <c>#if</c> would make this element's shape differ by build configuration, which is a bad
        /// thing for layout arithmetic to do; an absent optional control is something the arrange
        /// pass already knows how to express.
        /// </remarks>
        private readonly StandardButton? _cacheButton;

        private readonly BrowserViewport _viewport;
        private readonly StandardLabel _status;
        private readonly List<StandardButton> _favorites = [];
        private bool _isCompact;

        /// <summary>The width the cache control claims, which is none when there is not one.</summary>
        private double CacheWidthWhenShown => _cacheButton is null ? 0 : CacheButtonWidth + Margin;

        public BrowserContent(
            StandardButton backButton,
            StandardButton forwardButton,
            StandardButton refreshButton,
            StandardButton stopButton,
            StandardEdit address,
            StandardButton starButton,
            StandardButton goButton,
            StandardButton? cacheButton,
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
            _cacheButton = cacheButton;
            _viewport = viewport;
            _status = status;

            AddChild(_backButton);
            AddChild(_forwardButton);
            AddChild(_refreshButton);
            AddChild(_stopButton);
            AddChild(_address);
            AddChild(_starButton);
            AddChild(_goButton);
            if (_cacheButton is not null)
                AddChild(_cacheButton);
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
            double addressWidth = Math.Max(
                90,
                width - 4 * NavButtonWidth - StopButtonWidth - StarWidth - GoButtonWidth - CacheWidthWhenShown - 9 * Margin);
            BSize controlSize = new(double.PositiveInfinity, ControlHeight);

            _backButton.Measure(new BSize(NavButtonWidth, ControlHeight));
            _forwardButton.Measure(new BSize(NavButtonWidth, ControlHeight));
            _refreshButton.Measure(new BSize(NavButtonWidth + 24, ControlHeight));
            _stopButton.Measure(new BSize(StopButtonWidth, ControlHeight));
            _address.Measure(new BSize(addressWidth, ControlHeight));
            _starButton.Measure(new BSize(StarWidth, ControlHeight));
            _goButton.Measure(new BSize(GoButtonWidth, ControlHeight));
            _cacheButton?.Measure(new BSize(CacheButtonWidth, ControlHeight));
            foreach (StandardButton button in _favorites)
                button.Measure(controlSize);

            double viewportHeight = Math.Max(120, height - ToolbarHeight - FavoritesBarHeight - StatusBarHeight);
            _viewport.Measure(new BSize(width, viewportHeight));
            _status.Measure(new BSize(Math.Max(0, width - 2 * Margin), StatusBarHeight));
            return new BSize(width, height);
        }

        protected override void ArrangeCore(BRect finalRect)
        {
            bool compact = finalRect.Width < CompactWidth;
            _isCompact = compact;
            _forwardButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;
            _refreshButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;
            _stopButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;
            _starButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;
            if (_cacheButton is not null)
                _cacheButton.Visibility = compact ? UiVisibility.Collapsed : UiVisibility.Visible;

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

            double rightControls =
                (compact ? 0 : StarWidth + Margin) + (compact ? 0 : CacheWidthWhenShown) + GoButtonWidth + Margin;
            double addressWidth = Math.Max(90, finalRect.Right - Margin - x - rightControls);
            _address.Arrange(new BRect(x, y, addressWidth, controlHeight));
            x += addressWidth + Margin;
            if (!compact)
            {
                _starButton.Arrange(new BRect(x, y, StarWidth, ControlHeight));
                x += StarWidth + Margin;

                if (_cacheButton is not null)
                {
                    _cacheButton.Arrange(new BRect(x, y, CacheButtonWidth, ControlHeight));
                    x += CacheButtonWidth + Margin;
                }
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

        /// <summary>The page's scripts changed its document since the hosted controls were last set to it.</summary>
        private bool _controlsNeedSync;
        private HtmlContainer _container = CreateContentContainer(WelcomePage, string.Empty);
        private HtmlGraphicsRenderList? _renderList;

        /// <summary>The documents of the page's frames, which its display list does not paint.</summary>
        private readonly FrameCompositor _frames;

        /// <summary>The window's history, which a frame's links are <c>:visited</c> by too.</summary>
        public Func<Uri, bool>? VisitedLinkPredicate { get; set; }
        private InteractiveSession? _interactiveSession;
        private string? _lastAppliedHtml;

        // The session's RenderVersion when the document on screen was taken from it: a move shows the
        // page again only when the page has changed since.
        private long _appliedRenderVersion;

        // A run of presses at one place: when the last one was, where, and how many there have been, so
        // a page is told which press of a double click this is.
        private long _lastPressTicks;
        private PointF _lastPressPoint;
        private int _clickCount;

        // The buttons held, as MouseEvent.buttons' mask, and where the pointer last moved: a move to the
        // same place is not news to the page.
        private int _heldButtons;
        private PointF? _lastMovePoint;

        // Whether the page cancelled the key that is down, which keeps what it types from being typed.
        private bool _keyTextSuppressed;

        /// <summary>Whether the page acted on the last pointer input it was given itself (<see cref="PointerInputResult.Handled"/>).</summary>
        private bool _lastPointerHandled;

        /// <summary>The page's <see cref="InteractiveSession.FieldVersion"/> the field editor last followed.</summary>
        private long _followedFieldVersion = -1;

        /// <summary>The editor's selection as the page last heard it, so that only a change is reported.</summary>
        private (int Start, int End, bool Backward) _reportedSelection = (-1, -1, false);

        // The page's focus as the field editor last followed it (InteractiveSession.FocusVersion).
        private long _followedFocusVersion = -1;

        /// <summary>A press is part of a double click within this long of the last, as Windows' default.</summary>
        private const int DoubleClickMilliseconds = 500;

        /// <summary>... and this close to it, in CSS pixels.</summary>
        private const float DoubleClickDistance = 4;
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

        /// <summary>The page area's size as its last layout saw it; see <see cref="PageAreaSize"/>.</summary>
        private PageArea? _pageArea;

        /// <summary>
        /// The fragment of the URL the page was navigated to, until the element it names has been
        /// scrolled to, or the user scrolls first.
        /// </summary>
        private string? _pendingFragment;

        // Where the page's viewport was last seen scrolled, and where this view last told the page it was:
        // a page that scrolled itself since moves this view, and this view's own scroll moves the page's.
        private double _pageScrollSeen;
        private double _viewScrollReported;
        private bool _viewScrollReportedSinceTaken;

        private const double TouchPanThreshold = 6;

        private sealed record PageArea(float Width, float Height);

        /// <summary>
        /// The size of the page area in CSS pixels as its last layout saw it, or null before the first
        /// one. Read off the UI thread, by the load worker that parses the next page.
        /// </summary>
        /// <remarks>
        /// A container resolves media queries while it parses its document, and the viewport it
        /// resolves them against is its <c>MaxSize</c>, or its 99999px default page size when that is
        /// unset. The window set <c>MaxSize</c> only at its first layout, after the parse, so every page
        /// was styled for a 99999px screen and then laid out in the window: every <c>min-width</c>
        /// query matched and no <c>max-width</c> one did. MediaWiki's skin laid its widest-screen grid,
        /// sidebar columns and all, into a 1000px window.
        /// </remarks>
        internal SizeF? PageAreaSize => Volatile.Read(ref _pageArea) is { } area ? new SizeF(area.Width, area.Height) : null;

        /// <summary>Records an estimate of the page area until the first layout measures it.</summary>
        internal void SeedPageArea(SizeF size) =>
            Interlocked.CompareExchange(ref _pageArea, new PageArea(size.Width, size.Height), null);

        public BrowserViewport(Func<IBroilerRenderer?> getRenderer)
        {
            _getRenderer = getRenderer ?? throw new ArgumentNullException(nameof(getRenderer));
            _frames = new FrameCompositor((html, baseUrl, network, document, viewport) =>
                CreateContentContainer(html, baseUrl, network, document, viewport: viewport, visited: VisitedLinkPredicate));
            _container.LinkClicked += OnLinkClicked;
            _formEditor = new HtmlFormEditor(this);
            _formEditor.Committed += (_, _) => MarkLayoutDirty();
            _formEditor.SubmitRequested += (_, e) => SubmitHostedField(e.FieldId, e.FieldName);
            _controlHost = new HtmlFormControlHost(this, _formState);
            _controlHost.Changed += (_, _) => Invalidate(UiInvalidationKind.Render);
            _controlHost.FilePickRequested += (_, e) => FilePickRequested?.Invoke(this, e);

            // The user's choice in a hosted select is the page's select's too, with its input and change: the
            // option of a drop-down, every option of a multiple select's list.
            _controlHost.OptionChosen += (_, e) =>
            {
                if (_interactiveSession is { } page && page.SelectOptionByUser(e.SelectIndex, e.OptionIndex))
                    ShowPageIfChanged();
            };
            _controlHost.OptionsChosen += (_, e) =>
            {
                if (_interactiveSession is { } page && page.SelectOptionsByUser(e.SelectIndex, e.OptionIndexes))
                    ShowPageIfChanged();
            };
        }

        public event EventHandler<BrowserLinkEventArgs>? LinkActivated;

        /// <summary>The controls drawn over the page's own.</summary>
        internal IReadOnlyList<UiElement> HostedControls => _controlHost.Controls;

        /// <summary>Raised when a hosted file control was activated; the shell shows the dialog.</summary>
        public event EventHandler<HtmlFilePickEventArgs>? FilePickRequested;

        /// <summary>
        /// Records the file the shell's dialog returned and refreshes the control. A
        /// <c>multiple</c> input accumulates, so picking again adds to its selection
        /// instead of replacing it — the dialog chooses one file at a time. The page's input
        /// <paramref name="fileInputIndex"/> takes what the control holds now, with its input and change.
        /// </summary>
        public void RecordPickedFile(string controlId, string controlName, string path, bool allowsMultiple, int fileInputIndex = -1)
        {
            if (allowsMultiple)
                _formState.AddSelectedFile(controlId, controlName, path);
            else
                _formState.SetSelectedFile(controlId, controlName, path);

            _controlHost.RefreshFileLabels();

            if (_interactiveSession is { } page && fileInputIndex >= 0)
            {
                List<ChosenFile> files = [];
                foreach (string chosen in _formState.GetSelectedFiles(controlId, controlName))
                {
                    if (PageFiles.Read(chosen) is { } file)
                        files.Add(file);
                }

                if (page.SetFilesByUser(fileInputIndex, files))
                    ShowPageIfChanged();
            }
        }

        /// <summary>The shell's dialog for the page's file input <paramref name="fileInputIndex"/> closed without a file: the input hears <c>cancel</c>.</summary>
        public void CancelFilePick(int fileInputIndex)
        {
            if (_interactiveSession is { } page && fileInputIndex >= 0 && page.CancelFilePickByUser(fileInputIndex))
                ShowPageIfChanged();
        }

        public string BaseUrl { get; private set; } = string.Empty;

        /// <summary>
        /// The request context of the document on screen, or <see langword="null"/> for the browser's
        /// own pages (welcome, loading, error). It initiates every navigation the page starts — a link,
        /// a form, a script — so the transport judges those as the page's, not the user's.
        /// </summary>
        public DocumentRequestContext? DocumentContext => _container.DocumentContext;

        // The load window, not "are any timers queued at all" — see
        // InteractiveSession.HasWorkDueInLoadWindow. This drives the busy state, the 16 ms
        // animation tick and StopSession, and on a page holding an interval the unbounded
        // question never goes false: the browser would step JS and re-lay out the document on
        // the UI thread for as long as the window stayed open, which is what made google.com
        // hang while the CLI (which asks the bounded question) rendered it.
        public bool HasPendingWork => _interactiveSession?.HasWorkDueInLoadWindow == true;

        /// <param name="documentHtml">
        /// The document <paramref name="container"/> was parsed from, as <paramref name="interactiveSession"/>
        /// serialised it -- so the page is not parsed again until its scripts change it -- or null.
        /// </param>
        public void ReplacePage(HtmlContainer container, InteractiveSession? interactiveSession, string baseUrl, string? documentHtml = null)
        {
            ArgumentNullException.ThrowIfNull(container);
            StopSession();
            _formEditor.Cancel();
            _formState.Reset();
            _controlHost.Clear();
            _controlsDirty = true;
            DisposeRenderList();
            _frames.Clear();
            _container.LinkClicked -= OnLinkClicked;
            _container.Dispose();

            _container = container;
            _container.LinkClicked += OnLinkClicked;
            _interactiveSession = interactiveSession;
            _formState.PageHoldsState = interactiveSession is not null;
            _lastAppliedHtml = documentHtml;
            _appliedRenderVersion = interactiveSession?.RenderVersion ?? 0;
            BaseUrl = baseUrl ?? string.Empty;
            _scrollY = 0;
            _pageScrollSeen = 0;
            _viewScrollReported = 0;
            _pendingFragment = null;
            _viewportZoom = 1f;
            MarkLayoutDirty();
        }

        public bool StepAnimation()
        {
            if (_interactiveSession is null || !_interactiveSession.HasWorkDueInLoadWindow)
                return false;

            string? html = _interactiveSession.Step();
            ApplyPageDocument(html);

            // The session stays when nothing is left to step: the page's scripts live on in it, for
            // the next thing the user does to the page.
            return html is not null;
        }

        /// <summary>
        /// Shows <paramref name="html"/>, the page's document as its scripts left it, when it differs
        /// from what is on screen.
        /// </summary>
        /// <remarks>
        /// Re-parsing costs a full parse and layout of the document, so it is worth paying only when
        /// the scripts actually changed it. A callback batch that touched no DOM — a timer that only
        /// reads, schedules, or measures — still returns the serialised document, and google.com runs
        /// many of those.
        /// </remarks>
        private void ApplyPageDocument(string? html)
        {
            _appliedRenderVersion = _interactiveSession?.RenderVersion ?? 0;
            if (string.IsNullOrWhiteSpace(html) || string.Equals(html, _lastAppliedHtml, StringComparison.Ordinal))
                return;

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

            _controlsNeedSync = true;
            MarkLayoutDirty();
        }

        /// <summary>
        /// The navigation the live page has asked for since the last step, as the request it makes,
        /// or <c>null</c> when it has asked for none.
        /// </summary>
        /// <remarks>
        /// The load loop reads the pending navigation while a page is loading; this is the same
        /// question after it has loaded, and it is the one that matters for <c>form.submit()</c> —
        /// the usual shape is a user filling a form and a click handler submitting it, which happens
        /// long after the load window closed. It uses the viewport's own form state, because by now
        /// the overrides in it are what the user actually typed.
        /// </remarks>
        public PageRequest? TakePendingNavigation()
        {
            // Consuming, so a request only ever answers once. The load decided about everything the
            // page asked for before it finished; what reaches here is what it asked for since.
            NavigationRequest? pending = _interactiveSession?.TakePendingNavigation();
            return pending is null ? null : BuildRequest(pending, _formState, GetPageHtml(), BaseUrl, DocumentContext);
        }

        /// <summary>What the page's session history did since the window last asked (InteractiveSession.TakeHistoryChanges).</summary>
        public IReadOnlyList<HistoryChange> TakeHistoryChanges() => _interactiveSession?.TakeHistoryChanges() ?? [];

        /// <summary>Back or forward by <paramref name="delta"/> to one of the page's own entries; false when the page has none there.</summary>
        public bool TraverseHistory(int delta)
        {
            if (_interactiveSession is not { } page || !page.TraverseHistory(delta))
                return false;

            ShowPageIfChanged();
            return true;
        }

        /// <summary>Tells the page how many of the window's history entries surround its own.</summary>
        public void SetSessionHistory(int before, int after) => _interactiveSession?.SetSessionHistory(before, after);

        /// <summary>A <c>javascript:</c> URL the user followed: its script runs in the page, if the page's scripts run.</summary>
        public bool RunJavaScriptUrl(string url) => _interactiveSession?.RunJavaScriptUrl(url) == true;

        public void StopSession()
        {
            _interactiveSession?.Dispose();
            _interactiveSession = null;
            _formState.PageHoldsState = false;
        }

        public void ReleaseGraphicsResources()
        {
            DisposeRenderList();
            _frames.ReleaseRenderLists();
            _layoutDirty = true;
            _renderDirty = true;
        }

        /// <summary>
        /// Parses <paramref name="html"/> into a container for the viewport.
        /// </summary>
        /// <param name="html">The markup, prepared for browsing.</param>
        /// <param name="baseUrl">The document's URL, which relative references resolve against.</param>
        /// <param name="network">
        /// The profile's network for a page's container, or <see langword="null"/> for the browser's own
        /// pages, which load nothing from the network as anyone's document.
        /// </param>
        /// <param name="document">The page's request context; given exactly when <paramref name="network"/> is.</param>
        /// <param name="sameDocument">
        /// Another container of the same document (an earlier frame of its load), whose images,
        /// stylesheets and fonts this one reuses rather than fetching them again; <see langword="null"/>
        /// for none. Ignored unless it holds the same <paramref name="network"/> and <paramref name="document"/>.
        /// </param>
        /// <remarks>
        /// Both are set before the parse, because the parse is what loads the linked stylesheets and
        /// <c>@font-face</c> fonts; images follow at layout. Every one of them is a sub-resource request
        /// of <paramref name="document"/> on <paramref name="network"/> — the same container re-parsed
        /// by a step of the page's scripts keeps both — and none goes through a client of its own.
        /// </remarks>
        /// <param name="visited">The window's history, for <c>:visited</c>; null for none.</param>
        /// <param name="targetFragment">
        /// The fragment the page was opened at, whose element is <c>:target</c>, for a page no script
        /// runs; a page whose scripts run has the bridge mark it.
        /// </param>
        public static HtmlContainer CreateContentContainer(
            string html,
            string baseUrl,
            IBrowserRequestTransport? network = null,
            DocumentRequestContext? document = null,
            HtmlContainer? sameDocument = null,
            SizeF? viewport = null,
            Func<Uri, bool>? visited = null,
            string? targetFragment = null)
        {
            HtmlContainer container = new()
            {
                AvoidAsyncImagesLoading = true,
                AvoidImagesLateLoading = true,
                BaseUrl = baseUrl,
                RequestTransport = network,
                DocumentContext = document,
                VisitedLinkPredicate = visited,
                TargetFragment = targetFragment,
                // The scripting bridge leaves a box placed by position-area, anchor() or position-try to the
                // renderer, and names a popover's implicit anchor; without this, such a box was drawn where a
                // box with no anchor goes.
                PlacesAnchoredBoxes = true,
            };

            // Before the parse, which resolves the document's media queries against it
            // (PageAreaSize says why). Layout sets it again at whatever size the window has then.
            if (viewport is { Width: > 0, Height: > 0 } size)
                container.MaxSize = size;

            if (sameDocument is not null)
                container.ShareSubresourceCacheWith(sameDocument);
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
            RenderListReplay.Replay(context.RenderList, htmlList.Commands);
            _frames.Replay(context.RenderList, _container.ScrollOffset);
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
        /// Discovers the page's checkbox, radio and select controls once per page, and sets them to what the
        /// page's scripts did to them since. The parse is the expensive half, so it is deferred to the first
        /// render after the page changed rather than repeated per layout.
        /// </summary>
        private void RebuildHostedControlsIfNeeded()
        {
            if (_controlsDirty)
            {
                _controlsDirty = false;
                _controlsNeedSync = false;
                _controlHost.Rebuild(GetPageHtml());
                return;
            }

            if (_controlsNeedSync)
            {
                _controlsNeedSync = false;
                _controlHost.Sync(GetPageHtml());
            }
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
                _frames.Dispose();
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
            EnsureLayout();

            FollowPageScroll();
            if (_pendingFragment is { } fragment)
                TryScrollToFragment(fragment);

            ClampScroll(viewportHeight);
            ReportViewScroll();
            if (_renderDirty || _renderList is null)
            {
                _container.ScrollOffset = new PointF(0, -_scrollY / _viewportZoom);
                DisposeRenderList();
                _renderList = HtmlGraphicsRenderListBuilder.Build(
                    renderer,
                    _container.CreateDisplayList(),
                    new RectangleF(0, 0, viewportWidth, viewportHeight));
                _frames.Update(renderer, _container);
                _renderDirty = false;
            }

            return _renderList.RenderList;
        }

        /// <summary>
        /// Lays the page out at the viewport's size when it has changed since the last layout: before a
        /// render, and before the field editor looks for a field in the layout.
        /// </summary>
        private void EnsureLayout()
        {
            float viewportWidth = (float)Math.Max(0, Bounds.Width);
            float viewportHeight = (float)Math.Max(0, Bounds.Height);
            BSize viewportSize = new(viewportWidth, viewportHeight);
            if (Bounds.IsEmpty)
                return;

            if (_layoutDirty || viewportSize != _lastLayoutSize)
            {
                _container.Location = PointF.Empty;
                _container.MaxSize = new SizeF(viewportWidth, viewportHeight);
                _container.PerformLayout(new RectangleF(0, 0, viewportWidth, viewportHeight));
                _contentHeight = _container.ActualSize.Height * _viewportZoom;
                _layoutDirty = false;
                _renderDirty = true;
                if (viewportSize != _lastLayoutSize)
                {
                    Volatile.Write(ref _pageArea, new PageArea(viewportWidth, viewportHeight));

                    // The page's scripts, and the hit test of the user's next click, measure the page
                    // at the size it is shown at.
                    _interactiveSession?.SetViewport((int)Math.Round(viewportWidth), (int)Math.Round(viewportHeight));
                }

                _lastLayoutSize = viewportSize;
            }
        }

        /// <remarks>
        /// <para>
        /// <b>The page's scripts hear it first.</b> A press and a release are delivered to them as a
        /// browser delivers them (<see cref="InteractiveSession.DispatchPointer"/>): hit-tested against
        /// the page's layout, into a frame where the pointer is over one, and fired as trusted pointer,
        /// mouse and click events. Nothing the user did reached a page's scripts before: the window
        /// selected text and followed links itself, so a button with a click listener -- reCAPTCHA's
        /// checkbox, a <c>span</c> in a frame -- did nothing at all.
        /// </para>
        /// <para>
        /// <b>Then the window does what it does by default, unless the page cancelled it:</b> no text
        /// selection for a cancelled press, no link followed for a cancelled click. The document the
        /// scripts changed is shown after that, so the window's own handling of the release sees the
        /// document the press was on.
        /// </para>
        /// </remarks>
        private bool HandlePointerButton(UiInputEvent input)
        {
            PointF point = ToLocalPoint(input.Position);
            bool left = input.MouseButton == MouseButton.Left;
            bool right = input.MouseButton == MouseButton.Right;

            if (input.MouseButtonTransition == MouseButtonTransition.Down)
            {
                // The page hears the press first, a press on a text field included -- it sees the field
                // focused -- and a press it cancels neither edits a field nor starts a selection.
                _heldButtons |= ButtonMask(input.MouseButton);
                bool cancelled = DeliverPointer(input, point, PointerInputKind.Down);

                // A click on a text field starts editing instead of a text selection. Its release goes to
                // the field's editor rather than here, and reaches the page through it
                // (TryDispatchThroughPage); the press ends now, and what the page did with it -- a focus
                // listener's changes -- is shown now.
                if (left && !cancelled && BeginFormEdit(point))
                {
                    _heldButtons &= ~ButtonMask(input.MouseButton);
                    if (_interactiveSession is { } editedPage)
                        _followedFocusVersion = editedPage.FocusVersion;
                    ApplyPageDocument(_interactiveSession?.CurrentHtml());
                    InvalidateRenderedContent();
                    return true;
                }

                // The editor closes -- unless the page kept its focus on the field by cancelling the press.
                if (!cancelled || _interactiveSession is null)
                {
                    _formEditor.Commit();
                    Session?.SetFocus(this);
                }

                if (!cancelled)
                    _container.HandleMouseDown(point, left, right);

                // The press may have moved the page's focus onto a field the window edits: one a
                // mousedown listener focused.
                if (_interactiveSession is { } page)
                    FollowPageFocus(page);

                // What the press changed is shown at the release: parsing the page again now would
                // lose the press the container is holding, and with it the link the release follows.
                InvalidateRenderedContent();
                return true;
            }

            if (input.MouseButtonTransition == MouseButtonTransition.Up)
            {
                _heldButtons &= ~ButtonMask(input.MouseButton);
                bool cancelled = DeliverPointer(input, point, PointerInputKind.Up);

                // The release still ends a text selection; a click the page cancelled follows no link, and
                // one the page acted on itself -- validated and submitted a form, or reset it -- submits
                // nothing of the window's.
                _suppressNavigation = cancelled || _lastPointerHandled;
                try
                {
                    _container.HandleMouseUp(point, left, right);
                }
                finally
                {
                    _suppressNavigation = false;
                }

                ApplyPageDocument(_interactiveSession?.CurrentHtml());
                InvalidateRenderedContent();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Delivers a press, a release or a move at <paramref name="point"/> to the page's scripts, if it
        /// has any. Answers whether they cancelled what it does by default.
        /// </summary>
        private bool DeliverPointer(UiInputEvent input, PointF point, PointerInputKind kind)
        {
            if (_interactiveSession is not { } session)
                return false;

            int button = input.MouseButton switch
            {
                MouseButton.Right => 2,
                MouseButton.Middle => 1,
                _ => 0,
            };

            if (kind == PointerInputKind.Down)
            {
                long now = Environment.TickCount64;
                bool again = _clickCount > 0
                    && now - _lastPressTicks <= DoubleClickMilliseconds
                    && Math.Abs(point.X - _lastPressPoint.X) <= DoubleClickDistance
                    && Math.Abs(point.Y - _lastPressPoint.Y) <= DoubleClickDistance;
                _clickCount = again ? _clickCount + 1 : 1;
                _lastPressTicks = now;
                _lastPressPoint = point;
            }

            // Page coordinates: the point in the viewport, in CSS pixels, plus how far the page is scrolled.
            float scrollY = _scrollY / _viewportZoom;
            KeyboardModifierState modifiers = input.KeyModifiers;
            try
            {
                _lastPointerHandled = false;
                PointerInputResult result = session.DispatchPointer(new PointerInput(kind, point.X, point.Y + scrollY)
                {
                    ScrollY = scrollY,
                    Button = button,
                    Buttons = _heldButtons,
                    ClickCount = Math.Max(1, _clickCount),
                    CtrlKey = modifiers.HasFlag(KeyboardModifierState.Control),
                    ShiftKey = modifiers.HasFlag(KeyboardModifierState.Shift),
                    AltKey = modifiers.HasFlag(KeyboardModifierState.Alt),
                    MetaKey = modifiers.HasFlag(KeyboardModifierState.LeftWindows) || modifiers.HasFlag(KeyboardModifierState.RightWindows),
                });
                _lastPointerHandled = result.Handled;
                return result.DefaultPrevented;
            }
            catch (Exception ex)
            {
                // A page whose scripts fail on a click must not cost the user the window's own handling of it.
                RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.input", $"Delivering pointer input to the page failed: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>A button as <c>MouseEvent.buttons</c>' bit: 1 the main one, 2 the secondary, 4 the middle.</summary>
        private static int ButtonMask(MouseButton button) => button switch
        {
            MouseButton.Right => 2,
            MouseButton.Middle => 4,
            _ => 1,
        };

        /// <summary>
        /// Delivers <paramref name="input"/> to the page's scripts first when it is meant for the page,
        /// and then to the window's own control for it, if the page left it that. Answers whether it was
        /// the page's -- and in <paramref name="handled"/> whether anything handled it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Keys and typed text, while the page has focus</b> -- the viewport's, or a control the
        /// window hosts over the page, its field editor first. The page hears a key's <c>keydown</c>
        /// before the editor edits or the viewport scrolls, and what the key does by default where the
        /// page does it (Tab, Enter and Space: <see cref="KeyboardInputResult.Handled"/>) is not done
        /// again; a key the page cancelled types nothing. What the editor then makes of a key or of
        /// typed text reaches the page as the user's edit of the field, and is undone if the page
        /// cancels that.
        /// </para>
        /// <para>
        /// <b>A press, a release or a move over a control the window hosts on the page</b> -- the field
        /// editor, a checkbox -- which the viewport never sees, since the control takes it: the release
        /// of a press on a text field went to the editor, so the page had no <c>click</c> for it. Not a
        /// list a control has open outside itself, whose items are not on the page.
        /// </para>
        /// </remarks>
        internal bool TryDispatchThroughPage(UiInputEvent input, UiSession session, out bool handled)
        {
            handled = false;
            if (_interactiveSession is not { } page)
                return false;

            switch (input.Kind)
            {
                case UiInputEventKind.KeyboardKey when HasPageFocus(session):
                    handled = DispatchKeyThroughPage(input, page, session);
                    return true;
                case UiInputEventKind.TextInput when HasPageFocus(session):
                    handled = DispatchTextThroughPage(input, page, session);
                    return true;
                case UiInputEventKind.TextComposition when HasPageFocus(session):
                    handled = DispatchCompositionThroughPage(input, page, session);
                    return true;
                case UiInputEventKind.PointerButton or UiInputEventKind.PointerMove when IsOverHostedControl(session, input.Position):
                    handled = DispatchHostedPointerThroughPage(input, session);
                    return true;
                default:
                    return false;
            }
        }

        private bool HasPageFocus(UiSession session) =>
            session.FocusedElement is { } focused && (ReferenceEquals(focused, this) || IsHostedHere(focused));

        private bool IsHostedHere(UiElement element)
        {
            for (UiElement? current = element.Parent; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, this))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether pointer input at <paramref name="position"/> goes to a control the window hosts over the
        /// page -- the one it hits, or the one holding the pointer -- with the point on the control itself.
        /// </summary>
        private bool IsOverHostedControl(UiSession session, BPoint position)
        {
            UiElement? target = session.CapturedElement ?? session.HitTest(position);
            if (target is null || ReferenceEquals(target, this) || !IsHostedHere(target))
                return false;

            UiElement control = target;
            while (control.Parent is { } parent && !ReferenceEquals(parent, this))
                control = parent;

            return session.CapturedElement is not null || control.Bounds.Contains(position);
        }

        private bool DispatchKeyThroughPage(UiInputEvent input, InteractiveSession page, UiSession session)
        {
            KeyboardInput key = PageKeys.From(input);
            if (key.Kind == KeyboardInputKind.Down)
                _keyTextSuppressed = false;

            string? before = EditorText(session);
            KeyboardInputResult result = DeliverKey(page, key);
            bool handled;
            if (key.Kind == KeyboardInputKind.Up)
            {
                handled = session.DispatchInput(input) || result.Delivered;
            }
            else if (result.DefaultPrevented)
            {
                _keyTextSuppressed = true;
                handled = true;
            }
            else if (result.Handled)
            {
                handled = true;
            }
            else
            {
                handled = session.DispatchInput(input);
                ReportEditorChange(page, session, before, EditInputType(key));
                ReportEditorSelection(page);
            }

            FollowPageFocus(page);
            ShowPageIfChanged();
            return handled;
        }

        private bool DispatchTextThroughPage(UiInputEvent input, InteractiveSession page, UiSession session)
        {
            string text = input.Text ?? string.Empty;

            // A key the page cancelled types nothing, in the page or in the editor.
            if (_keyTextSuppressed)
                return true;

            bool handled = true;
            string? before = EditorText(session);
            try
            {
                if (before is not null)
                {
                    handled = session.DispatchInput(input);
                    if (page.DispatchText(new TextInput(text) { EditedValue = _formEditor.Text }).DefaultPrevented)
                        _formEditor.SetText(before);
                }
                else
                {
                    page.DispatchText(new TextInput(text));
                }
            }
            catch (Exception ex)
            {
                RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.input", $"Delivering typed text to the page failed: {ex.Message}", ex);
            }

            ReportEditorSelection(page);
            ShowPageIfChanged();
            return handled;
        }

        /// <summary>
        /// An input method's composition in the field the editor holds: the editor shows and commits it,
        /// and the page hears it -- <c>compositionstart</c>, <c>compositionupdate</c>,
        /// <c>compositionend</c> and the <c>insertCompositionText</c> edits.
        /// </summary>
        private bool DispatchCompositionThroughPage(UiInputEvent input, InteractiveSession page, UiSession session)
        {
            bool handled = session.DispatchInput(input);
            string text = input.Text ?? string.Empty;
            try
            {
                switch (input.CompositionState ?? TextCompositionState.Updated)
                {
                    case TextCompositionState.Started:
                        page.DispatchComposition(new CompositionInput(CompositionInputKind.Start, string.Empty));
                        if (text.Length > 0)
                            page.DispatchComposition(new CompositionInput(CompositionInputKind.Update, text));
                        break;
                    case TextCompositionState.Committed:
                        page.DispatchComposition(new CompositionInput(CompositionInputKind.Commit, text) { EditedValue = EditorText(session) });
                        break;
                    case TextCompositionState.Cancelled:
                        page.DispatchComposition(new CompositionInput(CompositionInputKind.Cancel, string.Empty));
                        break;
                    default:
                        page.DispatchComposition(new CompositionInput(CompositionInputKind.Update, text));
                        break;
                }
            }
            catch (Exception ex)
            {
                RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.input", $"Delivering a composition to the page failed: {ex.Message}", ex);
            }

            ReportEditorSelection(page);
            ShowPageIfChanged();
            return handled;
        }

        /// <summary>
        /// Tells the page where the editor's selection is once the user moved it -- a drag, Shift and an
        /// arrow, a click that placed the caret -- so its <c>selectionStart</c> follows and it hears
        /// <c>select</c> and <c>selectionchange</c>.
        /// </summary>
        private void ReportEditorSelection(InteractiveSession page)
        {
            if (!_formEditor.IsActive)
                return;

            var selection = _formEditor.Selection;
            if (selection == _reportedSelection)
                return;

            _reportedSelection = selection;
            try
            {
                page.DispatchSelection(new FieldSelectionInput(selection.Start, selection.End) { Backward = selection.Backward });
            }
            catch (Exception ex)
            {
                RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.input", $"Delivering a selection to the page failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Keeps the editor on what the page's scripts did to the field it holds -- a mask that reformatted
        /// the value, a caret put back with <c>setSelectionRange</c>, a Tab that selected all of it.
        /// </summary>
        private void FollowPageField(InteractiveSession page)
        {
            long version = page.FieldVersion;
            if (version == _followedFieldVersion || !_formEditor.IsActive)
                return;

            _followedFieldVersion = version;
            if (page.FocusedTextField is not { InFrame: false } field)
                return;

            if (!string.Equals(_formEditor.Text, field.Value, StringComparison.Ordinal))
                _formEditor.SetText(field.Value);

            _formEditor.SetSelection(field.SelectionStart, field.SelectionEnd);
            _reportedSelection = _formEditor.Selection;
        }

        private static KeyboardInputResult DeliverKey(InteractiveSession page, KeyboardInput key)
        {
            try
            {
                return page.DispatchKey(key);
            }
            catch (Exception ex)
            {
                // A page whose scripts fail on a key must not cost the user the window's own handling of it.
                RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.input", $"Delivering a key to the page failed: {ex.Message}", ex);
                return default;
            }
        }

        /// <summary>The field editor's text, when it has the focus a key or typed text goes to.</summary>
        private string? EditorText(UiSession session) =>
            _formEditor.IsActive && ReferenceEquals(session.FocusedElement, _formEditor.Editor) ? _formEditor.Text : null;

        /// <summary>
        /// Tells the page what a key did to the field the editor holds -- a deletion, a paste -- and
        /// undoes it when the page cancels it.
        /// </summary>
        private void ReportEditorChange(InteractiveSession page, UiSession session, string? before, string inputType)
        {
            if (before is null || !_formEditor.IsActive || string.Equals(_formEditor.Text, before, StringComparison.Ordinal))
                return;

            try
            {
                if (page.DispatchEdit(new FieldEdit(inputType, _formEditor.Text)).DefaultPrevented)
                    _formEditor.SetText(before);
            }
            catch (Exception ex)
            {
                RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.input", $"Delivering an edit to the page failed: {ex.Message}", ex);
            }
        }

        /// <summary>What a key's change to a field is, as <c>InputEvent.inputType</c> names it.</summary>
        private static string EditInputType(KeyboardInput key) => key.Key switch
        {
            "Backspace" => "deleteContentBackward",
            "Delete" => "deleteContentForward",
            "v" or "V" when key.CtrlKey => "insertFromPaste",
            "x" or "X" when key.CtrlKey => "deleteByCut",
            "z" or "Z" when key.CtrlKey => "historyUndo",
            "y" or "Y" when key.CtrlKey => "historyRedo",
            _ => "insertReplacementText",
        };

        /// <summary>
        /// A press, a release or a move over a control the window hosts on the page: the page's scripts
        /// hear it, and the control gets it unless the page cancelled the press.
        /// </summary>
        private bool DispatchHostedPointerThroughPage(UiInputEvent input, UiSession session)
        {
            PointF point = ToLocalPoint(input.Position);
            bool handled;
            if (input.Kind == UiInputEventKind.PointerMove)
            {
                if (_lastMovePoint != point)
                {
                    _lastMovePoint = point;
                    DeliverPointer(input, point, PointerInputKind.Move);
                }

                handled = session.DispatchInput(input);
            }
            else if (input.MouseButtonTransition == MouseButtonTransition.Down)
            {
                _heldButtons |= ButtonMask(input.MouseButton);

                // A press the page cancels neither moves the editor's caret nor changes the control.
                handled = DeliverPointer(input, point, PointerInputKind.Down) || session.DispatchInput(input);
                if (_interactiveSession is { } page)
                    FollowPageFocus(page);
            }
            else
            {
                _heldButtons &= ~ButtonMask(input.MouseButton);
                DeliverPointer(input, point, PointerInputKind.Up);
                handled = session.DispatchInput(input);
            }

            // A press placed the editor's caret, a drag moved its selection.
            if (_interactiveSession is { } selecting)
                ReportEditorSelection(selecting);

            ShowPageIfChanged();
            return handled;
        }

        /// <summary>
        /// Keeps the field editor on the text field the page has focused: it opens on a field of the
        /// page that Tab, a script or a press focused, and closes once the page's focus has left it. A
        /// field in a frame has no editor; what is typed there the page puts in the field itself.
        /// </summary>
        private void FollowPageFocus(InteractiveSession page)
        {
            long version = page.FocusVersion;
            if (version == _followedFocusVersion)
                return;

            _followedFocusVersion = version;
            if (page.FocusedTextField is not { InFrame: false } field)
            {
                if (_formEditor.IsActive)
                {
                    _formEditor.Commit();
                    Session?.SetFocus(this);
                }

                return;
            }

            PointF centre = new((float)(field.X + (field.Width / 2)), (float)(field.Y + (field.Height / 2)));
            if (_formEditor.Covers(centre))
                return;

            ShowPageIfChanged();
            EnsureLayout();
            if (!BeginFormEdit(new PointF(centre.X, centre.Y - (_scrollY / _viewportZoom))) && _formEditor.IsActive)
            {
                _formEditor.Commit();
                Session?.SetFocus(this);
                return;
            }

            // The editor opens on the selection the page has for the field: all of it after a Tab.
            _followedFieldVersion = -1;
            FollowPageField(page);
        }

        /// <summary>Shows what the page's scripts changed since the window last showed the page, unless a press is held.</summary>
        private void ShowPageIfChanged()
        {
            if (_interactiveSession is { } following)
                FollowPageField(following);

            if (_interactiveSession is { } page && _heldButtons == 0 && page.RenderVersion != _appliedRenderVersion)
                ApplyPageDocument(page.CurrentHtml());

            InvalidateRenderedContent();
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

        /// <remarks>
        /// The page's scripts hear a move first: the boundary events of what the pointer left and
        /// reached, then <c>pointermove</c> and <c>mousemove</c>. What the page changed since the window
        /// last showed it -- in those handlers, or in a press's that began editing a field -- is shown
        /// straight away, unless a button is held, since parsing the page again during a press loses
        /// the press the container is holding (see <see cref="HandlePointerButton"/>). The page is asked
        /// whether anything changed rather than serialized after every move.
        /// </remarks>
        private bool HandlePointerMove(UiInputEvent input)
        {
            PointF point = ToLocalPoint(input.Position);
            if (_interactiveSession is { } session && _lastMovePoint != point)
            {
                _lastMovePoint = point;
                DeliverPointer(input, point, PointerInputKind.Move);
                if (_heldButtons == 0 && session.RenderVersion != _appliedRenderVersion)
                    ApplyPageDocument(session.CurrentHtml());
            }

            _container.HandleMouseMove(point, false, false);
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

        /// <summary>
        /// The user followed a link into the page, or went back or forward between two places in it: a
        /// page whose scripts run hears it as its own fragment navigation -- <c>location.hash</c>,
        /// <c>hashchange</c>, <c>:target</c> -- and one without has the renderer style the new target.
        /// </summary>
        public void ShowTarget(string url, string fragment)
        {
            if (_interactiveSession is { } page)
            {
                try
                {
                    page.NavigateToFragment(url);
                }
                catch (Exception ex)
                {
                    RenderLogger.LogWarning(LogCategory.JavaScript, "Browser.fragment", $"Delivering a fragment navigation to the page failed: {ex.Message}", ex);
                }

                ShowPageIfChanged();
                return;
            }

            string? target = fragment.Length == 0 ? null : fragment;
            if (string.Equals(_container.TargetFragment, target, StringComparison.Ordinal))
                return;

            _container.TargetFragment = target;
            _suppressNavigation = true;
            try
            {
                _container.RestyleDocument();
            }
            finally
            {
                _suppressNavigation = false;
            }

            MarkLayoutDirty();
        }

        /// <summary>
        /// Scrolls to what <paramref name="fragment"/> indicates, as soon as the page has a layout to
        /// find it in: HTML §7.4.6.4, "scroll to the fragment".
        /// </summary>
        /// <remarks>
        /// Nothing scrolled to a fragment. A link to <c>#top</c> reloaded the page at the top, and a
        /// page loaded with a fragment opened at the top too: Acid2's "Take The Acid2 Test" link, which
        /// is <c>href="#top"</c>, left the test's face far below the window.
        /// </remarks>
        public void ScrollToFragment(string fragment)
        {
            _pendingFragment = fragment;
            _renderDirty = true;
            Invalidate(UiInvalidationKind.Render);
        }

        /// <summary>
        /// Scrolls to the element whose id is the decoded fragment, or to the top for an empty
        /// fragment or <c>top</c> that names no element. An element that is not there yet is looked
        /// for again at the next layout, until the user scrolls.
        /// </summary>
        private void TryScrollToFragment(string fragment)
        {
            string decoded = Uri.UnescapeDataString(fragment);
            if (decoded.Length > 0 && _container.GetElementRectangle(decoded) is { } target)
            {
                _scrollY = target.Y * _viewportZoom;
            }
            else if (decoded.Length == 0 || decoded.Equals("top", StringComparison.OrdinalIgnoreCase))
            {
                _scrollY = 0;
            }
            else
            {
                return;
            }

            _pendingFragment = null;
            _renderDirty = true;
        }

        /// <summary>
        /// Follows the page when it scrolled its viewport itself -- <c>scrollTo</c>, <c>scrollIntoView</c>, a
        /// script's fragment navigation -- since this view last saw it: the page did not scroll at all before.
        /// </summary>
        private void FollowPageScroll()
        {
            if (_interactiveSession is not { } page)
                return;

            double pageY = page.ViewportScroll.Y;
            if (Math.Abs(pageY - _pageScrollSeen) <= 0.5)
                return;

            _pageScrollSeen = pageY;
            _viewScrollReported = pageY;
            _pendingFragment = null;
            _scrollY = (float)(pageY * _viewportZoom);
            _renderDirty = true;
        }

        /// <summary>
        /// Tells the page where this view is scrolled when it moved since the page was last told -- the user's
        /// scroll, a fragment the window scrolled to, a clamp -- so its <c>scrollY</c> and its geometry answer
        /// for what is on screen and it hears its <c>scroll</c>. The page knew nothing of it before.
        /// </summary>
        private void ReportViewScroll()
        {
            if (_interactiveSession is not { } page)
                return;

            double viewY = _scrollY / _viewportZoom;
            if (Math.Abs(viewY - _viewScrollReported) <= 0.5)
                return;

            _viewScrollReported = viewY;
            _viewScrollReportedSinceTaken = true;
            page.ScrollViewportTo(0, viewY);
            _pageScrollSeen = page.ViewportScroll.Y;
        }

        /// <summary>Whether a frame told the page where this view is scrolled since this was last asked.</summary>
        public bool TakeReportedScroll()
        {
            bool reported = _viewScrollReportedSinceTaken;
            _viewScrollReportedSinceTaken = false;
            return reported;
        }

        /// <summary>Where the view is scrolled, in CSS pixels.</summary>
        public float ScrollY => _scrollY / _viewportZoom;

        /// <summary>Scrolls the view to <paramref name="y"/> CSS pixels, where a history entry was left; the page hears it.</summary>
        public void RestoreScroll(float y) => SetScroll(y * _viewportZoom);

        private void SetScroll(float value)
        {
            // The user's scroll wins over a fragment still waiting for its element.
            _pendingFragment = null;
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
            PageRequest? submission = _formState.TryBuildSubmitRequest(GetPageHtml(), e.Attributes, e.Link);

            // Only a link and a form's submit button go anywhere. A button that submits no form -- one in no
            // form, a reset or a plain button -- came with the link the renderer resolves from no href, which
            // is the page's own URL, and following it reloaded the page under every click on a page's
            // script-driven buttons (measured: a popover's invoker reloaded the page it opened the popover in).
            if (submission is null && !e.Attributes.ContainsKey("href"))
                return;

            PageRequest request = (submission ?? PageRequest.ForUrl(e.Link)) with
            {
                Initiator = DocumentContext,
                NavigationType = submission is null ? PageNavigationType.Link : PageNavigationType.FormSubmission,
            };
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
            {
                request = request with { Initiator = DocumentContext, NavigationType = PageNavigationType.FormSubmission };
                LinkActivated?.Invoke(this, new BrowserLinkEventArgs(request, new Dictionary<string, string>()));
            }
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
    }

    /// <summary>
    /// Lays out the resubmission prompt: the message across the top, its two buttons
    /// on a row at the bottom right. A dialog's default child arrangement stretches
    /// every child over the whole surface, so this places them itself.
    /// </summary>
    private sealed class ConfirmPrompt : UiElement
    {
        private const double Margin = 16;
        private const double ButtonHeight = 28;
        private const double ButtonWidth = 84;
        private const double ButtonGap = 8;

        private readonly StandardLabel _message;
        private readonly StandardButton _accept;
        private readonly StandardButton _cancel;

        public ConfirmPrompt(StandardLabel message, StandardButton accept, StandardButton cancel)
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
