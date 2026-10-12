using Broiler.Graphics.Geometry;
using Broiler.Graphics.Paging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.PrintDialog.Standard;
using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// Ctrl+P prints the page as it is: laid out again for print media, broken into pages where the
/// page breaks, and drawn as the pages the print dialog previews and the job sends.
/// </summary>
/// <remarks>Markers are assembled at run time, so the one found painted is never one in a style or a script.</remarks>
public sealed class BrowserPrintTests
{
    private static string Marker(string name) => name + "marker";

    private static string Painted(BPage page) =>
        string.Join(" ", page.Commands.OfType<BRenderCommand.DrawText>().Select(command => command.Text.Text));

    [Fact]
    public void A_Page_Prints_For_Print_Media_Broken_Where_It_Breaks()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><head><style>@media print { .screen { display: none } }</style></head><body>" +
            $"<p class=\"screen\">{Marker("screen")}</p><p>{Marker("first")}</p>" +
            $"<p style=\"break-before: page\">{Marker("second")}</p></body></html>"));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        var printer = new FakePrintHost();
        window.PrintHost = printer;

        window.PressPrint();

        StandardPrintDialog dialog = Assert.IsType<StandardPrintDialog>(window.Session.ModalElement);
        IBPageSource pages = Assert.IsAssignableFrom<IBPageSource>(dialog.Pages);
        Assert.Equal(2, pages.PageCount);
        Assert.Contains(Marker("first"), Painted(pages.GetPage(0)), StringComparison.Ordinal);
        Assert.DoesNotContain(Marker("second"), Painted(pages.GetPage(0)), StringComparison.Ordinal);
        Assert.DoesNotContain(Marker("screen"), Painted(pages.GetPage(0)), StringComparison.Ordinal);
        Assert.Contains(Marker("second"), Painted(pages.GetPage(1)), StringComparison.Ordinal);
        Assert.Equal(BPaperSize.A4.Size, pages.GetPage(0).Size);

        Assert.True(dialog.AcceptPrint());
        WaitForPrint(window);

        UiPrintJob job = Assert.Single(printer.Jobs);
        Assert.Same(pages, job.Pages);
        Assert.Equal("Printed 2 pages", window.Status);
    }

    [Fact]
    public void A_Long_Page_Runs_Onto_Pages_That_Each_Hold_Only_Their_Own_Band()
    {
        string paragraphs = string.Concat(Enumerable.Range(1, 120).Select(i => $"<p>{Marker("line" + i)}</p>"));
        using var server = new LoopbackHttpServer();
        server.Map("/long", Reply.Text($"<!DOCTYPE html><html><body>{paragraphs}</body></html>"));
        using var window = new TestWindow(server.Url("/long"));
        window.Settle();
        window.PrintHost = new FakePrintHost();

        window.PressPrint();

        StandardPrintDialog dialog = Assert.IsType<StandardPrintDialog>(window.Session.ModalElement);
        IBPageSource pages = dialog.Pages!;
        Assert.True(pages.PageCount >= 3, $"120 paragraphs laid out to {pages.PageCount} page(s).");
        string first = " " + Painted(pages.GetPage(0)) + " ";
        string last = " " + Painted(pages.GetPage(pages.PageCount - 1)) + " ";
        Assert.Contains(" " + Marker("line1") + " ", first, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker("line120"), first, StringComparison.Ordinal);
        Assert.Contains(Marker("line120"), last, StringComparison.Ordinal);
        Assert.DoesNotContain(" " + Marker("line1") + " ", last, StringComparison.Ordinal);

        dialog.Cancel();
        WaitForPrint(window);
        Assert.Equal("Printing cancelled", window.Status);
    }

    [Fact]
    public void Without_A_Print_Host_Ctrl_P_Says_So()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text("<!DOCTYPE html><html><body><p>x</p></body></html>"));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        window.PressPrint();

        Assert.Null(window.Session.ModalElement);
        Assert.Equal("Printing is not available here", window.Status);
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(95, 110, false)]
    [InlineData(100, 120, false)]
    [InlineData(150, 210, false)]
    [InlineData(200, 220, true)]
    public void A_Command_Wholly_Outside_A_Band_Is_Left_Off_Its_Page(double top, double bottom, bool outside)
    {
        var rect = new BRenderCommand.FillRect(BRect.FromLTRB(0, top, 10, bottom), Broiler.Graphics.Color.BColor.Red);

        Assert.Equal(outside, BrowserPrintPages.IsOutside(rect, 100, 200));
        Assert.False(BrowserPrintPages.IsOutside(new BRenderCommand.PushClip(new BRect(0, 0, 1, 1)), 100, 200));
        Assert.True(BrowserPrintPages.IsOutside(new BRenderCommand.DrawText(new BTextRun("x"), new BPoint(0, 300)), 100, 200));
    }

    private static void WaitForPrint(TestWindow window)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        do
        {
            window.Paint();
            if (!window.IsPrinting)
                return;

            Thread.Sleep(5);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail("The print job never finished.");
    }

    private sealed class FakePrintHost : IUiPrintHost
    {
        public List<UiPrintJob> Jobs { get; } = [];

        public bool ShowsSystemPrintDialog => false;

        public IReadOnlyList<UiPrinterInfo> GetPrinters() => [new UiPrinterInfo("Office", IsDefault: true)];

        public UiPrinterCapabilities GetCapabilities(string printerName) => UiPrinterCapabilities.Unknown;

        public Task<UiPrintResult> PrintAsync(UiPrintJob job, CancellationToken cancellationToken = default)
        {
            Jobs.Add(job);
            return Task.FromResult(UiPrintResult.Printed(job.ResolvePages(cancellationToken).PageCount));
        }
    }
}
