using System.Runtime.CompilerServices;
using System.Text.Json;
using Broiler.Cli.Analysis;
using Broiler.JavaScript.Runtime;

namespace Broiler.Cli.Tests;

/// <summary>
/// The exception log: every exception raised while it listens is on disk before the throw goes on —
/// the caught, first-chance ones above all, because a caught exception is how a fallback hides.
/// </summary>
/// <remarks>
/// The recorder listens to the whole process, so these tests share the diagnostics collection, which
/// runs nothing else at the same time, and each assertion looks for its own marker.
/// </remarks>
[Collection("DiagnosticSession")]
public sealed class ExceptionRecorderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "broiler-exceptions-" + Guid.NewGuid().ToString("N"));

    public ExceptionRecorderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string LogPath => Path.Combine(_directory, "exceptions.log");

    private string JsonPath => Path.Combine(_directory, "exceptions.json");

    [Fact(Timeout = 600000)]
    public void A_Caught_Exception_Is_On_Disk_Before_The_Recorder_Is_Disposed()
    {
        using var recorder = ExceptionRecorder.Start(LogPath, JsonPath, static () => "marker-phase");

        ThrowAndCatch(new InvalidOperationException("caught-marker-1"));

        var log = ReadShared(LogPath);
        Assert.Contains("first-chance [marker-phase]", log, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: caught-marker-1", log, StringComparison.Ordinal);

        // The stack at the throw, with the throwing method on it: a first-chance notification comes
        // before the unwind, which is what makes it more than the exception's own trace.
        Assert.Contains(nameof(ThrowAndCatch), log, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Repeats_Are_Counted_By_Signature_And_Summarised()
    {
        using (var recorder = ExceptionRecorder.Start(LogPath, JsonPath, static () => "repeat-phase"))
        {
            for (var i = 0; i < ExceptionRecorder.StacksPerSignature + 3; i++)
                ThrowAndCatch(new FormatException("repeat-marker"));

            var signature = Assert.Single(recorder.Signatures(), static s => s.FirstMessage == "repeat-marker");
            Assert.Equal(ExceptionRecorder.StacksPerSignature + 3, signature.Count);
            Assert.Equal("first-chance", signature.Kind);
            Assert.Equal(["repeat-phase"], signature.Phases);
            Assert.NotNull(signature.FirstStack);
        }

        using var json = JsonDocument.Parse(File.ReadAllText(JsonPath));
        var row = json.RootElement.GetProperty("signatures").EnumerateArray()
            .Single(static s => s.GetProperty("message").GetString() == "repeat-marker");
        Assert.Equal(ExceptionRecorder.StacksPerSignature + 3, row.GetProperty("count").GetInt32());
    }

    [Fact(Timeout = 600000)]
    public void Nothing_Is_Recorded_After_Dispose()
    {
        var recorder = ExceptionRecorder.Start(LogPath, JsonPath, static () => "disposed-phase");
        recorder.Dispose();

        ThrowAndCatch(new InvalidOperationException("after-dispose-marker"));

        Assert.DoesNotContain("after-dispose-marker", File.ReadAllText(LogPath), StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Last_Exception_On_A_Thread_Is_Remembered_For_A_Later_Report()
    {
        using var recorder = ExceptionRecorder.Start(LogPath, JsonPath, static () => "cause-phase");

        ThrowAndCatch(new IOException("cause-marker"));

        var last = ExceptionRecorder.LastOnCurrentThread;
        Assert.NotNull(last);
        Assert.Equal("cause-marker", last.Message);
        Assert.Equal("System.IO.IOException", last.Type);
    }

    /// <summary>
    /// Reading a JavaScript exception's message renders the thrown value, which can run the page's own
    /// code; the message the Error object was constructed with is read instead.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_JavaScript_Exceptions_Message_Is_Read_Without_Rendering_It()
    {
        using var context = new Broiler.JavaScript.Engine.JSContext();
        var exception = new JSException("js-marker-message");

        Assert.True(ExceptionText.IsJavaScriptException(exception.GetType()));
        Assert.Equal("js-marker-message", ExceptionText.SafeMessage(exception));
        Assert.False(ExceptionText.IsJavaScriptException(typeof(InvalidOperationException)));
    }

    [Fact(Timeout = 600000)]
    public void Inner_And_Aggregated_Exceptions_Are_Described()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("first-inner"),
            new IOException("outer", new FormatException("nested-inner")));

        var text = ExceptionText.Describe(aggregate);

        Assert.Contains("System.InvalidOperationException: first-inner", text, StringComparison.Ordinal);
        Assert.Contains("System.FormatException: nested-inner", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// .NET raises a first-chance notification for every throw of an exception object, <c>throw;</c>
    /// included, so one failure rethrown on its way out used to be counted once per frame it crossed.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Rethrown_Exception_Is_Recorded_Once_And_Its_Rethrows_Counted()
    {
        using (var recorder = ExceptionRecorder.Start(LogPath, JsonPath, static () => "rethrow-phase"))
        {
            try
            {
                try
                {
                    throw new InvalidOperationException("rethrow-marker");
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
            }
            catch (InvalidOperationException)
            {
                // Caught on purpose, after one rethrow.
            }

            Assert.Equal(1, Assert.Single(recorder.Signatures(), static s => s.FirstMessage == "rethrow-marker").Count);
            Assert.True(recorder.Rethrows >= 1);
        }

        using var json = JsonDocument.Parse(File.ReadAllText(JsonPath));
        Assert.True(json.RootElement.GetProperty("rethrows").GetInt64() >= 1);
    }

    /// <summary>
    /// A script engine throws "Maximum call stack size exceeded" when the stack is nearly gone, and the
    /// handler runs on top of that throw; taking a stack with file information there could overflow it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Exception_Thrown_With_The_Stack_Nearly_Exhausted_Is_Counted_And_Not_Recorded()
    {
        using var recorder = ExceptionRecorder.Start(LogPath, JsonPath, static () => "deep-phase");

        var thread = new Thread(static () => ThrowAtTheBottomOfTheStack(0), maxStackSize: 1024 * 1024);
        thread.Start();
        thread.Join();

        Assert.True(recorder.WithoutStackRoom >= 1);
        Assert.DoesNotContain(recorder.Signatures(), static s => s.FirstMessage == "deep-marker");
    }

    /// <summary>
    /// <see cref="AggregateException.Message"/> appends each inner exception's own message, and a
    /// JavaScript exception's renders the page's thrown value; the aggregate's is put together from
    /// the inner exceptions' safe messages instead.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Aggregates_Message_Is_Put_Together_From_Its_Inner_Exceptions_Safe_Messages()
    {
        using var context = new Broiler.JavaScript.Engine.JSContext();
        var aggregate = new AggregateException(new JSException("js-inner-marker"), new FormatException("format-marker"));

        Assert.Equal(
            "2 error(s) occurred: JSException: js-inner-marker | FormatException: format-marker",
            ExceptionText.SafeMessage(aggregate));
    }

    // Recurses until the runtime says the stack is nearly gone, and throws there. The addition after
    // the call keeps it from becoming a loop.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ThrowAtTheBottomOfTheStack(int depth)
    {
        if (RuntimeHelpers.TryEnsureSufficientExecutionStack())
            return ThrowAtTheBottomOfTheStack(depth + 1) + 1;

        try
        {
            throw new InvalidOperationException("deep-marker");
        }
        catch (InvalidOperationException)
        {
            return depth;
        }
    }

    private static void ThrowAndCatch(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception)
        {
            // Caught on purpose: this is the first-chance case.
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
