using System.Globalization;
using System.Text.Json;
using Broiler.HTML.Image;
using Broiler.HtmlBridge.Logging;

namespace Broiler.Cli;

// Disambiguate the unqualified `DateTime` type: the Broiler.JS engine now exposes a top-level
// `Broiler.DateTime` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.DateTime by simple-name lookup. The alias must sit inside the Broiler.Cli namespace
// scope so it is resolved before the enclosing `Broiler` namespace's `DateTime` member.
using DateTime = System.DateTime;

/// <summary>
/// Entry point for the Broiler CLI tool.
/// Supports website capture through the browser's own page pipeline, page evaluation, layout
/// fuzzing and engine smoke testing.
/// </summary>
public class Program
{
    /// <summary>
    /// What <c>--convert-doc</c> says now. Document conversion is Broiler.Documents' business, and
    /// that repository has its own command line, <c>broilerdoc</c>, whose <c>convert</c> reads and
    /// writes every format this one did and more. The browser composes no document codec, so it does
    /// not carry a second converter for them.
    /// </summary>
    internal const string ConvertDocMoved =
        "Error: '--convert-doc' is not part of the browser's command line. Document conversion is " +
        "Broiler.Documents' own command line now: broilerdoc convert <input> --out <path> " +
        "(Broiler-Platform/Broiler.Documents, src/Broiler.Documents.Cli).";

