using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.HtmlBridge;
using Broiler.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

// Setup test TLS certificate
using var key = RSA.Create(2048);
var certRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var generated = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2));
using var cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);

// Dummy assets
byte[] png1x1 = [
    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
    0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
    0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
    0x42, 0x60, 0x82
];
byte[] dummyFont = [0x77, 0x4F, 0x46, 0x32, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10];

// Endpoint 1: HTTP/2 (ALPN negotiates h2)
var builderH2 = WebApplication.CreateBuilder();
builderH2.Logging.ClearProviders();
builderH2.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, l => { l.Protocols = HttpProtocols.Http1AndHttp2; l.UseHttps(cert); }));
await using var serverH2 = builderH2.Build();
var statsH2 = new Stats();
ConfigureServerRoutes(serverH2, statsH2);
await serverH2.StartAsync();
var baseUrlH2 = serverH2.Urls.Single();

// Endpoint 2: HTTP/1.1 (ALPN negotiates http/1.1)
var builderH1 = WebApplication.CreateBuilder();
builderH1.Logging.ClearProviders();
builderH1.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, l => { l.Protocols = HttpProtocols.Http1; l.UseHttps(cert); }));
await using var serverH1 = builderH1.Build();
var statsH1 = new Stats();
ConfigureServerRoutes(serverH1, statsH1);
await serverH1.StartAsync();
var baseUrlH1 = serverH1.Urls.Single();

void ConfigureServerRoutes(WebApplication server, Stats stats)
{
    server.Run(async context =>
    {
        stats.Enter(context);
        try
        {
            var path = context.Request.Path.Value ?? "";
            if (path == "/representative-page")
            {
                await Task.Delay(50, context.RequestAborted);
                context.Response.ContentType = "text/html; charset=utf-8";
                var baseHost = $"{context.Request.Scheme}://{context.Request.Host}";
                var html = $$"""
                    <!DOCTYPE html>
                    <html>
                    <head>
                      <title>Representative Benchmark</title>
                      <link rel="stylesheet" href="{{baseHost}}/css/style1.css">
                      <link rel="stylesheet" href="{{baseHost}}/css/style2.css">
                      <link rel="stylesheet" href="{{baseHost}}/css/style3.css">
                      <link rel="stylesheet" href="{{baseHost}}/css/style4.css">
                      <link rel="stylesheet" href="{{baseHost}}/css/style5.css">
                      <link rel="stylesheet" href="{{baseHost}}/css/style6.css">
                      <script src="{{baseHost}}/js/script1.js"></script>
                      <script src="{{baseHost}}/js/script2.js"></script>
                      <script src="{{baseHost}}/js/script3.js"></script>
                      <script src="{{baseHost}}/js/script4.js"></script>
                    </head>
                    <body>
                      <h1>Representative Benchmark Page</h1>
                      <img src="{{baseHost}}/img/image1.png" alt="1">
                      <img src="{{baseHost}}/img/image2.png" alt="2">
                      <img src="{{baseHost}}/img/image3.png" alt="3">
                      <img src="{{baseHost}}/img/image4.png" alt="4">
                      <img src="{{baseHost}}/img/image5.png" alt="5">
                      <img src="{{baseHost}}/img/image6.png" alt="6">
                      <div id="out">initial</div>
                      <script>
                        Promise.all([
                          fetch('{{baseHost}}/api/data1').then(r => r.text()),
                          fetch('{{baseHost}}/api/data2').then(r => r.text()),
                          fetch('{{baseHost}}/css/style1.css').then(r => r.text()),
                          fetch('{{baseHost}}/fonts/font1.woff2').then(r => r.text())
                        ]).then(([a, b]) => {
                          document.getElementById('out').textContent = a + '+' + b;
                        });
                      </script>
                    </body>
                    </html>
                    """;
                await context.Response.WriteAsync(html);
            }
            else if (path.StartsWith("/css/"))
            {
                await Task.Delay(50, context.RequestAborted);
                context.Response.ContentType = "text/css; charset=utf-8";
                var baseHost = $"{context.Request.Scheme}://{context.Request.Host}";
                var css = path.EndsWith("style1.css")
                    ? $"@font-face {{ font-family: 'TestFont'; src: url('{baseHost}/fonts/font1.woff2'); }}\nbody {{ font-family: 'TestFont'; color: #222; }}"
                    : "body { margin: 0; padding: 10px; }";
                await context.Response.WriteAsync(css);
            }
            else if (path.StartsWith("/js/"))
            {
                await Task.Delay(50, context.RequestAborted);
                context.Response.ContentType = "application/javascript; charset=utf-8";
                await context.Response.WriteAsync("/* script */");
            }
            else if (path.StartsWith("/img/"))
            {
                await Task.Delay(50, context.RequestAborted);
                context.Response.ContentType = "image/png";
                await context.Response.Body.WriteAsync(png1x1);
            }
            else if (path.StartsWith("/fonts/"))
            {
                await Task.Delay(50, context.RequestAborted);
                context.Response.ContentType = "font/woff2";
                await context.Response.Body.WriteAsync(dummyFont);
            }
            else if (path.StartsWith("/api/"))
            {
                await Task.Delay(50, context.RequestAborted);
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("data");
            }
            else if (path == "/stream")
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("first");
                await context.Response.Body.FlushAsync();
                await Task.Delay(400, context.RequestAborted);
                await context.Response.WriteAsync("done");
            }
            else if (path == "/warmup")
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("warm");
            }
            else
            {
                await Task.Delay(200, context.RequestAborted);
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("done");
            }
        }
        finally
        {
            stats.Exit();
        }
    });
}

