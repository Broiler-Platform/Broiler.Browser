using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Broiler.Graphics;
using Broiler.Graphics.Linux;
using Broiler.Graphics.Linux.OpenGL;
using Broiler.App;

namespace Broiler.Browser;

internal static class LinuxBrowserRunner
{
    private static readonly TimeSpan InteractivePollInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan OffscreenPollInterval = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan AnimationInterval = TimeSpan.FromMilliseconds(16);
    private static readonly BRenderOptions RenderOptions = new(Antialias: true, VSync: true, SubpixelText: true);

    public static async Task<int> RunAsync(LinuxBrowserOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        Console.WriteLine("Broiler Browser");
        Console.WriteLine();
        Console.WriteLine("Preview safety notice: use controlled content only. JavaScript is not a sandbox.");
        Console.WriteLine();
        PrintRuntime(LinuxGraphicsRuntimeDiagnostics.Capture());
        Print("Windowing baseline", LinuxGraphicsDependencies.CheckWindowingBaseline());
        Print("OpenGL", LinuxOpenGlRenderer.CheckDependencies());

        long frameIndex = 0;
        long renderTicks = 0;
        int renderedFrames = 0;
        BFrameContext lastFrameContext = BFrameContext.Default;
        var postedActions = new ConcurrentQueue<Action>();

        using LinuxOpenGlRenderer renderer = new();
        BSurfaceDescriptor descriptor = BSurfaceDescriptor.Default(new BSize(options.Width, options.Height));
        using IBroilerSurface surface = options.OpenWindow
            ? renderer.CreateX11WindowSurface(descriptor, "Broiler Browser")
            : renderer.CreateSurface(descriptor);

        LinuxOpenGlX11WindowSurface? x11Window = surface as LinuxOpenGlX11WindowSurface;
        bool canUseEvdev = x11Window is not null;

        // The X11 clipboard, on its own connection. Absent when running
        // offscreen with no display, and then copy and paste report themselves
        // unavailable rather than silently working only inside this process.
        using LinuxX11Clipboard? clipboard = x11Window is not null ? LinuxX11Clipboard.TryOpen() : null;
        if (x11Window is not null && clipboard is null)
            Console.WriteLine("No X11 clipboard is available; copy and paste are disabled.");

        using BrowserUiHost host = new(
            () => surface.Size,
            () => surface.DpiScale,
            static () => { },
            renderList =>
            {
                lastFrameContext = new BFrameContext(BrowserPalette.Canvas, frameIndex++, RenderOptions);
                renderer.Render(surface, renderList, lastFrameContext);
            },
            action =>
            {
                postedActions.Enqueue(action);
                return true;
            },
            () => clipboard is not null && clipboard.TryGetText(out string text) ? text : null,
            text => clipboard?.SetText(text));
        using BrowserApp app = new(host, () => renderer, options.InitialUrl, static _ => { });

        await using LinuxInputCoordinator input = new(
            canUseEvdev,
            Console.WriteLine,
            externalPointer: x11Window is not null,
            applicationName: "Broiler Browser");
        await input.InitializeAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset start = DateTimeOffset.UtcNow;
        DateTimeOffset nextAnimationUpdate = DateTimeOffset.MinValue;
        BSize lastSurfaceSize = surface.Size;
        using PeriodicTimer timer = new(options.OpenWindow ? InteractivePollInterval : OffscreenPollInterval);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool processedWindowEvents = false;

            // An X11 copy is a promise to answer requests: between these calls,
            // another application pasting from Broiler sees an empty clipboard.
            clipboard?.ProcessPendingEvents();
            if (x11Window is not null)
            {
                processedWindowEvents = x11Window.ProcessPendingEvents();
                await input.SetActiveAsync(x11Window.IsFocused, cancellationToken).ConfigureAwait(false);
                if (!surface.Size.Equals(lastSurfaceSize))
                {
                    app.Invalidate();
                    lastSurfaceSize = surface.Size;
                }
            }

            input.SetViewport(host.ViewportSize);
            if (x11Window is not null && x11Window.TryGetPointerPosition(out int pointerX, out int pointerY, out _))
            {
                double scale = host.Scale <= 0 ? 1.0 : host.Scale;
                input.SetExternalPointer(pointerX / scale, pointerY / scale);
            }

            int posted = DrainPostedActions(postedActions);
            int inputEvents = input.Drain(app.Dispatch);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (app.HasPendingWork && now >= nextAnimationUpdate)
            {
                app.StepAnimation();
                nextAnimationUpdate = now + AnimationInterval;
            }

            if (host.IsInvalidated || processedWindowEvents || posted > 0 || inputEvents > 0)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                app.RenderFrame();
                stopwatch.Stop();
                renderTicks += stopwatch.ElapsedTicks;
                renderedFrames++;
            }

            if (!options.OpenWindow && !ShouldContinueOffscreen(app, host, postedActions, start, options.DurationMilliseconds))
                break;

