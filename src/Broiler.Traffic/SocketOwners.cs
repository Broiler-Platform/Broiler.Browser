using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Broiler.Traffic;

internal sealed record SocketRow(string Transport, string LocalAddress, int LocalPort,
    string? RemoteAddress, int RemotePort, int Pid);
internal sealed record Owner(int Pid, string? Name, string PacketSide, string Evidence);
internal sealed record Attribution(DateTimeOffset? SnapshotTime, Owner[] Owners);

internal sealed class SocketSnapshot
{
    internal DateTimeOffset Time { get; }
    private readonly ILookup<(string, string, int), SocketRow> rows;
    private readonly IReadOnlyDictionary<int, string?> names;
    private readonly HashSet<string> localAddresses;

    internal SocketSnapshot(DateTimeOffset time, IEnumerable<SocketRow> rows,
        IReadOnlyDictionary<int, string?> names, HashSet<string> localAddresses)
    {
        Time = time;
        this.rows = rows.Where(r => r.Pid > 0).ToLookup(r => (r.Transport, r.LocalAddress, r.LocalPort));
        this.names = names;
        this.localAddresses = localAddresses;
    }

    internal Owner[] Match(Packet p)
    {
        var matches = new List<Owner>();
        MatchSide(p.Source, p.SourcePort, p.Destination, p.DestinationPort, "source");
        MatchSide(p.Destination, p.DestinationPort, p.Source, p.SourcePort, "destination");
        return matches.Distinct().ToArray();

        void MatchSide(string local, int port, string remote, int remotePort, string side)
        {
            // Capture fields have no IPv6 zone ID. Avoid assigning scoped addresses to the wrong interface.
            if (IPAddress.Parse(local).IsIPv6LinkLocal || IPAddress.Parse(remote).IsIPv6LinkLocal) return;
            var candidates = rows[(p.Transport, local, port)];
            if (p.Transport == "UDP" && localAddresses.Contains(local))
                candidates = candidates.Concat(rows[("UDP", local.Contains(':') ? "::" : "0.0.0.0", port)]);
            foreach (var row in candidates)
            {
                if (p.Transport == "TCP" && (row.RemoteAddress != remote || row.RemotePort != remotePort)) continue;
                names.TryGetValue(row.Pid, out string? name);
                matches.Add(new(row.Pid, name, side, p.Transport == "TCP"
                    ? "TCP full tuple in sampled OS table" : "UDP local endpoint candidate; peer unavailable"));
            }
        }
    }
}

internal sealed class SocketSampler
{
    private readonly object gate = new();
    private readonly List<SocketSnapshot> history = [];

    internal void Refresh()
    {
        var rows = WindowsSocketTable.Read();
        var names = new Dictionary<int, string?>();
        foreach (int pid in rows.Select(r => r.Pid).Where(pid => pid > 0).Distinct())
        {
            try { using var process = Process.GetProcessById(pid); names[pid] = process.ProcessName; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            { names[pid] = null; }
        }
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => new IPAddress(a.Address.GetAddressBytes()).ToString()).ToHashSet();
        addresses.Add("127.0.0.1");
        addresses.Add("::1");
        var snapshot = new SocketSnapshot(DateTimeOffset.UtcNow, rows, names, addresses);
        lock (gate)
        {
            history.Add(snapshot);
            history.RemoveAll(s => snapshot.Time - s.Time > TimeSpan.FromSeconds(5));
        }
    }

    internal async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(token)) Refresh();
    }

    internal Attribution Match(Packet packet)
    {
        SocketSnapshot? snapshot;
        lock (gate)
            snapshot = history.MinBy(s => Math.Abs((s.Time - packet.Time).TotalMilliseconds));
        // Never apply today's socket owners to old/delayed packets without a nearby observation.
        if (snapshot is null || Math.Abs((snapshot.Time - packet.Time).TotalMilliseconds) > 500)
            return new(null, []);
        return new(snapshot.Time, snapshot.Match(packet));
    }
}

internal static class WindowsSocketTable
{
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);

    internal static List<SocketRow> Read()
    {
        var result = new List<SocketRow>();
        foreach (bool tcp in new[] { true, false })
        foreach (bool ipv6 in new[] { false, true })
            result.AddRange(ReadTable(tcp, ipv6));
        return result;
    }

    private static List<SocketRow> ReadTable(bool tcp, bool ipv6)
    {
        uint Read(IntPtr buffer, ref int size) => tcp
            ? GetExtendedTcpTable(buffer, ref size, false, ipv6 ? 23 : 2, 5, 0) // OWNER_PID_ALL
            : GetExtendedUdpTable(buffer, ref size, false, ipv6 ? 23 : 2, 1, 0); // OWNER_PID
        int size = 0;
        uint error = Read(IntPtr.Zero, ref size);
        if (error != 122 && error != 0) throw new Win32Exception((int)error, "Reading Windows socket owners failed.");
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int allocated = Math.Max(size, 4);
            IntPtr buffer = Marshal.AllocHGlobal(allocated);
            try
            {
                error = Read(buffer, ref size);
                if (error == 122) continue; // Table grew between calls.
                if (error != 0) throw new Win32Exception((int)error, "Reading Windows socket owners failed.");
                var bytes = new byte[allocated];
                Marshal.Copy(buffer, bytes, 0, allocated);
                return Decode(bytes, tcp, ipv6);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new IOException("Windows socket table kept growing; could not take a snapshot.");
    }

    internal static List<SocketRow> Decode(byte[] bytes, bool tcp, bool ipv6)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        int rowSize = tcp ? (ipv6 ? 56 : 24) : (ipv6 ? 28 : 12);
        if (count < 0 || count > (bytes.Length - 4) / rowSize) throw new IOException("Invalid Windows socket table size.");
        var rows = new List<SocketRow>(count);
        for (int i = 0; i < count; i++)
        {
            int start = 4 + i * rowSize;
            int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(start + offset, 4));
            int Port(int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(start + offset, 2));
            string Address(int offset) => new IPAddress(bytes.AsSpan(start + offset, ipv6 ? 16 : 4)).ToString();
            if (tcp && Int(ipv6 ? 48 : 0) == 2) continue; // LISTEN has no meaningful remote endpoint.
            rows.Add(tcp
                ? new("TCP", Address(ipv6 ? 0 : 4), Port(ipv6 ? 20 : 8), Address(ipv6 ? 24 : 12),
                    Port(ipv6 ? 44 : 16), Int(ipv6 ? 52 : 20))
                : new("UDP", Address(0), Port(ipv6 ? 20 : 4), null, 0, Int(ipv6 ? 24 : 8)));
        }
        return rows;
    }
}
