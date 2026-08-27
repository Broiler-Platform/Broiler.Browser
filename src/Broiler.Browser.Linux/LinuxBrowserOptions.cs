namespace Broiler.Browser;

internal sealed record LinuxBrowserOptions(
    bool ShowHelp,
    bool OpenWindow,
    bool RunUntilClose,
    int Width,
    int Height,
    int DurationMilliseconds,
    string? InitialUrl,
    string? ArtifactDirectory)
{
    public static LinuxBrowserOptions Parse(string[] args)
    {
        bool openWindow = OperatingSystem.IsLinux();
        bool runUntilClose = OperatingSystem.IsLinux();
        int width = 1100;
        int height = 800;
        int durationMilliseconds = 5000;
        string? initialUrl = null;
        string? artifactDirectory = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h" or "/?")
            {
                return new LinuxBrowserOptions(
                    ShowHelp: true,
                    OpenWindow: openWindow,
                    RunUntilClose: runUntilClose,
                    Width: width,
                    Height: height,
                    DurationMilliseconds: durationMilliseconds,
                    InitialUrl: initialUrl,
                    ArtifactDirectory: artifactDirectory);
            }

            if (arg.Equals("--window", StringComparison.OrdinalIgnoreCase))
            {
                openWindow = true;
                continue;
            }

            if (arg.Equals("--offscreen", StringComparison.OrdinalIgnoreCase))
            {
                openWindow = false;
                runUntilClose = false;
                continue;
            }

            if (arg.Equals("--run-until-close", StringComparison.OrdinalIgnoreCase))
            {
                runUntilClose = true;
                continue;
            }

            if (TryReadInt(args, ref i, "--width", arg, out int parsedWidth))
            {
                width = parsedWidth;
                continue;
            }

            if (TryReadInt(args, ref i, "--height", arg, out int parsedHeight))
            {
                height = parsedHeight;
                continue;
            }

            if (TryReadInt(args, ref i, "--duration-ms", arg, out int parsedDuration))
            {
                durationMilliseconds = parsedDuration;
                continue;
            }

            if (TryReadString(args, ref i, "--artifact-dir", arg, out string? parsedArtifactDirectory))
            {
                artifactDirectory = parsedArtifactDirectory;
                continue;
            }

            if (TryReadString(args, ref i, "--url", arg, out string? parsedUrl))
            {
                initialUrl = parsedUrl;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Unknown option: " + arg);

            initialUrl ??= arg;
        }

        width = Math.Max(64, width);
        height = Math.Max(64, height);
        durationMilliseconds = Math.Max(1, durationMilliseconds);

        return new LinuxBrowserOptions(
            ShowHelp: false,
            OpenWindow: openWindow,
            RunUntilClose: runUntilClose,
            Width: width,
            Height: height,
            DurationMilliseconds: durationMilliseconds,
            InitialUrl: initialUrl,
            ArtifactDirectory: artifactDirectory);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Broiler Browser Linux");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --window                 Open an X11/OpenGL window on Linux (default on Linux).");
        Console.WriteLine("  --offscreen              Render once using an offscreen OpenGL surface.");
        Console.WriteLine("  --url <url>              Initial URL or local HTML file to load.");
        Console.WriteLine("  --width <pixels>         Surface width. Default: 1100.");
        Console.WriteLine("  --height <pixels>        Surface height. Default: 800.");
        Console.WriteLine("  --duration-ms <ms>       Windowed run duration unless --run-until-close is used. Default: 5000.");
        Console.WriteLine("  --run-until-close        Keep the window open until it is closed.");
        Console.WriteLine("  --artifact-dir <path>    Save backend and CPU-rendered PNG snapshots.");
        Console.WriteLine("  --help                   Show this help.");
    }

    private static bool TryReadString(string[] args, ref int index, string option, string arg, out string? value)
    {
        if (arg.Equals(option, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException("Missing value for " + option + ".");

            value = args[++index];
            return true;
        }

        string prefix = option + "=";
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..];
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryReadInt(string[] args, ref int index, string option, string arg, out int value)
    {
        if (TryReadString(args, ref index, option, arg, out string? raw))
        {
            if (!int.TryParse(raw, out value))
                throw new ArgumentException("Invalid integer for " + option + ": " + raw);

            return true;
        }

        value = 0;
        return false;
    }
}
