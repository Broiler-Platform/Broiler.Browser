using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Runtime.Versioning;

namespace Broiler.Browser;

/// <summary>
/// Entry point for the Broiler browser built on Broiler.Graphics (Win32 + Direct2D).
/// This is the preview shell that replaces the WPF host (<c>Broiler.App</c>).
/// </summary>
[SupportedOSPlatform("windows7.0")]
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // The HTML stack pulls in two physically distinct builds of the same-identity
        // assemblies (e.g. Broiler.Dom is checked out under both Broiler.DOM and the
        // CSS submodule). MSBuild dedups them and drops their runtime entry from deps.json,
        // so the host never probes for them even though the DLLs are in the output folder.
        // Fall back to loading any such assembly directly from the application directory.
        AssemblyLoadContext.Default.Resolving += ResolveFromAppDirectory;

        // Composition root: register the concrete image codecs Broiler.Graphics decodes/encodes with.
        Broiler.Graphics.BImageCodecs.Use(
            new Broiler.Media.MediaCodecCatalog(Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));

        if (!ConfirmPreviewSafetyNotice())
            return 0;

        // app.manifest declares PerMonitorV2, and the loader applies that before Main runs — so this
        // call is expected to fail with ERROR_ACCESS_DENIED on the normal apphost launch, and the
        // failure is the *success* case. It stays because it is not redundant everywhere: launching
        // through `dotnet Broiler.Browser.Windows.dll` uses the host's manifest rather than ours, and
        // there this is the only thing that makes the process DPI-aware. Either way awareness must be
        // settled before the first window exists; Direct2DWindow reads the live DPI from creation on.
        _ = SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2.

        string? initialUrl = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : null;

        try
        {
            using var window = new BrowserWindow(initialUrl);
            return window.Run();
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, ex.ToString(), "Broiler", MbIconError | MbOk);
            return 1;
        }
    }

    private static Assembly? ResolveFromAppDirectory(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name))
            return null;

        string candidate = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }

    private static bool ConfirmPreviewSafetyNotice()
    {
        const string message =
            "Broiler is an early preview; some human review records are still pending.\n\n" +
            "Risks: HTML/CSS/JS, images, downloads, network/file access, and Windows interop are not security-hardened. JavaScript is not a sandbox.\n\n" +
            "Recommendation: use controlled content only. Test unknown content in a VM or sandbox, restrict host, file, and network access, and use resource limits.\n\n" +
            "Choose OK to continue or Cancel to exit.";

        return MessageBox(IntPtr.Zero, message, "Broiler Preview - Safety Notice", MbIconWarning | MbOkCancel) == IdOk;
    }

    private const uint MbOk = 0x00000000;
    private const uint MbOkCancel = 0x00000001;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconWarning = 0x00000030;
    private const int IdOk = 1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
}