            if (options.OpenWindow &&
                !options.RunUntilClose &&
                (DateTimeOffset.UtcNow - start).TotalMilliseconds >= options.DurationMilliseconds)
            {
                break;
            }
        }
        while (!input.QuitRequested &&
               (x11Window is null || !x11Window.IsCloseRequested) &&
               await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));

        await input.SetActiveAsync(false, cancellationToken).ConfigureAwait(false);
        using BBitmap bitmap = ReadSurface(surface);
        Console.WriteLine("Browser presentation:");
        Console.WriteLine("  presentation: " + PresentationState(surface));
        Console.WriteLine("  diagnostic: " + SurfaceDiagnostic(surface));
        Console.WriteLine("  page: " + app.Status);
        Console.WriteLine("  bitmap: " + bitmap.Width.ToString(CultureInfo.InvariantCulture) + "x" + bitmap.Height.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  render-time: " + FormatMilliseconds(renderTicks) + " ms total across " + renderedFrames.ToString(CultureInfo.InvariantCulture) + " frame(s)");
        Console.WriteLine("  input: " + InputSummary(input.Snapshot));
        SaveArtifacts(options, surface, bitmap, host.LastRenderList, lastFrameContext);
        Console.WriteLine();
        return 0;
    }

    private static int DrainPostedActions(ConcurrentQueue<Action> actions)
    {
        int count = 0;
        while (actions.TryDequeue(out Action? action))
        {
            action();
            count++;
        }

        return count;
    }

    private static bool ShouldContinueOffscreen(
        BrowserApp app,
        BrowserUiHost host,
        ConcurrentQueue<Action> postedActions,
        DateTimeOffset start,
        int durationMilliseconds)
    {
        if ((DateTimeOffset.UtcNow - start).TotalMilliseconds >= durationMilliseconds)
            return false;

        return app.IsBusy || app.HasPendingWork || host.IsInvalidated || !postedActions.IsEmpty;
    }

    private static BBitmap ReadSurface(IBroilerSurface surface) =>
        surface switch
        {
            LinuxOpenGlSurface openGlSurface => openGlSurface.ReadToBitmap(),
            LinuxOpenGlX11WindowSurface x11Surface => x11Surface.ReadToBitmap(),
            _ => throw new InvalidOperationException("Unexpected Linux browser surface."),
        };

    private static string PresentationState(IBroilerSurface surface) =>
        surface switch
        {
            LinuxOpenGlSurface openGlSurface => openGlSurface.IsGpuBacked ? "EGL/OpenGL pbuffer" : "CPU fallback",
            LinuxOpenGlX11WindowSurface x11Surface => x11Surface.IsGpuBacked ? "X11/EGL/OpenGL window" : "CPU fallback",
            _ => "unknown",
        };

    private static string SurfaceDiagnostic(IBroilerSurface surface) =>
        surface switch
        {
            LinuxOpenGlSurface openGlSurface => openGlSurface.Diagnostic,
            LinuxOpenGlX11WindowSurface x11Surface => x11Surface.Diagnostic,
            _ => string.Empty,
        };

    private static void SaveArtifacts(
        LinuxBrowserOptions options,
        IBroilerSurface surface,
        BBitmap backendBitmap,
        BRenderList? renderList,
        BFrameContext frameContext)
    {
        if (options.ArtifactDirectory is null)
            return;

        Directory.CreateDirectory(options.ArtifactDirectory);
        string backendPath = Path.Combine(options.ArtifactDirectory, "broiler-browser-linux-opengl.png");
        backendBitmap.Save(backendPath);

        if (renderList is not null)
        {
            using BImageRenderer cpu = new();
            using BBitmap cpuBitmap = cpu.RenderToImage(renderList, new BSurfaceDescriptor(surface.Size, surface.DpiScale), frameContext);
            string cpuPath = Path.Combine(options.ArtifactDirectory, "broiler-browser-linux-cpu.png");
            cpuBitmap.Save(cpuPath);
            Console.WriteLine("  artifacts: " + cpuPath + "; " + backendPath);
            return;
        }

        Console.WriteLine("  artifact: " + backendPath);
    }

    private static string InputSummary(LinuxWriterInputSnapshot input)
    {
        if (!input.Enabled)
            return "disabled";
        if (!input.Initialized)
            return "requested but not opened";

        string keyboard = input.KeyboardDevice ?? "none";
        string mouse = input.MouseDevice ?? "none";
        return "active=" + input.Active.ToString(CultureInfo.InvariantCulture) +
            ", keyboard=" + keyboard +
            ", mouse=" + mouse +
            ", keys=" + input.KeyEvents.ToString(CultureInfo.InvariantCulture) +
            ", text=" + input.TextEvents.ToString(CultureInfo.InvariantCulture) +
            ", moves=" + input.MouseMoveEvents.ToString(CultureInfo.InvariantCulture) +
            ", buttons=" + input.MouseButtonEvents.ToString(CultureInfo.InvariantCulture) +
            ", wheels=" + input.MouseWheelEvents.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatMilliseconds(long ticks) =>
        (ticks * 1000.0 / Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture);

    private static void PrintRuntime(LinuxGraphicsRuntimeReport report)
    {
        Console.WriteLine("Runtime:");
        Console.WriteLine("  os: " + report.OperatingSystemDescription);
        Console.WriteLine("  framework: " + report.FrameworkDescription);
        Console.WriteLine("  arch: " + report.ProcessArchitecture);
        Console.WriteLine("  rid: " + report.RuntimeIdentifier);
        Console.WriteLine("  display: " + report.DisplayServer.Diagnostic);
        Console.WriteLine();
    }

    private static void Print(string title, System.Collections.Generic.IReadOnlyList<LinuxNativeLibraryStatus> statuses)
    {
        Console.WriteLine(title + ":");
        foreach (LinuxNativeLibraryStatus status in statuses)
        {
            string state = status.IsAvailable ? "available" : "missing";
            Console.WriteLine("  " + status.Id + ": " + state + " - " + status.Diagnostic);
        }

        Console.WriteLine();
    }
}
