namespace Broiler.Cli.Tests;

/// <summary>The loopback test servers never listen on a port Broiler.Net refuses (<see cref="LoopbackPorts"/>).</summary>
public sealed class LoopbackPortsTests
{
    /// <summary>
    /// The servers skip exactly the bad ports Broiler.Net refuses that an ephemeral port can be -- those from 1024
    /// up -- so no test lands on one ("Port 6667 is blocked" on a machine whose dynamic range starts at 1024).
    /// </summary>
    [Fact(Timeout = 600000)]
    public void TheServersSkipThePortsBroilerNetRefuses()
    {
        var field = typeof(Broiler.Net.Http.BrowserNetworkSession).GetField(
            "BadPorts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var refused = Assert.IsAssignableFrom<IEnumerable<int>>(field?.GetValue(null));

        var ephemeral = Enumerable.Range(1024, 65536 - 1024).ToList();
        Assert.Equal(ephemeral.Where(refused.Contains), ephemeral.Where(LoopbackPorts.IsBad));
    }
}
