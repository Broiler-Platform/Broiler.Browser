namespace Broiler.Traffic;

internal sealed class Options
{
    public bool Help, List, IncludeUnattributed, Json;
    public string? Interface, Output, Host, Label, Tshark;
    public string Filter = "tcp or udp";
    public int Seconds = 60;
    public List<int> Pids { get; } = [];
    public List<string> Processes { get; } = [];

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)
                ? args[i] : throw new ArgumentException($"Missing value for {args[i - 1]}.");
            switch (args[i])
            {
                case "--help" or "-h": o.Help = true; break;
                case "--list": o.List = true; break;
                case "--interface" or "-i": o.Interface = Value(); break;
                case "--filter" or "-f": o.Filter = Value(); break;
                case "--host": o.Host = Value().TrimEnd('.').ToLowerInvariant(); break;
                case "--label": o.Label = Value(); break;
                case "--tshark": o.Tshark = Value(); break;
                case "--output": o.Output = Value(); break;
                case "--json": o.Json = true; break;
                case "--include-unattributed": o.IncludeUnattributed = true; break;
                case "--seconds": o.Seconds = PositiveInt(Value(), "seconds", 86400); break;
                case "--pid": o.Pids.Add(PositiveInt(Value(), "pid", int.MaxValue)); break;
                case "--process": o.Processes.Add(Value()); break;
                default: throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }
        if (!o.Help && !o.List && string.IsNullOrWhiteSpace(o.Interface))
            throw new ArgumentException("Choose --interface from --list.");
        return o;
    }

    private static int PositiveInt(string text, string option, int maximum) =>
        int.TryParse(text, out int value) && value > 0 && value <= maximum
            ? value : throw new ArgumentException($"--{option} must be between 1 and {maximum}.");

    public bool Accept(Owner[] owners, string? host)
    {
        if (Host is not null && !TrafficClassifier.IsDomain(host, Host)) return false;
        if (Pids.Count == 0 && Processes.Count == 0) return true;
        if (owners.Length == 0) return IncludeUnattributed;
        return owners.Any(owner => (Pids.Count == 0 || Pids.Contains(owner.Pid)) &&
            (Processes.Count == 0 || Processes.Any(name =>
                owner.Name?.Contains(name, StringComparison.OrdinalIgnoreCase) == true)));
    }

    public const string Usage = """
        Broiler.Traffic — Windows packet capture with best-effort process attribution

        --list                         List Npcap capture interfaces
        --interface, -i <number/name>   Required capture interface from --list
        --filter, -f <BPF expression>   Capture filter (default: tcp or udp)
        --seconds <1..86400>            Capture duration (default: 60); Ctrl+C stops early
        --process <name fragment>      Filter owning process; repeat for alternatives
        --pid <number>                 Filter owning PID; repeat for alternatives
        --include-unattributed         Keep unknown owners when filtering processes
        --host <domain>                Observed hostname/domain suffix filter
        --label <text>                 User-assigned test/use-case label on every record
        --output <new.jsonl>           Also write matching metadata as JSON Lines
        --json                        JSON Lines on stdout instead of a readable table
        --tshark <path>                Override installed Wireshark/tshark.exe
        --help, -h                     Show help

        Process/host filters apply AFTER capture. HTTPS paths/statuses are normally
        encrypted. Hostname/service hints and sampled socket ownership are not proof
        of a browser tab, user action, or the cause of an HTTP 429. Output is metadata only;
        TShark can use temporary packet files while capturing.
        """;
}
