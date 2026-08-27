// Broiler.Octane.JintHost — the JavaScript shell the Octane harness runs its
// `jint` engine in, backed by Jint (https://github.com/sebastienros/jint), a
// managed ECMAScript interpreter.
//
//     dotnet Broiler.Octane.JintHost.dll <script.js> [--stack-mb N] [--max-frames N]
//
// scripts/run-octane.mjs builds one combined script per Octane suite (base.js +
// the benchmark files + scripts/octane-runner.js) and runs it in a fresh process
// of this shell, exactly as it does for `BroilerJS --script-host`.
//
// Why Jint is here: Chromium answers "how far is Broiler from V8", which is a
// number about a JIT-compiling C++ engine. Jint answers the question a Broiler
// change is actually steered by — how Broiler compares to another managed engine
// on the same CLR, running the same script in the same process shape.
//
// The shell surface is deliberately the one BroilerJS's --script-host mode
// defines and nothing more: `print`, `read`, and no `window`. A suite that fails
// for want of a host function then fails the same way under both engines, and
// the comparison stays a statement about the engine rather than about which
// shell was more generous.

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Broiler.Octane.JintHost;

internal static class Program
{
    // Same budget the BroilerJS shell gives JavaScript (Broiler.JavaScript/Program.cs,
    // ScriptHostStackBytes), rather than whatever the runtime handed Main — 1 MiB
    // on Windows, 8 MiB on a typical Linux. Two managed engines measured against
    // each other should get the same stack.
    private const int DefaultStackBytes = 16 * 1024 * 1024;

    // Arms Jint's call-depth guard (Options.Constraints.MaxExecutionStackCount).
    // What that buys is the failure *mode*. Disarmed — the Jint default, a
    // negative count — a runaway recursion exhausts the CLR stack, and a stack
    // overflow is not catchable: the process aborts and the suite reports no
    // result at all. Armed, the same recursion raises `RangeError: Maximum call
    // stack size exceeded`, which the benchmark can catch and Octane can report,
    // exactly as it would in a browser. That mirrors the BroilerJS shell, which
    // reaches the same outcome through its 4 MiB stack reserve.
    //
    // The depth actually reached comes from the stack budget above rather than
    // from this number: measured on .NET 10/linux-x64, a 16 MiB stack trips the
    // guard between 9,000 and 10,000 frames of plain recursion, and a 64 MiB one
    // between 30,000 and 60,000. So this is an upper bound that the stack reaches
    // first — set to the frame count 16 MiB affords, which is the same order as
    // V8's own limit (~13,955) and as the ~10,000 frames the BroilerJS shell
    // budgets for.
    private const int DefaultMaxFrames = 10_000;

