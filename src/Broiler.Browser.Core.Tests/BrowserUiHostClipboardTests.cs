using Broiler.Browser;
using Broiler.Graphics;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The host's clipboard port. What it must never do is hold a private string:
/// that was what made copy and paste look like they worked while nothing
/// outside the process could see either one.
/// </summary>
public sealed class BrowserUiHostClipboardTests
{
    [Fact(Timeout = 600000)]
    public void A_Host_With_No_Platform_Clipboard_Reports_No_Text()
    {
        using BrowserUiHost host = CreateHost(null, null);

        host.SetText("copied");

        Assert.False(host.TryGetText(out string text));
        Assert.Empty(text);
    }

    [Fact(Timeout = 600000)]
    public void A_Copy_Reaches_The_Platform_Clipboard()
    {
        string? written = null;
        using BrowserUiHost host = CreateHost(() => written, text => written = text);

        host.SetText("copied");

        Assert.Equal("copied", written);
        Assert.True(host.TryGetText(out string text));
        Assert.Equal("copied", text);
    }

    [Fact(Timeout = 600000)]
    public void A_Paste_Reads_The_Platform_Clipboard_Every_Time()
    {
        string? platform = "first";
        using BrowserUiHost host = CreateHost(() => platform, text => platform = text);

        Assert.True(host.TryGetText(out string before));
        Assert.Equal("first", before);

        // Another application copied. Nothing may be cached from the earlier
        // read, or Broiler pastes what the clipboard used to hold.
        platform = "second";

        Assert.True(host.TryGetText(out string after));
        Assert.Equal("second", after);
    }

    [Fact(Timeout = 600000)]
    public void An_Empty_Platform_Clipboard_Reports_No_Text()
    {
        using BrowserUiHost host = CreateHost(static () => string.Empty, static _ => { });

        Assert.False(host.TryGetText(out string text));
        Assert.Empty(text);
    }

    private static BrowserUiHost CreateHost(Func<string?>? read, Action<string>? write) =>
        new(
            static () => new BSize(800, 600),
            static () => 1.0,
            static () => { },
            static _ => { },
            static _ => true,
            read,
            write);
}