SocketsHttpHandler CreateSocketsHandler() => new()
{
    AllowAutoRedirect = false,
    UseCookies = false,
    UseProxy = false,
    EnableMultipleHttp2Connections = false,
    SslOptions = new SslClientAuthenticationOptions
    {
        RemoteCertificateValidationCallback = (_, remote, _, _) => remote?.GetCertHashString() == cert.GetCertHashString()
    },
};

var allResults = new List<BenchmarkResult>();

async Task RunProtocolSuite(string protocolName, string baseUrl, Stats stats)
{
    Console.WriteLine($"\n========================================================");
    Console.WriteLine($"  RUNNING BENCHMARK SUITE: {protocolName} ({baseUrl})");
    Console.WriteLine($"========================================================");

    using var socketsHandler = CreateSocketsHandler();
    using var profile = BrowserProfile.CreateEphemeral(socketsHandler);
    var network = profile.Network;

    async Task<string?> FetchAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await network.SendAsync(request, RequestContext.TopLevelNavigation(null));
        return await response.Message.Content.ReadAsStringAsync();
    }

    string RunJs(string script)
    {
        var engine = new ScriptEngine(new DomBridgeFactory(new DomBridgeSessionOptions { Network = network, Cookies = profile.DocumentCookies }));
        var html = engine.Execute([script], "<html><body><div id=\"out\"></div></body></html>", baseUrl + "/page");
        return Regex.Match(html, "<div id=\"out\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
    }

    // Warmup
    await FetchAsync(baseUrl + "/warmup");
    RunJs("document.getElementById('out').textContent = 'warm';");

    async Task<BenchmarkResult> Measure(string label, Func<Task<string>> action)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        stats.Reset();
        var g0Before = GC.CollectionCount(0);
        var g1Before = GC.CollectionCount(1);
        var g2Before = GC.CollectionCount(2);
        var allocBefore = GC.GetTotalAllocatedBytes(precise: true);

        ThreadPool.GetAvailableThreads(out int availWorkersBefore, out _);
        ThreadPool.GetMaxThreads(out int maxWorkers, out _);
        var threadCountBefore = ThreadPool.ThreadCount;

        var watch = Stopwatch.StartNew();
        var detail = await action();
        watch.Stop();

        ThreadPool.GetAvailableThreads(out int availWorkersAfter, out _);
        var threadCountAfter = ThreadPool.ThreadCount;
        var allocAfter = GC.GetTotalAllocatedBytes(precise: true);

        var result = new BenchmarkResult(
            Protocol: protocolName,
            Label: label,
            ElapsedMs: watch.ElapsedMilliseconds,
            Requests: stats.Count,
            PeakConcurrent: stats.Peak,
            Connections: stats.Connections.Count,
            NegotiatedProtocols: stats.Protocols.Keys.ToArray(),
            AllocatedBytes: allocAfter - allocBefore,
            Gen0Collections: GC.CollectionCount(0) - g0Before,
            Gen1Collections: GC.CollectionCount(1) - g1Before,
            Gen2Collections: GC.CollectionCount(2) - g2Before,
            ThreadCount: threadCountAfter,
            BusyWorkers: Math.Max(0, maxWorkers - Math.Min(availWorkersBefore, availWorkersAfter)),
            Detail: detail);

        allResults.Add(result);
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result;
    }

    // 1. Representative Page Load (Cold & Warm)
    for (var round = 1; round <= 2; round++)
    {
        var roundNum = round;
        await Measure($"page-load-round{roundNum}", async () =>
        {
            var scriptEngine = new ScriptEngine(new DomBridgeFactory(new DomBridgeSessionOptions { Network = network, Cookies = profile.DocumentCookies }));
            using var pipeline = new RenderingPipeline(new PageLoader(network), scriptEngine, network);
            var page = await pipeline.LoadAsync(PageRequest.ForUrl(baseUrl + "/representative-page"));
            using var session = pipeline.ExecuteScriptsInteractive(page.Content, page.InheritedPolicy, page);
            var html = session?.SettleLoadWindow() ?? string.Empty;
            var outMatch = Regex.Match(html, "<div id=\"out\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
            return $"out={outMatch},protocol={page.FetchTiming?.NextHopProtocol}";
        });
    }

    // 2. Prefetcher Concurrency Batches (12, 16, 24 subresources)
    foreach (var count in new[] { 12, 16, 24 })
    {
        for (var round = 1; round <= 2; round++)
        {
            var r = round;
            var c = count;
            var urls = Enumerable.Range(0, c).Select(i => baseUrl + $"/delay?batch={c}&round={r}&id={i}").ToArray();
            await Measure($"prefetch-{c}-round{r}", () =>
            {
                var prefetcher = new SubResourcePrefetcher(FetchAsync);
                prefetcher.Prefetch(urls);
                return Task.FromResult(string.Join(',', urls.Select(prefetcher.Consume)));
            });
        }
    }

    // 3. JavaScript Microbenchmarks (Fetch, XHR, Beacon, Stream)
    for (var round = 1; round <= 2; round++)
    {
        var r = round;
        await Measure($"js-fetch-2-round{r}", () => Task.FromResult(RunJs("var t=Date.now(); var a=fetch('/delay?a'); var b=fetch('/delay?b'); document.getElementById('out').textContent = 'callsReturnedMs='+(Date.now()-t);")));
        await Measure($"js-async-xhr-2-round{r}", () => Task.FromResult(RunJs("var t=Date.now(); var a=new XMLHttpRequest(); a.open('GET','/delay?a',true); a.send(); var b=new XMLHttpRequest(); b.open('GET','/delay?b',true); b.send(); document.getElementById('out').textContent = 'callsReturnedMs='+(Date.now()-t);")));
        await Measure($"js-beacon-2-round{r}", () => Task.FromResult(RunJs("var t=Date.now(); var a=navigator.sendBeacon('/delay?a','a'); var b=navigator.sendBeacon('/delay?b','b'); document.getElementById('out').textContent = a+','+b+',callsReturnedMs='+(Date.now()-t);")));
        await Measure($"js-fetch-stream-round{r}", () => Task.FromResult(RunJs("var t=Date.now(); var a=fetch('/stream'); document.getElementById('out').textContent = 'callReturnedMs='+(Date.now()-t);")));
    }
}

