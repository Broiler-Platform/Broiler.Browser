using Broiler.Graphics.Imaging;
using System.Reflection;
using System.Runtime.Loader;

namespace Broiler.Browser;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += ResolveFromAppDirectory;

        // Composition root: register the concrete image codecs Broiler.Graphics decodes/encodes with,
        // as the Windows head does. Without them BBitmap.Save, which --artifact-dir writes with, throws.
        BImageCodecs.Use(
            new Broiler.Media.MediaCodecCatalog(Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));

        LinuxBrowserOptions options = LinuxBrowserOptions.Parse(args);
        if (options.ShowHelp)
        {
            LinuxBrowserOptions.PrintHelp();
            return 0;
        }

        if (options.OpenWindow && !OperatingSystem.IsLinux())
        {
            Console.WriteLine("Windowed Broiler Browser uses the Linux X11/OpenGL backend; running offscreen on this OS.");
            options = options with { OpenWindow = false, RunUntilClose = false };
        }

        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        return await LinuxBrowserRunner.RunAsync(options, shutdown.Token).ConfigureAwait(false);
    }

    private static Assembly? ResolveFromAppDirectory(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name))
            return null;

        string candidate = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }
}
