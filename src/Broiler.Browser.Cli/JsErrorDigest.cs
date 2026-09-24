using System.Text.RegularExpressions;

namespace Broiler.Cli;

// Disambiguate the unqualified `Regex` type: the Broiler.JS engine exposes a top-level
// `Broiler.Regex` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.Text.RegularExpressions.Regex by simple-name lookup (CS0118). This file uses only
// the .NET Regex.
using Regex = System.Text.RegularExpressions.Regex;

/// <summary>
/// A JavaScript failure that occurred <paramref name="Count"/> times, and one example of it.
/// </summary>
internal sealed record JsErrorGroup(string Signature, int Count, string Example, string? FirstContext);

/// <summary>
/// A platform feature the page asked for and did not get, named as the script named it.
/// </summary>
internal sealed record MissingApi(string Name, string Kind, int Count);

/// <summary>
/// Turns a run's JavaScript errors into the two things a reader actually wants from them: the
/// distinct failures ranked by how often they happened, and the platform features whose absence
/// caused them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why grouping is not a nicety.</b> A heavily-scripted page throws the same error from inside a
/// loop, an event handler and a retry timer; the raw log is then tens of thousands of lines
/// describing a handful of distinct defects. Grouping by a signature that ignores the varying parts
/// turns that into something readable, and the count is itself the priority order.
/// </para>
/// <para>
/// <b>Why the missing-API list exists.</b> Nearly every failure on such a page is the engine not
/// having something: <c>ReferenceError: X is not defined</c> is a missing global, <c>X is not a
/// function</c> is a missing method, and <c>Cannot read properties of undefined (reading 'y')</c> is
/// a missing property on an object we did return. Those identifiers are the work list for making the
/// page run, so they are extracted rather than left for a reader to grep out.
/// </para>
/// </remarks>
internal static partial class JsErrorDigest
{
    /// <summary>
    /// Groups <paramref name="messages"/> by what makes two of them the same defect, most frequent
    /// first. <paramref name="contexts"/> is the parallel list of logging contexts the messages came
    /// from, so a group can name where it was first seen.
    /// </summary>
    public static IReadOnlyList<JsErrorGroup> Group(
        IReadOnlyList<string> messages,
        IReadOnlyList<string>? contexts = null)
    {
        var groups = new Dictionary<string, (int Count, string Example, string? Context)>(StringComparer.Ordinal);
        var order = new List<string>();

        for (var i = 0; i < messages.Count; i++)
        {
            var signature = Signature(messages[i]);
            if (groups.TryGetValue(signature, out var existing))
            {
                groups[signature] = (existing.Count + 1, existing.Example, existing.Context);
                continue;
            }

            order.Add(signature);
            groups[signature] = (1, messages[i], contexts is not null && i < contexts.Count ? contexts[i] : null);
        }

        // OrderByDescending is a stable sort, so ties keep first-seen order and the digest of one
        // page does not shuffle between runs.
        return [.. order
            .Select(signature =>
            {
                var (count, example, context) = groups[signature];
                return new JsErrorGroup(signature, count, example, context);
            })
            .OrderByDescending(static group => group.Count)];
    }

    /// <summary>
    /// The platform features <paramref name="messages"/> blame, most frequently blamed first.
    /// </summary>
    public static IReadOnlyList<MissingApi> MissingApis(IReadOnlyList<string> messages)
    {
        var counts = new Dictionary<(string Name, string Kind), int>();
        var order = new List<(string Name, string Kind)>();

        void Add(string name, string kind)
        {
            if (!NamesSomething(name))
                return;

            var key = (name, kind);
            if (counts.TryGetValue(key, out var count))
            {
                counts[key] = count + 1;
                return;
            }

            order.Add(key);
            counts[key] = 1;
        }

        foreach (var message in messages)
        {
            foreach (Match match in NotDefinedPattern().Matches(message))
                Add(match.Groups["name"].Value, "global");

            foreach (Match match in NotCallablePattern().Matches(message))
                Add(match.Groups["name"].Value, "callable");

            foreach (Match match in MissingPropertyPattern().Matches(message))
                Add(match.Groups["name"].Value, "property");

            foreach (Match match in ReadPropertiesOfPattern().Matches(message))
                Add(match.Groups["name"].Value, "property");
        }

        return [.. order
            .Select(key => new MissingApi(key.Name, key.Kind, counts[key]))
            .OrderByDescending(static api => api.Count)];
    }

    /// <summary>
    /// What makes two messages the same defect: the message with the parts that identify <em>where</em>
    /// it happened blanked, leaving only <em>what</em> happened. A script label goes first — one
    /// missing global brought down an inline script and a deferred one, and that is one defect, not
    /// two — and then the numbers and hex blobs that vary per occurrence.
    /// </summary>
    internal static string Signature(string message) =>
        VaryingLiteralPattern().Replace(ScriptLabelPattern().Replace(message, "<script>"), "#");

    /// <summary>
    /// Whether a captured identifier names something that could be implemented. A message that could
    /// not name its subject says <c>undefined is not a function</c>, and listing <c>undefined</c> as a
    /// feature to implement is noise that crowds out the rows that are not — so those are dropped
    /// here rather than left for a reader to skip past.
    /// </summary>
    private static bool NamesSomething(string name) =>
        name.Length > 0 &&
        name is not ("undefined" or "null" or "NaN" or "this" or "target") &&
        !name.All(static c => char.IsDigit(c) || c == '.');

    /// <summary>Numbers and hex blobs vary per occurrence and must not split a group.</summary>
    [GeneratedRegex(@"0x[0-9a-fA-F]+|\b\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex VaryingLiteralPattern();

    /// <summary>
    /// The script labels the capture host mints — <c>inline-3</c>, <c>deferred-0</c>,
    /// <c>module-2</c>. They say which script, which is not part of what went wrong.
    /// The trailing index is required so this cannot swallow <c>inline-block</c> and its kin out of
    /// a message that happens to quote CSS.
    /// </summary>
    [GeneratedRegex(@"\b(?:inline|deferred|module)-\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptLabelPattern();

    /// <summary><c>ReferenceError: foo is not defined</c> — a global the engine does not have.</summary>
    [GeneratedRegex(@"(?<name>[\w$.]+) is not defined", RegexOptions.CultureInvariant)]
    private static partial Regex NotDefinedPattern();

    /// <summary><c>a.b.c is not a function</c> — a method or constructor the engine does not have.</summary>
    [GeneratedRegex(@"(?<name>[\w$.\[\]]+) is not a (?:function|constructor)", RegexOptions.CultureInvariant)]
    private static partial Regex NotCallablePattern();

    /// <summary>
    /// <c>Cannot get property foo of undefined</c> and the <c>set</c>/<c>null</c> spellings — the
    /// wording Broiler's own <c>JSUndefined</c>/<c>JSNull</c> raise, and so the one that dominates a
    /// real log. A property missing from an object the engine did produce.
    /// </summary>
    [GeneratedRegex(
        @"Cannot (?:get|set) property (?<name>[^\s]+) of (?:undefined|null)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MissingPropertyPattern();

    /// <summary>
    /// <c>Cannot read properties of undefined (reading 'foo')</c> — the spelling
    /// <c>JSValue.GetValue</c> raises, and the one other engines use, so a log can hold both.
    /// </summary>
    [GeneratedRegex(
        @"Cannot read propert(?:y|ies) of \w+ \(reading '(?<name>[^']+)'\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReadPropertiesOfPattern();
}
