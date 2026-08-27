namespace Broiler.Browser;

/// <summary>
/// Minimal file logger. Exceptions thrown out of a Win32 window-procedure callback cross the
/// native boundary and trigger a runtime fail-fast that <c>try</c>/<c>catch</c> in <c>Main</c>
/// cannot observe, so callback-invoked code routes failures here instead of letting them escape.
/// </summary>
internal static class Diagnostics
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "broiler-graphics.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{System.DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never itself throw across the callback boundary.
        }
    }

    public static void Log(string context, Exception ex)
    {
        string detail = ex.ToString();
        if (ex is System.IO.FileNotFoundException fnf && !string.IsNullOrEmpty(fnf.FusionLog))
            detail += $"{Environment.NewLine}FusionLog:{Environment.NewLine}{fnf.FusionLog}";
        Log($"[{context}] {detail}");
    }

    /// <summary>Runs <paramref name="action"/>, logging and swallowing any exception.</summary>
    public static void Guard(string context, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log(context, ex);
        }
    }
}