// Run HTTP/2
await RunProtocolSuite("HTTP/2", baseUrlH2, statsH2);

// Run HTTP/1.1
await RunProtocolSuite("HTTP/1.1", baseUrlH1, statsH1);

// Write outputs
var json = JsonSerializer.Serialize(allResults, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "results.json"), json);
var currentDir = AppContext.BaseDirectory;
string? repoRoot = null;
while (!string.IsNullOrEmpty(currentDir))
{
    if (Directory.Exists(Path.Combine(currentDir, "docs", "repros")))
    {
        repoRoot = currentDir;
        break;
    }
    currentDir = Path.GetDirectoryName(currentDir);
}
var reproPath = Path.Combine(repoRoot ?? AppContext.BaseDirectory, "docs/repros/http2-async-step5-comparison-2026-10-11.json");
await File.WriteAllTextAsync(reproPath, json);
Console.WriteLine($"\nComparative results written to: {reproPath}");

record BenchmarkResult(
    string Protocol,
    string Label,
    long ElapsedMs,
    int Requests,
    int PeakConcurrent,
    int Connections,
    string[] NegotiatedProtocols,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ThreadCount,
    int BusyWorkers,
    string Detail
);

sealed class Stats
{
    public int Count, Peak;
    private int _active;
    public ConcurrentDictionary<string, byte> Connections = new();
    public ConcurrentDictionary<string, byte> Protocols = new();

    public void Enter(HttpContext context)
    {
        Interlocked.Increment(ref Count);
        Connections.TryAdd(context.Connection.Id, 0);
        Protocols.TryAdd(context.Request.Protocol, 0);
        var active = Interlocked.Increment(ref _active);
        int old;
        do { old = Peak; if (active <= old) break; } while (Interlocked.CompareExchange(ref Peak, active, old) != old);
    }

    public void Exit() => Interlocked.Decrement(ref _active);
    public void Reset() { Count = Peak = _active = 0; Connections.Clear(); Protocols.Clear(); }
}
