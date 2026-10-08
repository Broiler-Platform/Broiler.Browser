using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Broiler.Traffic;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.Help) { Console.WriteLine(Options.Usage); return 0; }
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Live process attribution requires Windows.");
            string tshark = FindTshark(options.Tshark);
            if (options.List)
            {
                var listInfo = StartInfo(tshark, ["-D"]);
                listInfo.RedirectStandardOutput = listInfo.RedirectStandardError = true;
                listInfo.StandardOutputEncoding = listInfo.StandardErrorEncoding = Encoding.UTF8;
                using var listing = Process.Start(listInfo)!;
                var stdout = listing.StandardOutput.ReadToEndAsync();
                var stderr = listing.StandardError.ReadToEndAsync();
                await listing.WaitForExitAsync();
                Console.Write(await stdout);
                Console.Error.Write(await stderr);
                return listing.ExitCode;
            }
            return await CaptureAsync(tshark, options);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or Win32Exception or
                                      UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static string FindTshark(string? requested)
    {
        if (requested is not null)
            return File.Exists(requested) ? Path.GetFullPath(requested) : throw new FileNotFoundException("--tshark does not exist.");
        var directories = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Wireshark") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        return directories.Select(d => Path.Combine(d, "tshark.exe")).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("TShark not found. Install Wireshark with TShark/Npcap or supply --tshark <path>.");
    }

    private static ProcessStartInfo StartInfo(string executable, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    internal static List<string> CaptureArguments(Options o)
    {
        List<string> args = ["-i", o.Interface!, "-p", "-n", "-l", "-f", o.Filter, "-a",
            "duration:" + o.Seconds.ToString(CultureInfo.InvariantCulture), "-T", "fields",
            "-E", "separator=/t", "-E", "quote=n", "-E", "occurrence=f", "-E", "escape=y"];
        foreach (var field in Packet.Fields) { args.Add("-e"); args.Add(field); }
        return args;
    }

    private static async Task<int> CaptureAsync(string tshark, Options options)
    {
        // CreateNew deliberately protects an earlier diagnostic run from replacement.
        using var output = options.Output is null ? null : new StreamWriter(
            new FileStream(options.Output, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        using var stop = new CancellationTokenSource();
        using var samplingStop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        var sampler = new SocketSampler();
        var classifier = new TrafficClassifier();
        var info = StartInfo(tshark, CaptureArguments(options));
        info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
        using var capture = new Process { StartInfo = info };
        Task? sampleTask = null, errorTask = null;
        long seen = 0, shown = 0, unknown = 0, skipped = 0;
        bool started = false;
        try
        {
            sampler.Refresh();
            capture.Start();
            started = true;
            sampleTask = Task.Run(async () =>
            {
                try { await sampler.RunAsync(samplingStop.Token); }
                catch (OperationCanceledException) when (samplingStop.IsCancellationRequested) { }
                catch { stop.Cancel(); throw; }
            });
            errorTask = Task.Run(async () =>
            {
                while (await capture.StandardError.ReadLineAsync() is { } line) Console.Error.WriteLine("tshark: " + Safe(line));
            });
            // TShark owns the normal duration stop; this also bounds a hung startup/child.
            stop.CancelAfter(TimeSpan.FromSeconds(options.Seconds + 15));
            Console.Error.WriteLine($"Capturing interface {Safe(options.Interface!)} for {options.Seconds}s; Ctrl+C stops. Ownership is sampled every 100ms.");
            if (!options.Json) Console.WriteLine("TIME (UTC)     PROTOCOL  PROCESS(PID):SIDE                 SOURCE -> DESTINATION | HOST | USE CASE");
            try
            {
                while (await capture.StandardOutput.ReadLineAsync(stop.Token) is { } line)
                {
                    var packet = Packet.Parse(line);
                    if (packet is null) { skipped++; continue; }
                    seen++;
                    var attribution = sampler.Match(packet);
                    var hint = classifier.Classify(packet);
                    if (!options.Accept(attribution.Owners, hint.Host)) continue;
                    shown++;
                    if (attribution.Owners.Length == 0) unknown++;
                    var record = new
                    {
                        packet.Time, packet.Bytes, packet.Transport, packet.Protocol, packet.Source, packet.SourcePort,
                        packet.Destination, packet.DestinationPort, packet.Stream, hint.Host,
                        HostEvidence = hint.Evidence, hint.UseCase, UserLabel = options.Label, packet.HttpStatus,
                        attribution.SnapshotTime, attribution.Owners
                    };
                    string json = JsonSerializer.Serialize(record, JsonOptions);
                    if (options.Json) Console.WriteLine(json);
                    else
                    {
                        string owners = attribution.Owners.Length == 0 ? "unknown" : string.Join(",",
                            attribution.Owners.Select(o => $"{o.Name ?? "?"}({o.Pid}):{o.PacketSide}"));
                        Console.WriteLine($"{packet.Time:HH:mm:ss.fff}  {Safe(packet.Protocol),-9} {Safe(owners),-33} " +
                            $"{Endpoint(packet.Source, packet.SourcePort)} -> {Endpoint(packet.Destination, packet.DestinationPort)} | " +
                            $"{Safe(hint.Host ?? "?")} | {hint.UseCase}" +
                            (packet.HttpStatus is null ? "" : $" | HTTP {packet.HttpStatus}") +
                            (options.Label is null ? "" : " | label=" + Safe(options.Label)));
                    }
                    if (output is not null) await output.WriteLineAsync(json);
                }
                await capture.WaitForExitAsync(stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (started && !capture.HasExited)
            {
                try { capture.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await capture.WaitForExitAsync();
            }
            samplingStop.Cancel();
            if (errorTask is not null) await errorTask;
            if (sampleTask is not null) await sampleTask;
        }
        Console.Error.WriteLine($"Finished: {seen} decoded, {shown} matched, {unknown} matched without owner, {skipped} unsupported/non-TCP/UDP frames.");
        return stop.IsCancellationRequested ? 130 : capture.ExitCode;
    }

    private static string Endpoint(string address, int port) => address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";
    private static string Safe(string text) => new(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