    private static int Main(string[] args)
    {
        string? scriptPath = null;
        var stackBytes = DefaultStackBytes;
        var maxFrames = DefaultMaxFrames;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stack-mb":
                    if (!TryTakeInt(args, ref i, out var mb) || mb <= 0) return Usage("--stack-mb expects a positive integer");
                    stackBytes = mb * 1024 * 1024;
                    break;
                case "--max-frames":
                    if (!TryTakeInt(args, ref i, out maxFrames)) return Usage("--max-frames expects an integer (negative disarms the guard)");
                    break;
                case "-h":
                case "--help":
                    return Usage(null);
                default:
                    if (args[i].StartsWith('-')) return Usage($"Unknown option: {args[i]}");
                    if (scriptPath != null) return Usage("Only one script may be given");
                    scriptPath = args[i];
                    break;
            }
        }

        if (scriptPath == null) return Usage("A script path is required");
        var script = new FileInfo(scriptPath);
        if (!script.Exists) return Usage($"Script not found: {script.FullName}");

        // Read back by scripts/run-octane.mjs, which labels the engine in the
        // comparison with what it says. The version that ran is a property of the
        // run, not something the harness should be hard-coding on its side.
        var version = JintVersion();
        Console.WriteLine("OCTANE_ENGINE " + new JsonObject
        {
            ["engine"] = "jint",
            ["label"] = $"Jint {version} (interpreter)",
            ["version"] = version,
            ["maxFrames"] = maxFrames,
            ["stackBytes"] = stackBytes,
        }.ToJsonString());

        return RunOnOwnThread(script, stackBytes, maxFrames);
    }

    private static bool TryTakeInt(string[] args, ref int i, out int value)
    {
        value = 0;
        return i + 1 < args.Length && int.TryParse(args[++i], out value);
    }

    private static int Usage(string? error)
    {
        var to = error == null ? Console.Out : Console.Error;
        if (error != null) to.WriteLine($"error: {error}");
        to.WriteLine("""
            usage: Broiler.Octane.JintHost <script.js> [--stack-mb N] [--max-frames N]

              --stack-mb N     Stack for the thread JavaScript runs on (default: 16). This
                               is what decides how deep recursion can go — roughly 9,000
                               frames at the default.
              --max-frames N   Arm Jint's call-depth guard at N frames, so exhausting the
                               stack raises `RangeError: Maximum call stack size exceeded`
                               instead of aborting the process (default: 10000). A negative
                               count disarms it, which is Jint's own default.
            """);
        return error == null ? 0 : 2;
    }

    // JavaScript runs on a thread this shell sizes itself; an escaping exception is
    // rethrown on the main thread so an uncaught JavaScript error still reaches the
    // runtime's unhandled-exception path with its original stack — which is what
    // prints the `Unhandled exception.` block (engine frames, and the JavaScript
    // frames Jint attaches as an inner exception) that the harness reads back when
    // a suite dies without reporting a result.
    private static int RunOnOwnThread(FileInfo script, int stackBytes, int maxFrames)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    Run(script, maxFrames);
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            },
            stackBytes);

        thread.Start();
        thread.Join();
        failure?.Throw();
        return 0;
    }

    private static void Run(FileInfo script, int maxFrames)
    {
        var engine = new Engine(options =>
        {
            // Everything else stays at Jint's defaults — no statement budget, no
            // execution timeout, no memory cap. The harness enforces its own
            // per-suite timeout by killing this process, and a constraint that
            // fired mid-benchmark would be measuring the constraint.
            options.Constraints.MaxExecutionStackCount = maxFrames;
        });

        DefineShellGlobals(engine);

        // The path is what Jint cites in stack traces. Passing the combined
        // script's real path is what lets the harness rewrite those citations back
        // to the Octane file each line came from (base.js:371, crypto.js:104, …).
        engine.Execute(File.ReadAllText(script.FullName), script.FullName);
    }

    // Host functions provided by JavaScript shells (V8's d8, SpiderMonkey's js)
    // that are not part of ECMAScript. Kept in step with the BroilerJS shell's
    // DefineScriptHostGlobals, down to the argument handling.
    private static void DefineShellGlobals(Engine engine)
    {
        // `print(...values)` writes the space-joined string form of its arguments,
        // followed by a newline, to standard output and returns undefined. The
        // Octane runner reports every result and diagnostic through it.
        engine.SetValue("print", new ClrFunction(
            engine,
            "print",
            (_, args) =>
            {
                var parts = new string[args.Length];
                for (var i = 0; i < args.Length; i++)
                    parts[i] = TypeConverter.ToString(args[i]);

                Console.WriteLine(string.Join(" ", parts));
                return JsValue.Undefined;
            },
            length: 1));

        // `read(path)` returns the file's contents as a string; `read(path, "binary")`
        // returns them as a Uint8Array. Emscripten's shell preamble references it
        // unconditionally — `Module.read = read` — which is how Octane's zlib
        // benchmark loads outside a browser: without the binding that bare
        // reference is a ReferenceError before the benchmark runs a line, even
        // though it never reads a file (its corpus is embedded in zlib-data.js).
        engine.SetValue("read", new ClrFunction(
            engine,
            "read",
            (_, args) =>
            {
                if (args.Length == 0 || args[0].IsUndefined())
                    throw new JavaScriptException(engine.Intrinsics.TypeError, "read requires a file path");

                var path = TypeConverter.ToString(args[0]);
                var binary = args.Length > 1
                    && !args[1].IsUndefined()
                    && string.Equals(TypeConverter.ToString(args[1]), "binary", StringComparison.Ordinal);

                try
                {
                    return binary
                        ? engine.Intrinsics.Uint8Array.Construct(File.ReadAllBytes(path))
                        : new JsString(File.ReadAllText(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new JavaScriptException(engine.Intrinsics.Error, $"Cannot read file {path}: {ex.Message}");
                }
            },
            length: 1));
    }

    // "4.15.3", not the "4.15.3+<commit>" the informational version carries: the
    // engine label is read by people comparing runs, and the commit hash is noise
    // in a table cell.
    private static string JintVersion()
    {
        var assembly = typeof(Engine).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
            return assembly.GetName().Version?.ToString() ?? "unknown";

        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
