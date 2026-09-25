using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Broiler.JavaScript.Runtime;

namespace Broiler.Cli.Analysis;

/// <summary>
/// Reads an exception's message without running the page's JavaScript.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an exception's message can run page code.</b> Broiler.JS's <see cref="JSException.Message"/>
/// is the thrown value rendered the way JavaScript renders it: for an Error it reads the error's
/// <c>message</c> property, and for any other thrown value it calls that value's <c>toString</c>. A
/// page can define either — a getter on a custom error class, a <c>toString</c> on a thrown object —
/// and reading <c>Message</c> then runs it. From an exception log that is not a read: it is page code
/// running at a point the page never scheduled it (inside the throw, before any <c>catch</c>), and
/// possibly on a thread the page's realm is not current on. A diagnostic that changes what the page
/// does is worse than none.
/// </para>
/// <para>
/// <b>What it reads instead.</b> An Error object keeps the message it was constructed with as a plain
/// .NET property (<see cref="IJSError.Message"/>), which is what an <c>Error</c>, a <c>TypeError</c> or a
/// DOM error thrown by the engine or by the page reports here. Any other thrown value — a string, an
/// object — is described by the text the engine itself rendered on the throwing thread when it built
/// the exception (<c>RawMessage</c>). That property is internal to the engine, so it is read by
/// reflection — the one place the command line reaches past an assembly's surface without a grant —
/// and it degrades to saying so rather than to running page code if the member ever moves.
/// </para>
/// <para>
/// The engine's own rendering is not used for Error objects because it is taken too early: an
/// <c>Error</c> the page constructs builds its exception before its message is assigned, and renders
/// as <c>Error: </c> with the message missing.
/// </para>
/// </remarks>
internal static class ExceptionText
{
    private const string UnreadableJavaScriptMessage =
        "(message not read: rendering it would run the page's JavaScript)";

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> RawMessageProperties = new();

    /// <summary>
    /// <paramref name="exception"/>'s message, or for a JavaScript exception the message of the value
    /// it threw, read without rendering it. Never throws and never runs JavaScript.
    /// </summary>
    public static string SafeMessage(Exception exception)
    {
        try
        {
            if (exception is JSException javaScript)
                return JavaScriptMessage(javaScript);

            return exception.Message;
        }
        catch (Exception)
        {
            return "(message unavailable)";
        }
    }

    /// <summary>
    /// The exception and its inner exceptions as <c>Type: message</c> lines, each read with
    /// <see cref="SafeMessage"/>. Aggregates list every inner exception, so a failure that was only
    /// one of several is not reported as the whole story.
    /// </summary>
    public static string Describe(Exception exception)
    {
        var builder = new StringBuilder();
        Append(builder, exception, depth: 0);
        return builder.ToString().TrimEnd();
    }

    /// <summary>Whether <paramref name="type"/> is, or derives from, the engine's JavaScript exception.</summary>
    public static bool IsJavaScriptException(Type type) => typeof(JSException).IsAssignableFrom(type);

    private static string JavaScriptMessage(JSException exception)
    {
        // Both reads are .NET property reads: the thrown value, and an Error's own message field.
        if (exception.Error is IJSError error)
            return ErrorName(error) is { } name ? $"{name}: {error.Message}" : error.Message;

        return RawMessageProperties.GetOrAdd(exception.GetType(), FindRawMessage)?.GetValue(exception) as string
            ?? UnreadableJavaScriptMessage;
    }

    /// <summary>
    /// The error's constructor name as its .NET type spells it — a <c>JSTypeError</c> is a
    /// <c>TypeError</c> — or null when the type does not say. JavaScript would read
    /// <c>constructor.name</c>, a property the page can redefine. The base <c>JSError</c> says nothing:
    /// the engine raises its own TypeErrors and ReferenceErrors as that type with the subclass's
    /// prototype, and so does a page's <c>class MyError extends Error</c>.
    /// </summary>
    private static string? ErrorName(IJSError error)
    {
        var name = error.GetType().Name;
        if (name == "JSError")
            return null;

        return name.Length > 2 && name.StartsWith("JS", StringComparison.Ordinal) ? name[2..] : name;
    }

    private static void Append(StringBuilder builder, Exception exception, int depth)
    {
        // A cycle in InnerException is not supposed to exist, and a diagnostic must not hang on one.
        if (depth > 8)
            return;

        builder.Append(' ', depth * 2)
            .Append(exception.GetType().FullName)
            .Append(": ")
            .AppendLine(SafeMessage(exception));

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                Append(builder, inner, depth + 1);
        }
        else if (exception.InnerException is { } inner)
        {
            Append(builder, inner, depth + 1);
        }
    }

    private static PropertyInfo? FindRawMessage(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(
                "RawMessage",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (property is not null && property.PropertyType == typeof(string))
                return property;
        }

        return null;
    }
}
