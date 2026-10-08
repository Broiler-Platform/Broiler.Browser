using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Broiler.Traffic.Tests;

public sealed class TrafficTests
{
    private static Packet Packet(string transport = "TCP", string source = "192.0.2.10", int sourcePort = 50123,
        string destination = "203.0.113.8", int destinationPort = 443) =>
        new(DateTimeOffset.UtcNow, 100, transport, source, sourcePort, destination, destinationPort,
            transport, null, null, null, null, null, "1");

    private static SocketSnapshot Snapshot(params SocketRow[] rows) => new(DateTimeOffset.UtcNow, rows,
        new Dictionary<int, string?> { [42] = "Broiler.Browser", [43] = "chrome" }, ["192.0.2.10", "::1"]);

    [Fact]
    public void TcpMatchesBothDirectionsButNotAnotherPeerWithSamePort()
    {
        var snapshot = Snapshot(new SocketRow("TCP", "192.0.2.10", 50123, "203.0.113.8", 443, 42));
        var packet = Packet();
        Assert.Equal("source", Assert.Single(snapshot.Match(packet)).PacketSide);
        Assert.Equal("destination", Assert.Single(snapshot.Match(Packet(source: packet.Destination,
            sourcePort: 443, destination: packet.Source, destinationPort: 50123))).PacketSide);
        Assert.Empty(snapshot.Match(packet with { Destination = "203.0.113.9" }));
    }

    [Fact]
    public void UdpWildcardRequiresLocalAddressAndPreservesAmbiguousPids()
    {
        var snapshot = Snapshot(new("UDP", "0.0.0.0", 50123, null, 0, 42),
            new("UDP", "0.0.0.0", 50123, null, 0, 43));
        Assert.Equal(2, snapshot.Match(Packet("UDP")).Length);
        Assert.Empty(snapshot.Match(Packet("UDP", source: "192.0.2.99")));
    }

    [Fact]
    public void LoopbackPreservesBothEndpointOwners()
    {
        var snapshot = Snapshot(new("TCP", "::1", 50123, "::1", 8000, 42),
            new("TCP", "::1", 8000, "::1", 50123, 43));
        Assert.Equal(2, snapshot.Match(Packet(source: "::1", destination: "::1", destinationPort: 8000)).Length);
    }