    public static async Task<int> Main(string[] args)
    {
        // Composition root: register the concrete image codecs Broiler.Graphics decodes/encodes with.
        Broiler.Graphics.Imaging.BImageCodecs.Use(
            new Broiler.Media.MediaCodecCatalog(Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));

        // Repeatable inputs: one is the single-input path this CLI has always had, more than
        // one is a batch. Accumulating rather than overwriting is what makes `--url a --url b`
        // mean both instead of only the last.
        var urls = new List<string>();
        var captureImageUrls = new List<string>();
        var evaluatePageUrls = new List<string>();
        var evaluateExpressions = new List<string>();
        var analyzeUrls = new List<string>();
        bool verbose = false;
        bool sampleStacks = false;
        int analysisTimeoutSeconds = Analysis.PageAnalysisOptions.DefaultWatchdogSeconds;
        string? evaluateHtmlOutput = null;
        string? output = null;
        string? outputDir = null;
        string? outputFormat = null;
        int? threads = null;
        int? fuzzSeed = null;
        // Set by the parent on a fuzz shard so the child appends a machine-readable totals line
        // for it to sum. Not part of the documented surface — it only means anything to a parent.
        bool emitFuzzTotals = false;
        bool fullPage = false;
        bool testEngines = false;
        bool fuzzLayout = false;
        bool followFirstLink = false;
        bool diagnostics = false;
        string? diagnosticDir = null;
        string? diagnosticLog = null;
        int fuzzCount = 1000;
        int timeoutSeconds = 30;
        int width = 1024;
        int height = 768;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--convert-doc":
                    Console.Error.WriteLine(ConvertDocMoved);
                    return 1;
                case "--url" when i + 1 < args.Length:
                    urls.Add(args[++i]);
                    break;
                case "--capture-image" when i + 1 < args.Length:
                    captureImageUrls.Add(args[++i]);
                    break;
                case "--evaluate-page" when i + 1 < args.Length:
                    evaluatePageUrls.Add(args[++i]);
                    break;
                case "--evaluate" when i + 1 < args.Length:
                    evaluateExpressions.Add(args[++i]);
                    break;
                case "--analyze" when i + 1 < args.Length:
                case "--analyse" when i + 1 < args.Length:
                    analyzeUrls.Add(args[++i]);
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--sample-stacks":
                    sampleStacks = true;
                    break;
                case "--analysis-timeout" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out analysisTimeoutSeconds) || analysisTimeoutSeconds < 0)
                    {
                        Console.Error.WriteLine("Error: '--analysis-timeout' must be a whole number of seconds; 0 turns the watchdog off.");
                        return 1;
                    }
                    break;
                case "--evaluate-html-output" when i + 1 < args.Length:
                    evaluateHtmlOutput = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--output-dir" when i + 1 < args.Length:
                    outputDir = args[++i];
                    break;
                case "--output-format" when i + 1 < args.Length:
                    outputFormat = args[++i];
                    break;
                case "--threads" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out int parsedThreads) || parsedThreads <= 0)
                    {
                        Console.Error.WriteLine("Error: '--threads' must be a positive integer.");
                        return 1;
                    }

                    threads = parsedThreads;
                    break;
                case "--seed" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out int parsedSeed))
                    {
                        Console.Error.WriteLine("Error: '--seed' must be an integer.");
                        return 1;
                    }

                    fuzzSeed = parsedSeed;
                    break;
                case "--timeout" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out timeoutSeconds) || timeoutSeconds <= 0)
                    {
                        Console.Error.WriteLine("Error: '--timeout' must be a positive integer (seconds).");
                        return 1;
                    }
                    break;
                case "--width" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out width) || width <= 0)
                    {
                        Console.Error.WriteLine("Error: '--width' must be a positive integer.");
                        return 1;
                    }
                    break;
                case "--height" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out height) || height <= 0)
                    {
                        Console.Error.WriteLine("Error: '--height' must be a positive integer.");
                        return 1;
                    }
                    break;
                case "--full-page":
                    fullPage = true;
                    break;
                case "--follow-first-link":
                    followFirstLink = true;
                    break;
                case "--test-engines":
                    testEngines = true;
                    break;
                case "--fuzz-layout":
                    fuzzLayout = true;
                    break;
                case "--emit-totals":
                    emitFuzzTotals = true;
                    break;
                case "--diagnostics":
                    diagnostics = true;
                    break;
                case "--diagnostic-dir" when i + 1 < args.Length:
                    diagnosticDir = args[++i];
                    break;
                case "--diagnostic-log" when i + 1 < args.Length:
                    diagnosticLog = args[++i];
                    break;
                case "--count" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out fuzzCount) || fuzzCount <= 0)
                    {
                        Console.Error.WriteLine("Error: '--count' must be a positive integer.");
                        return 1;
                    }
                    break;
                case "--url":
                case "--capture-image":
                case "--evaluate-page":
                case "--evaluate":
                case "--evaluate-html-output":
                case "--output":
                case "--output-dir":
                case "--output-format":
                case "--threads":
                case "--seed":
                case "--timeout":
                case "--width":
                case "--height":
                case "--count":
                case "--diagnostic-dir":
                case "--diagnostic-log":
                case "--analyze":
                case "--analyse":
                case "--analysis-timeout":
                    Console.Error.WriteLine($"Error: '{args[i]}' requires a value.");
                    PrintUsage();
                    return 1;
                case "--help":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Error: Unrecognized argument '{args[i]}'.");
                    PrintUsage();
                    return 1;
            }
        }

        if (testEngines)
        {
            return RunEngineTests();
        }

        if (fuzzLayout)
        {
            var fuzzService = new LayoutFuzzService();
            return fuzzService.Run(
                fuzzCount,
                seed: fuzzSeed,
                outputDir: output,
                threads: threads,
                emitTotals: emitFuzzTotals);
        }

        if (analyzeUrls.Count > 0)
        {
            if (urls.Count > 0 || captureImageUrls.Count > 0 || evaluatePageUrls.Count > 0)
            {
                Console.Error.WriteLine(
                    "Error: '--analyze' is a run of its own; it already captures the page, its image and its " +
                    "diagnostics, so it cannot be combined with '--url', '--capture-image' or '--evaluate-page'.");
                return 1;
            }

            return await RunAnalysis(
                analyzeUrls,
                outputDir,
                width,
                height,
                timeoutSeconds,
                followFirstLink,
                verbose,
                sampleStacks,
                analysisTimeoutSeconds);
        }

        // --evaluate-page is deliberately single-page: unlike a capture, its whole output is one
        // small JSON document, and a caller running many pages wants them attributed to separate
        // files anyway. Rejecting the repeat here is clearer than silently using the first.
        if (evaluatePageUrls.Count > 1)
        {
            Console.Error.WriteLine("Error: '--evaluate-page' accepts one URL; run it once per page.");
            return 1;
        }

        if (evaluatePageUrls.Count == 0 && (evaluateExpressions.Count > 0 || evaluateHtmlOutput is not null))
        {
            Console.Error.WriteLine("Error: '--evaluate' and '--evaluate-html-output' require '--evaluate-page <URL>'.");
            PrintUsage();
            return 1;
        }

        // When --diagnostics is active, subscribe to the render logger and
        // collect entries to emit as structured JSON on stdout after the
        // main operation completes.
        List<RenderLogEntry>? diagnosticEntries = null;
        Action<RenderLogEntry>? diagHandler = null;
        if (diagnostics)
        {
            diagnosticEntries = [];
            diagHandler = entry => { lock (diagnosticEntries) diagnosticEntries.Add(entry); };
            RenderLogger.EntryLogged += diagHandler;
        }

        var diagnosticOptions = ResolveDiagnosticOptions(diagnosticDir, diagnosticLog);
        int exitCode;

        // Batch capture runs each input as its own child process rather than on a thread: the
        // render path still has unsynchronised caches on process-wide singletons (roadmap items
        // #9/#8), so a second render in this process would be a data race. See BatchRunner.
        if (captureImageUrls.Count > 1 || urls.Count > 1)
        {
            // A batch's diagnostics are produced by the children, each into its own sub-bundle, for
            // the same reason the captures themselves are: they run at once, and one log file or one
            // resources/ directory shared between concurrent captures would interleave into evidence
            // that cannot be attributed to a page. A bare --diagnostic-log has nowhere to put them.
            if (diagnosticOptions.IsActive && diagnosticOptions.Directory is null)
            {
                Console.Error.WriteLine(
                    "Error: capturing several URLs with diagnostics requires '--diagnostic-dir <DIR>'; " +
                    "each capture is written to its own sub-directory of <DIR>.");
                return 1;
            }

            exitCode = RunBatchCapture(
                captureImageUrls,
                urls,
                outputDir,
                outputFormat,
                threads,
                width,
                height,
                fullPage,
                followFirstLink,
                timeoutSeconds,
                diagnosticOptions.Directory);
            EmitDiagnostics(diagHandler, diagnosticEntries);
            return exitCode;
        }

        DiagnosticSession? diagnosticSession;
        try
        {
            diagnosticSession = DiagnosticSession.Start(diagnosticOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Failing here rather than degrading to a silent no-op: a mistyped destination is the
            // one diagnostics mistake that is invisible in the run's own output.
            Console.Error.WriteLine($"Error: cannot open the diagnostics destination: {ex.Message}");
            return 1;
        }

        string? url = urls.Count > 0 ? urls[0] : null;
        string? captureImageUrl = captureImageUrls.Count > 0 ? captureImageUrls[0] : null;
        string? evaluatePageUrl = evaluatePageUrls.Count > 0 ? evaluatePageUrls[0] : null;

        if (evaluatePageUrl is not null)
        {
            exitCode = await RunPageEvaluation(
                evaluatePageUrl, output, evaluateExpressions, evaluateHtmlOutput, timeoutSeconds);

            if (diagnosticSession is not null)
            {
                diagnosticSession.Dispose();
                Console.WriteLine(diagnosticSession.Describe());
            }

            EmitDiagnostics(diagHandler, diagnosticEntries);
            return exitCode;
        }

        if (captureImageUrl is not null)
        {
            if (output is null)
            {
                Console.Error.WriteLine("Error: '--output' is required when using '--capture-image'.");
                PrintUsage();
                exitCode = 1;
            }
            else
            {
                // Support bare file paths by converting to file:// URIs.
                // Separate any fragment (e.g. "#top") before checking the filesystem.
                string? captureFragment = null;
                var hashIdx = captureImageUrl.IndexOf('#');
                var captureFilePath = hashIdx >= 0 ? captureImageUrl[..hashIdx] : captureImageUrl;
                if (hashIdx >= 0)
                    captureFragment = captureImageUrl[hashIdx..]; // includes '#'

                if (File.Exists(captureFilePath))
                {
                    captureImageUrl = new Uri(Path.GetFullPath(captureFilePath)).AbsoluteUri
                                      + (captureFragment ?? string.Empty);
                }

                if (!Uri.TryCreate(captureImageUrl, UriKind.Absolute, out var imgUri)
                    || (imgUri.Scheme != "http" && imgUri.Scheme != "https" && imgUri.Scheme != "file"))
                {
                    Console.Error.WriteLine($"Error: '{captureImageUrl}' is not a valid HTTP, HTTPS, or file URL.");
                    exitCode = 1;
                }
                else
                {
                    var imageOptions = new ImageCaptureOptions
                    {
                        Url = captureImageUrl,
                        OutputPath = output,
                        Width = width,
                        Height = height,
                        FullPage = fullPage,
                        FollowFirstLink = followFirstLink,
                        TimeoutSeconds = timeoutSeconds,
                    };

                    try
                    {
                        var service = new CaptureService();
                        await service.CaptureImageAsync(imageOptions);
                        CaptureArtifactMetadata.WriteImageSidecar(output);

                        Console.WriteLine($"Image capture saved to {output} ({CaptureArtifactMetadata.CurrentRenderBackend.Label})");
                        exitCode = 0;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
                    {
                        Console.Error.WriteLine($"Capture failed: {ex.Message}");
                        exitCode = 1;
                    }
                    catch (IOException ex)
                    {
                        Console.Error.WriteLine($"File I/O error: {ex.Message}");
                        exitCode = 1;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Unexpected error: {ex.Message}");
                        exitCode = 1;
                    }
                }
            }
        }
        else if (url is null || output is null)
        {
            Console.Error.WriteLine("Error: Both --url and --output arguments are required.");
            PrintUsage();
            exitCode = 1;
        }
        else
        {
            // Support bare file paths by converting to file:// URIs.
            if (File.Exists(url))
            {
                url = new Uri(Path.GetFullPath(url)).AbsoluteUri;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "file"))
            {
                Console.Error.WriteLine($"Error: '{url}' is not a valid HTTP, HTTPS, or file URL.");
                exitCode = 1;
            }
            else
            {
                var captureOptions = new CaptureOptions
                {
                    Url = url,
                    OutputPath = output,
                    FullPage = fullPage,
                    FollowFirstLink = followFirstLink,
                    TimeoutSeconds = timeoutSeconds,
                };

                try
                {
                    var service = new CaptureService();
                    await service.CaptureAsync(captureOptions);

                    Console.WriteLine($"Capture saved to {output}");
                    exitCode = 0;
                }
                catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
                {
                    Console.Error.WriteLine($"Capture failed: {ex.Message}");
                    exitCode = 1;
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"File I/O error: {ex.Message}");
                    exitCode = 1;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
                    exitCode = 1;
                }
            }
        }

        // Disposed before the summary so the manifest, digest and summary are on disk by the time the
        // line pointing at them is printed.
        if (diagnosticSession is not null)
        {
            diagnosticSession.Dispose();
            Console.WriteLine(diagnosticSession.Describe());
        }

        EmitDiagnostics(diagHandler, diagnosticEntries);
        return exitCode;
    }

    /// <summary>
    /// Runs a page's scripts and evaluates the requested expressions against the settled page,
    /// writing a JSON report to <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// The exit code reports whether the run happened, not what the page computed: an expression
    /// that throws is a result the report records, in the same way a capture of a page whose
    /// scripts failed still produces an image. Only a page that could not be fetched, or an output
    /// that could not be written, is a failure of the run itself.
    /// </remarks>
    private static async Task<int> RunPageEvaluation(
        string pageUrl,
        string? output,
        IReadOnlyList<string> expressions,
        string? htmlOutput,
        int timeoutSeconds)
    {
        if (output is null)
        {
            Console.Error.WriteLine("Error: '--output' is required when using '--evaluate-page'.");
            PrintUsage();
            return 1;
        }

        if (expressions.Count == 0)
        {
            Console.Error.WriteLine("Error: '--evaluate-page' requires at least one '--evaluate <EXPRESSION>'.");
            PrintUsage();
            return 1;
        }

        // Support bare file paths by converting to file:// URIs, as the capture modes do.
        if (File.Exists(pageUrl))
            pageUrl = new Uri(Path.GetFullPath(pageUrl)).AbsoluteUri;

        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "file"))
        {
            Console.Error.WriteLine($"Error: '{pageUrl}' is not a valid HTTP, HTTPS, or file URL.");
            return 1;
        }

        try
        {
            var service = new CaptureService();
            await service.EvaluatePageAsync(new PageEvaluationOptions
            {
                Url = pageUrl,
                OutputPath = output,
                Expressions = expressions,
                HtmlOutputPath = htmlOutput,
                TimeoutSeconds = timeoutSeconds,
            });

            Console.WriteLine($"Evaluated {expressions.Count} expression(s) against {pageUrl}; report saved to {output}");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Page evaluation failed: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is TimeoutException or TaskCanceledException)
        {
            Console.Error.WriteLine($"Page evaluation timed out after {timeoutSeconds}s: {ex.Message}");
            return 1;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"File I/O error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Runs <c>--analyze</c>: one page into <paramref name="outputDir"/> itself, or several, each in its
    /// own child process and its own sub-directory named after the page.
    /// </summary>
    /// <remarks>
    /// Several pages never share a process, for the capture's reason and one of the analysis's own:
    /// the exception log listens to the whole process, so two analyses in one would each record the
    /// other's exceptions as their own.
    /// </remarks>
    private static async Task<int> RunAnalysis(
        IReadOnlyList<string> inputs,
        string? outputDir,
        int width,
        int height,
        int timeoutSeconds,
        bool followFirstLink,
        bool verbose,
        bool sampleStacks,
        int analysisTimeoutSeconds)
    {
        if (outputDir is null)
        {
            Console.Error.WriteLine("Error: '--analyze' requires '--output-dir <DIR>', which receives the report, the screenshots, the page's files and the logs.");
            PrintUsage();
            return 1;
        }

        var pages = new List<string>(inputs.Count);
        foreach (var input in inputs)
        {
            if (!TryResolvePageUrl(input, out var pageUrl))
            {
                Console.Error.WriteLine($"Error: '{input}' is not a valid HTTP, HTTPS, or file URL, or an existing file.");
                return 1;
            }

            pages.Add(pageUrl);
        }

        if (pages.Count == 1)
        {
            return await new Analysis.PageAnalyzer(new Analysis.PageAnalysisOptions
            {
                Url = pages[0],
                OutputDirectory = outputDir,
                Width = width,
                Height = height,
                TimeoutSeconds = timeoutSeconds,
                FollowFirstLink = followFirstLink,
                Verbose = verbose,
                SampleStacks = sampleStacks,
                Watchdog = analysisTimeoutSeconds == 0 ? null : TimeSpan.FromSeconds(analysisTimeoutSeconds),
            }).RunAsync();
        }

        // Each page gets the directory its name derives to, collisions numbered, exactly as a batch
        // capture names its files — so the directory is findable from the page without an index.
        var items = BatchRunner.DeriveOutputPaths(pages, outputDir, string.Empty);
        Directory.CreateDirectory(outputDir);
        Console.WriteLine($"Analyzing {items.Count} page(s), one process at a time…");

        // One at a time: an analysis already loads, runs and renders its page twice, and its timings
        // are part of what it reports — concurrent analyses would measure each other.
        return BatchRunner.RunInChildProcesses(items, degreeOfParallelism: 1, (item, _) =>
        {
            var arguments = new List<string>
            {
                "--analyze", item.Input,
                "--output-dir", item.OutputPath,
                "--width", width.ToString(CultureInfo.InvariantCulture),
                "--height", height.ToString(CultureInfo.InvariantCulture),
                "--timeout", timeoutSeconds.ToString(CultureInfo.InvariantCulture),
                "--analysis-timeout", analysisTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            };
            if (followFirstLink)
                arguments.Add("--follow-first-link");
            if (verbose)
                arguments.Add("--verbose");
            if (sampleStacks)
                arguments.Add("--sample-stacks");
            return arguments;
        }).ExitCode;
    }

    /// <summary>
    /// Turns what a user typed into a page URL: an absolute HTTP, HTTPS or <c>file:</c> URL as it is, and
    /// the path of an existing file — with any <c>#fragment</c> kept — as that file's URL.
    /// </summary>
    internal static bool TryResolvePageUrl(string input, out string url)
    {
        var hash = input.IndexOf('#');
        var path = hash >= 0 ? input[..hash] : input;
        var fragment = hash >= 0 ? input[hash..] : string.Empty;

        url = File.Exists(path) ? new Uri(Path.GetFullPath(path)).AbsoluteUri + fragment : input;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile);
    }

    /// <summary>
    /// Resolves the two diagnostics arguments into one destination. <c>--diagnostic-dir</c> alone
    /// puts the log inside the bundle, which is what a reader expects to find there;
    /// <c>--diagnostic-log</c> alone records the JavaScript failures and archives nothing, for when
    /// the errors are the whole question.
    /// </summary>
    internal static DiagnosticOptions ResolveDiagnosticOptions(string? directory, string? logPath) =>
        new()
        {
            Directory = directory,
            LogPath = logPath ?? (directory is null ? null : Path.Combine(directory, "javascript-errors.log")),
        };

    /// <summary>
    /// Captures several URLs concurrently, each in its own child <c>Broiler.Cli</c> process.
    /// The child invocation is an ordinary single-URL capture, so a batched capture and a
    /// hand-run one produce the same file by the same code path.
    /// </summary>
    private static int RunBatchCapture(
        IReadOnlyList<string> captureImageUrls,
        IReadOnlyList<string> urls,
        string? outputDir,
        string? outputFormat,
        int? threads,
        int width,
        int height,
        bool fullPage,
        bool followFirstLink,
        int timeoutSeconds,
        string? diagnosticDir)
    {
        if (captureImageUrls.Count > 0 && urls.Count > 0)
        {
            Console.Error.WriteLine("Error: '--capture-image' and '--url' cannot be batched together in one run.");
            return 1;
        }

        if (outputDir is null)
        {
            Console.Error.WriteLine("Error: capturing several URLs requires '--output-dir <DIR>'.");
            return 1;
        }

        bool imageMode = captureImageUrls.Count > 0;
        var inputs = imageMode ? captureImageUrls : urls;
        var extension = outputFormat ?? (imageMode ? "png" : "html");
        var items = BatchRunner.DeriveOutputPaths(inputs, outputDir, extension);
        int degree = BatchRunner.ResolveDegreeOfParallelism(threads, items.Count, Environment.ProcessorCount);

        Directory.CreateDirectory(outputDir);
        Console.WriteLine($"Capturing {items.Count} URL(s) across {degree} worker process(es)…");

        return BatchRunner.RunInChildProcesses(items, degree, (item, _) =>
        {
            var arguments = new List<string>
            {
                imageMode ? "--capture-image" : "--url",
                item.Input,
                "--output",
                item.OutputPath,
                "--timeout",
                timeoutSeconds.ToString(CultureInfo.InvariantCulture),
            };

            if (imageMode)
            {
                arguments.Add("--width");
                arguments.Add(width.ToString(CultureInfo.InvariantCulture));
                arguments.Add("--height");
                arguments.Add(height.ToString(CultureInfo.InvariantCulture));
            }

            if (fullPage)
                arguments.Add("--full-page");
            if (followFirstLink)
                arguments.Add("--follow-first-link");
            if (diagnosticDir is not null)
            {
                // Named after the output file's stem, which is derived from the input, so a bundle is
                // findable from the capture it explains without consulting an index.
                arguments.Add("--diagnostic-dir");
                arguments.Add(Path.Combine(
                    diagnosticDir,
                    Path.GetFileNameWithoutExtension(item.OutputPath)));
            }

            return arguments;
        }).ExitCode;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: Broiler.Cli --url <URL> --output <FILE> [OPTIONS]");
        Console.WriteLine("       Broiler.Cli --capture-image <URL> --output <FILE> [OPTIONS]");
        Console.WriteLine("       Broiler.Cli --evaluate-page <URL> --evaluate <EXPR> --output <FILE.json> [OPTIONS]");
        Console.WriteLine("       Broiler.Cli --analyze <URL> --output-dir <DIR> [--verbose] [OPTIONS]");
        Console.WriteLine("       Broiler.Cli --test-engines");
        Console.WriteLine("       Broiler.Cli --fuzz-layout [--count <N>] [--output <DIR>]");
        Console.WriteLine();
        Console.WriteLine("Pages load, run their scripts and settle their load window the way the browser window");
        Console.WriteLine("runs them, on the same pipeline and engine.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --url <URL>            The URL of the website to capture; saves the document its scripts");
        Console.WriteLine("                         left, as HTML, or as plain text for a .txt output");
        Console.WriteLine("  --capture-image <URL>  Capture the website as an image (PNG or JPEG)");
        Console.WriteLine("  --evaluate-page <URL>  Run the page's scripts, then evaluate --evaluate expressions");
        Console.WriteLine("                         against the settled page and write a JSON report to --output");
        Console.WriteLine("  --evaluate <EXPR>      A JavaScript expression to evaluate; repeat for several. They run");
        Console.WriteLine("                         in order on the page's own global, and pending timers and promises");
        Console.WriteLine("                         are drained between them, so an expression that starts the page's");
        Console.WriteLine("                         work has settled before the next one reads what it produced");
        Console.WriteLine("  --evaluate-html-output <FILE>  Also write the post-script DOM as HTML to FILE");
        Console.WriteLine("  --analyze <URL>        Load, run and render the page on Broiler.JS (never the VM profile) and");
        Console.WriteLine("                         write an analysis of it into --output-dir: report.html/.md/.json with");
        Console.WriteLine("                         ranked findings; screenshots with and without scripts, full page and");
        Console.WriteLine("                         with layout boxes outlined; every document, script, stylesheet, image");
        Console.WriteLine("                         and font in resources/; exceptions.log with every exception, first-");
        Console.WriteLine("                         chance ones included; JavaScript, console and pipeline logs; the");
        Console.WriteLine("                         network as network.json and network.har; the layout tree, computed");
        Console.WriteLine("                         styles and display list in layout/. Repeat it for several pages, each");
        Console.WriteLine("                         into <DIR>/<page name>. --analyse is the same flag");
        Console.WriteLine("  --verbose              With --analyze, print every request, script failure, console message");
        Console.WriteLine("                         and exception as it happens, not only each phase");
        Console.WriteLine("  --sample-stacks        With --analyze, take the process's stacks with dotnet-stack while a");
        Console.WriteLine("                         phase runs longer than 5 s, and rank the Broiler methods it was in");
        Console.WriteLine("                         (dotnet tool install -g dotnet-stack)");
        Console.WriteLine("  --analysis-timeout <SECS>  With --analyze, write what there is and stop after SECS");
        Console.WriteLine("                         (default: 300; 0 = never), exit code 3");
        Console.WriteLine("  --output <FILE>        Output file path");
        Console.WriteLine("  --output-dir <DIR>     Output directory for a batch. --url and --capture-image may each");
        Console.WriteLine("                         be repeated; with more than one input the items run concurrently");
        Console.WriteLine("                         and each result is written to <DIR>/<input name>.<format>");
        Console.WriteLine("  --output-format <EXT>  Output extension for a batch (default: png for --capture-image,");
        Console.WriteLine("                         html for --url)");
        Console.WriteLine("  --threads <N>          Concurrency for a batch or for --fuzz-layout (default: one per");
        Console.WriteLine("                         core). --threads 1 reproduces the sequential run exactly.");
        Console.WriteLine("  --seed <N>             Base seed for --fuzz-layout (default: a clock reading)");
        Console.WriteLine("  --width <PIXELS>       Image width in pixels (default: 1024, used with --capture-image)");
        Console.WriteLine("  --height <PIXELS>      Image height in pixels (default: 768, used with --capture-image)");
        Console.WriteLine("  --full-page            Capture the full page content");
        Console.WriteLine("  --follow-first-link    Follow the first link on the page before rendering");
        Console.WriteLine("  --timeout <SECS>       Navigation timeout in seconds (default: 30)");
        Console.WriteLine("  --test-engines         Run smoke tests for the embedded rendering engines");
        Console.WriteLine("  --fuzz-layout          Run layout fuzz testing with random HTML/CSS");
        Console.WriteLine("  --count <N>            Number of fuzz cases to generate (default: 1000)");
        Console.WriteLine("  --diagnostics          Emit structured JSON log output on stdout after the operation");
        Console.WriteLine("  --diagnostic-dir <DIR> Record a diagnostics bundle for the capture into <DIR>:");
        Console.WriteLine("                           javascript-errors.log  every JS failure, written as it happens");
        Console.WriteLine("                           console.log            console.log/warn/error/info, in order");
        Console.WriteLine("                           resources/             every page, script, stylesheet, fetch and");
        Console.WriteLine("                                                  sub-document, plus index.json describing them");
        Console.WriteLine("                           summary.md             distinct failures ranked, and the platform");
        Console.WriteLine("                                                  features the page asked for and did not get");
        Console.WriteLine("                           diagnostics.json       all of the above, machine-readable");
        Console.WriteLine("                         With several inputs, each capture gets its own sub-directory.");
        Console.WriteLine("  --diagnostic-log <FILE>  Write the JavaScript failure log to FILE. On its own it records");
        Console.WriteLine("                         only that log; with --diagnostic-dir it relocates the log.");
        Console.WriteLine("  --help                 Show this help message");
        Console.WriteLine();
        Console.WriteLine("Document conversion (--convert-doc) is Broiler.Documents' command line: broilerdoc convert.");
    }

    /// <summary>
    /// Runs smoke tests for all embedded rendering engines and reports results.
    /// Returns 0 if all engines pass, 1 if any engine fails.
    /// </summary>
    internal static int RunEngineTests()
    {
        var service = new EngineTestService();
        var results = service.RunAll();
        bool allPassed = true;

        foreach (var result in results)
        {
            if (result.Passed)
            {
                Console.WriteLine($"[PASS] {result.EngineName}");
            }
            else
            {
                Console.WriteLine($"[FAIL] {result.EngineName}: {result.Error}");
                allPassed = false;
            }
        }

        Console.WriteLine();
        Console.WriteLine(allPassed ? "All engine tests passed." : "Some engine tests failed.");

        return allPassed ? 0 : 1;
    }

    /// <summary>
    /// If <c>--diagnostics</c> was active, unsubscribes the handler and
    /// writes collected log entries to stdout as a JSON array.
    /// </summary>
    private static void EmitDiagnostics(
        Action<RenderLogEntry>? diagHandler,
        List<RenderLogEntry>? diagnosticEntries)
    {
        if (diagHandler is not null)
            RenderLogger.EntryLogged -= diagHandler;

        if (diagnosticEntries is null)
            return;

        RenderLogEntry[] snapshot;
        lock (diagnosticEntries) snapshot = diagnosticEntries.ToArray();

        if (snapshot.Length == 0)
            return;

        var renderBackend = CaptureArtifactMetadata.CurrentRenderBackend;
        var jsonEntries = snapshot.Select(e => new
        {
            timestamp = e.Timestamp.ToString("o"),
            category = e.Category.ToString(),
            level = e.Level.ToString(),
            context = e.Context,
            message = e.Message,
            exception = e.Exception?.ToString(),
            renderBackendId = renderBackend.Id,
            renderBackendDisplayName = renderBackend.DisplayName,
            renderBackendLabel = renderBackend.Label,
        });

        var json = JsonSerializer.Serialize(jsonEntries, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }
}

internal sealed record CaptureRenderBackendMetadata(
    string Id,
    string DisplayName,
    string Label);

internal static class CaptureArtifactMetadata
{
    internal static CaptureRenderBackendMetadata CurrentRenderBackend =>
        new(
            BGraphicsBackend.CurrentId,
            BGraphicsBackend.CurrentDisplayName,
            BGraphicsBackend.CurrentLabel);

    internal static string GetSidecarPath(string outputPath) => $"{outputPath}.metadata.json";

    internal static void WriteImageSidecar(string outputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        var renderBackend = CurrentRenderBackend;
        var metadata = new Dictionary<string, object?>
        {
            ["generatedAt"] = DateTime.UtcNow.ToString("o"),
            ["imagePath"] = Path.GetFileName(outputPath),
            ["renderBackend"] = new Dictionary<string, string>
            {
                ["id"] = renderBackend.Id,
                ["displayName"] = renderBackend.DisplayName,
                ["label"] = renderBackend.Label,
            },
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        File.WriteAllText(GetSidecarPath(outputPath), json);
    }
}