    [Fact]
    public void LinkLocalWithoutInterfaceScopeIsNotAttributed()
    {
        var snapshot = Snapshot(new SocketRow("UDP", "fe80::1", 50123, null, 0, 42));
        Assert.Empty(snapshot.Match(Packet("UDP", "fe80::1")));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void NativeTablesDecodeNetworkOrderAddressesPortsAndPid(bool tcp, bool ipv6)
    {
        int size = tcp ? ipv6 ? 56 : 24 : ipv6 ? 28 : 12;
        var bytes = new byte[4 + size];
        void Int(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + offset), value);
        void Port(int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4 + offset), value);
        string address = ipv6 ? "2001:db8::1" : "192.0.2.10";
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        IPAddress.Parse(address).GetAddressBytes().CopyTo(bytes, 4 + (tcp && !ipv6 ? 4 : 0));
        Port(ipv6 ? 20 : tcp ? 8 : 4, 50123);
        if (tcp)
        {
            Int(ipv6 ? 48 : 0, 5);
            IPAddress.Parse(address).GetAddressBytes().CopyTo(bytes, 4 + (ipv6 ? 24 : 12));
            Port(ipv6 ? 44 : 16, 443);
        }
        Int(tcp ? ipv6 ? 52 : 20 : ipv6 ? 24 : 8, 123456);
        var row = Assert.Single(WindowsSocketTable.Decode(bytes, tcp, ipv6));
        Assert.Equal(address, row.LocalAddress);
        Assert.Equal(50123, row.LocalPort);
        Assert.Equal(123456, row.Pid);
        Assert.Equal(tcp ? 443 : 0, row.RemotePort);
    }

    [Fact]
    public void MalformedTableCountIsRejected()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, int.MaxValue);
        Assert.Throws<IOException>(() => WindowsSocketTable.Decode(bytes, true, true));
    }

    [Fact]
    public void TsvParsesIpv6AndDropsQueryTokens()
    {
        string[] fields = ["1791453600.123456000", "120", "", "", "2001:db8::1", "2001:db8::2",
            "50123", "80", "", "", "HTTP", "", "", "www.google.com", "/search?q=private&token=secret", "", "7", ""];
        var packet = Assert.IsType<Packet>(Broiler.Traffic.Packet.Parse(string.Join('\t', fields)));
        Assert.Equal("2001:db8::1", packet.Source);
        Assert.Equal("/search", packet.RequestPath);
        Assert.Equal(50123, packet.SourcePort);
        Assert.Equal(1234560, packet.Time.Ticks % TimeSpan.TicksPerSecond);
        fields[2] = "192.0.2.1"; // Mixed inner/outer protocol layers.
        Assert.Null(Broiler.Traffic.Packet.Parse(string.Join('\t', fields)));
    }

    [Fact]
    public void HostHintsCarryWithinStreamButNeverClaimSearchFromSniAlone()
    {
        var classifier = new TrafficClassifier();
        var packet = Packet() with { Sni = "www.google.com" };
        Assert.Contains("unknown", classifier.Classify(packet).UseCase);
        var hint = classifier.Classify(packet with { Sni = null, Time = packet.Time.AddSeconds(1) });
        Assert.Equal("www.google.com", hint.Host);
        Assert.StartsWith("Earlier", hint.Evidence);
        Assert.Null(classifier.Classify(packet with { Sni = null, Stream = "2" }).Host);
        Assert.Null(classifier.Classify(packet with { Sni = null, Time = packet.Time.AddMinutes(6) }).Host);
        Assert.Contains("visible HTTP path", classifier.Classify(packet with { HttpHost = "www.google.com", RequestPath = "/search" }).UseCase);
    }

    [Fact]
    public void DomainFilterRejectsLookalikeAndSuffixSpoof()
    {
        Assert.True(TrafficClassifier.IsDomain("www.google.com", "google.com"));
        Assert.False(TrafficClassifier.IsDomain("notgoogle.com", "google.com"));
        Assert.False(TrafficClassifier.IsDomain("google.com.attacker.test", "google.com"));
    }

    [Fact]
    public void ProcessFiltersAreAppliedToSameCandidateAndUnknownIsExplicit()
    {
        var o = Options.Parse(["-i", "9", "--pid", "42", "--process", "Broiler"]);
        Assert.False(o.Accept([], null));
        Assert.False(o.Accept([new(42, "chrome", "source", "test"), new(43, "Broiler", "source", "test")], null));
        Assert.True(o.Accept([new(42, "Broiler.Browser", "source", "test")], null));
        o.IncludeUnattributed = true;
        Assert.True(o.Accept([], null));
    }

    [Theory]
    [InlineData("--pid", "0")]
    [InlineData("--seconds", "-1")]
    [InlineData("--seconds", "86401")]
    [InlineData("--unknown", "x")]
    public void InvalidOptionsFailBeforeCapture(string option, string value) =>
        Assert.Throws<ArgumentException>(() => Options.Parse(["-i", "9", option, value]));

    [Fact]
    public void FilterIsOneProcessArgumentAndCaptureIsNonPromiscuous()
    {
        var args = Program.CaptureArguments(Options.Parse(["-i", "9", "-f", "tcp port 443 or udp port 443"]));
        Assert.Contains("tcp port 443 or udp port 443", args);
        Assert.Contains("-p", args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWindowsSocketsAreAttributedForTcpAndUdp(bool ipv6)
    {
        if (!OperatingSystem.IsWindows() || ipv6 && !Socket.OSSupportsIPv6) return;
        var address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var listener = new TcpListener(address, 0);
        listener.Start();
        using var client = new TcpClient(address.AddressFamily);
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var server = await listener.AcceptTcpClientAsync();
        using var udp = new UdpClient(new IPEndPoint(address, 0));
        var sampler = new SocketSampler();
        sampler.Refresh();
        var local = (IPEndPoint)client.Client.LocalEndPoint!;
        var remote = (IPEndPoint)client.Client.RemoteEndPoint!;
        var tcpPacket = Packet(source: address.ToString(), sourcePort: local.Port,
            destination: address.ToString(), destinationPort: remote.Port);
        var attribution = sampler.Match(tcpPacket);
        Assert.Equal(2, attribution.Owners.Length);
        Assert.All(attribution.Owners, o => Assert.Equal(Environment.ProcessId, o.Pid));
        var udpPacket = Packet("UDP", address.ToString(), ((IPEndPoint)udp.Client.LocalEndPoint!).Port,
            address.ToString(), 12345);
        Assert.Contains(sampler.Match(udpPacket).Owners, o => o.Pid == Environment.ProcessId);
        Assert.Empty(sampler.Match(tcpPacket with { Time = tcpPacket.Time.AddMinutes(-1) }).Owners);
    }
}
