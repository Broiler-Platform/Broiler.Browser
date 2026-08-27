using System.Text;
using Broiler.Dom;

namespace Broiler.Browser;

/// <summary>
/// Builds a form's submission target from the page's DOM: the HTML "form data set"
/// of its successful controls, encoded as <c>application/x-www-form-urlencoded</c>
/// and applied to the form's action.
/// </summary>
/// <remarks>
/// <para>
/// The renderer resolves a submit control to its enclosing <c>&lt;form&gt;</c>'s
/// <c>action</c> and navigates there verbatim — no field values are collected — so a
/// search form went to <c>/search</c> rather than <c>/search?q=…</c>, and typing into
/// a page could not actually submit anything.
/// </para>
/// <para>
/// This is deliberately pure and DOM-only: it takes a parsed document, so it can be
/// tested directly and does not depend on layout, hit testing or the renderer's
/// internals. Values the user typed are supplied by the caller through a lookup,
/// since the page's markup still carries the values it was loaded with.
/// </para>
/// </remarks>
internal static class HtmlFormSerializer
{
    /// <summary>Input types whose value is submitted as-is when the control has a name.</summary>
    private static readonly HashSet<string> PlainValueInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "search", "url", "tel", "email", "password", "number", "range", "color",
        "date", "month", "week", "time", "datetime-local", "hidden",
    };

    /// <summary>Input types that are buttons: submitted only when they are the submitter.</summary>
    private static readonly HashSet<string> ButtonInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "submit", "image",
    };

    /// <summary>
    /// The <c>&lt;form&gt;</c> ancestor of <paramref name="control"/>, or <c>null</c>
    /// when it is not in one.
    /// </summary>
    public static DomElement? FindEnclosingForm(DomElement? control)
    {
        for (DomNode? node = control?.ParentNode; node is not null; node = node.ParentNode)
        {
            if (node is DomElement element && IsTag(element, "form"))
                return element;
        }

        return null;
    }

    /// <summary>
    /// Finds the form control in <paramref name="root"/> that the given attributes
    /// describe — how a clicked submit button, reported by the renderer as a bare
    /// attribute bag, is located in the parsed document. Matches on <c>id</c> first,
    /// then on the tag/type/name/value combination.
    /// </summary>
    public static DomElement? FindControl(DomNode? root, IReadOnlyDictionary<string, string>? attributes)
    {
        if (root is null || attributes is null)
            return null;

        if (TryGet(attributes, "id") is { Length: > 0 } id)
        {
            DomElement? byId = FindElement(root, e => string.Equals(e.GetAttribute("id"), id, StringComparison.Ordinal));
            if (byId is not null)
                return byId;
        }

        string? name = TryGet(attributes, "name");
        string? type = TryGet(attributes, "type");
        string? value = TryGet(attributes, "value");

        return FindElement(root, e =>
            (IsTag(e, "input") || IsTag(e, "button")) &&
            Matches(e.GetAttribute("name"), name) &&
            Matches(e.GetAttribute("type"), type) &&
            Matches(e.GetAttribute("value"), value));

        static bool Matches(string? actual, string? expected) =>
            expected is null || string.Equals(actual ?? string.Empty, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the <c>application/x-www-form-urlencoded</c> body of
    /// <paramref name="form"/>'s form data set.
    /// </summary>
    /// <param name="submitter">
    /// The control that triggered submission, if any. Only the submitter contributes
    /// its own name/value, which is how <c>&lt;input type="submit" name="…"&gt;</c>
    /// tells the server which button was pressed.
    /// </param>
    /// <param name="valueOverride">
    /// Returns the live value of a control when the user has edited it, or <c>null</c>
    /// to use the markup's value. Checkbox and radio state is expressed by returning
    /// <see cref="CheckedOverride"/> / <see cref="UncheckedOverride"/>.
    /// </param>
    public static string BuildFormData(
        DomElement form,
        DomElement? submitter = null,
        Func<DomElement, string?>? valueOverride = null) =>
        EncodeUrlEncoded(BuildEntryList(form, submitter, valueOverride));

    /// <summary>
    /// Supplies the files chosen for an <c>&lt;input type="file"&gt;</c> — several only
    /// when it is a <c>multiple</c> control. Empty when nothing is chosen.
    /// </summary>
    public delegate IReadOnlyList<SelectedFile> FileProvider(DomElement control);

    /// <summary>
    /// Supplies the option values a <c>&lt;select&gt;</c> now has selected, or
    /// <c>null</c> to fall back to the ones the markup marks. Returning an empty list
    /// means the user deselected everything, which a <c>multiple</c> select allows.
    /// </summary>
    public delegate IReadOnlyList<string>? SelectProvider(DomElement select);

    /// <summary>
    /// The form data set: every successful control's name and value, in tree order.
    /// Every encoding is built from this, so they cannot disagree about which
    /// controls submit.
    /// </summary>
    public static IReadOnlyList<FormEntry> BuildEntryList(
        DomElement form,
        DomElement? submitter = null,
        Func<DomElement, string?>? valueOverride = null,
        FileProvider? fileProvider = null,
        SelectProvider? selectProvider = null)
    {
        ArgumentNullException.ThrowIfNull(form);

        List<FormEntry> entries = [];
        foreach (DomElement control in Descendants(form))
            AppendControl(entries, control, submitter, valueOverride, fileProvider, selectProvider);

        return entries;
    }

    /// <summary>A single name/value pair of a form data set.</summary>
    /// <param name="FileName">
    /// Set for a file control, naming the selected file (empty when none is). Only
    /// multipart carries the bytes; the other encodings submit the filename alone,
    /// which is what the HTML spec's plain and URL encodings do.
    /// </param>
    /// <param name="FileContent">
    /// The selected file's bytes, for a multipart part. Null for a text entry and for
    /// a file control with nothing selected.
    /// </param>
    public sealed record FormEntry(
        string Name,
        string Value,
        string? FileName = null,
        byte[]? FileContent = null);

    /// <summary>
    /// The encoding the form asks for. An unrecognised <c>enctype</c> falls back to
    /// URL encoding, which is what the HTML spec's missing-value default is.
    /// </summary>
    public static string ResolveEncoding(DomElement form)
    {
        string? declared = form.GetAttribute("enctype")?.Trim();
        if (string.Equals(declared, MultipartFormData, StringComparison.OrdinalIgnoreCase))
            return MultipartFormData;
        if (string.Equals(declared, TextPlain, StringComparison.OrdinalIgnoreCase))
            return TextPlain;

        return UrlEncoded;
    }

    public const string UrlEncoded = "application/x-www-form-urlencoded";

    public const string MultipartFormData = "multipart/form-data";

    public const string TextPlain = "text/plain";

    /// <summary>Encodes an entry list as <c>application/x-www-form-urlencoded</c>.</summary>
    public static string EncodeUrlEncoded(IReadOnlyList<FormEntry> entries)
    {
        StringBuilder body = new();
        foreach (FormEntry entry in entries)
        {
            if (body.Length > 0)
                body.Append('&');

            // A file control submits its filename, since the bytes need multipart.
            body.Append(Encode(entry.Name)).Append('=').Append(Encode(entry.FileName ?? entry.Value));
        }

        return body.ToString();
    }

    /// <summary>Encodes an entry list as <c>text/plain</c> (HTML forms §plain text encoding).</summary>
    public static string EncodeTextPlain(IReadOnlyList<FormEntry> entries)
    {
        StringBuilder body = new();
        foreach (FormEntry entry in entries)
            body.Append(entry.Name).Append('=').Append(entry.FileName ?? entry.Value).Append("\r\n");

        return body.ToString();
    }

    /// <summary>
    /// Encodes an entry list as <c>multipart/form-data</c> with the given boundary.
    /// A file entry carries its filename and an <c>application/octet-stream</c>
    /// content type, so a server sees a file part rather than a text field.
    /// </summary>
    /// <remarks>
    /// Returns bytes rather than a string because a file part is arbitrary binary:
    /// round-tripping it through a UTF-8 string would corrupt anything that is not
    /// valid UTF-8. Headers are ASCII, the body is copied verbatim.
    /// </remarks>
    public static byte[] EncodeMultipart(IReadOnlyList<FormEntry> entries, string boundary)
    {
        ArgumentException.ThrowIfNullOrEmpty(boundary);

        using MemoryStream body = new();
        foreach (FormEntry entry in entries)
        {
            StringBuilder header = new();
            header.Append("--").Append(boundary).Append("\r\n");
            header.Append("Content-Disposition: form-data; name=\"").Append(EscapeQuotes(entry.Name)).Append('"');

            if (entry.FileName is not null)
            {
                header.Append("; filename=\"").Append(EscapeQuotes(entry.FileName)).Append('"');
                header.Append("\r\nContent-Type: application/octet-stream");
            }

            header.Append("\r\n\r\n");
            Write(body, header.ToString());

            if (entry.FileContent is { } content)
                body.Write(content, 0, content.Length);
            else
                Write(body, entry.Value);

            Write(body, "\r\n");
        }

        Write(body, "--" + boundary + "--\r\n");
        return body.ToArray();

        static void Write(Stream target, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            target.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>A boundary that cannot occur in the parts it separates.</summary>
    public static string CreateMultipartBoundary() => "----BroilerFormBoundary" + Guid.NewGuid().ToString("N");

    private static string EscapeQuotes(string text) => text.Replace("\"", "%22", StringComparison.Ordinal);

    /// <summary>Sentinel a <paramref name="valueOverride"/> returns to mark a checkbox/radio checked.</summary>
    public const string CheckedOverride = "checked";

    /// <summary>Sentinel a <paramref name="valueOverride"/> returns to mark a checkbox/radio unchecked.</summary>
    public const string UncheckedOverride = "unchecked";

    /// <summary>
    /// Applies <paramref name="formData"/> to <paramref name="action"/> as a query
    /// string, replacing any query the action already carries and dropping its
    /// fragment — the navigation a <c>method="get"</c> submission performs.
    /// </summary>
    public static string ApplyQuery(string action, string formData)
    {
        action ??= string.Empty;

        int fragment = action.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
            action = action[..fragment];

        int query = action.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
            action = action[..query];

        return formData.Length == 0 ? action : action + "?" + formData;
    }

    /// <summary>
    /// Whether the form submits with <c>method="get"</c> — the only method the
    /// browser's navigation path can carry, since it fetches by URL.
    /// </summary>
    public static bool IsGetSubmission(DomElement form) =>
        !string.Equals(form.GetAttribute("method"), "post", StringComparison.OrdinalIgnoreCase);

    private static void AppendControl(
        List<FormEntry> body,
        DomElement control,
        DomElement? submitter,
        Func<DomElement, string?>? valueOverride,
        FileProvider? fileProvider,
        SelectProvider? selectProvider)
    {
        // A disabled control is never successful.
        if (control.HasAttribute("disabled"))
            return;

        string name = control.GetAttribute("name") ?? string.Empty;
        string? live = valueOverride?.Invoke(control);

        if (IsTag(control, "textarea"))
        {
            if (name.Length > 0)
                Append(body, name, live ?? control.TextContent ?? string.Empty);
            return;
        }

        if (IsTag(control, "select"))
        {
            if (name.Length > 0)
                AppendSelect(body, control, name, selectProvider?.Invoke(control));
            return;
        }

        if (IsTag(control, "button"))
        {
            // Only a submit button that is the submitter is successful.
            string buttonType = control.GetAttribute("type") ?? "submit";
            if (ReferenceEquals(control, submitter) &&
                name.Length > 0 &&
                string.Equals(buttonType, "submit", StringComparison.OrdinalIgnoreCase))
            {
                Append(body, name, live ?? control.GetAttribute("value") ?? string.Empty);
            }

            return;
        }

        if (!IsTag(control, "input"))
            return;

        string type = control.GetAttribute("type") ?? "text";

        if (string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "radio", StringComparison.OrdinalIgnoreCase))
        {
            bool isChecked = live switch
            {
                CheckedOverride => true,
                UncheckedOverride => false,
                _ => control.HasAttribute("checked"),
            };

            // An unchecked checkbox or radio contributes nothing at all.
            if (isChecked && name.Length > 0)
                Append(body, name, control.GetAttribute("value") ?? "on");

            return;
        }

        if (ButtonInputTypes.Contains(type))
        {
            if (ReferenceEquals(control, submitter) && name.Length > 0)
                Append(body, name, live ?? control.GetAttribute("value") ?? string.Empty);
            return;
        }

        if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Length == 0)
                return;

            IReadOnlyList<SelectedFile> files = fileProvider?.Invoke(control) ?? [];

            // HTML Forms: a file control with nothing selected still submits an entry,
            // with an empty filename and an empty body. A `multiple` control submits
            // one entry per file, all under the same name.
            if (files.Count == 0)
            {
                AppendFile(body, name, string.Empty, null);
                return;
            }

            // Without `multiple` the control has one file however many were offered.
            int count = control.HasAttribute("multiple") ? files.Count : 1;
            for (int index = 0; index < count; index++)
                AppendFile(body, name, files[index].FileName, files[index].Content);

            return;
        }

        // reset and button never submit.
        if (!PlainValueInputTypes.Contains(type))
            return;

        if (name.Length > 0)
            Append(body, name, live ?? control.GetAttribute("value") ?? string.Empty);
    }

    private static void AppendSelect(
        List<FormEntry> body,
        DomElement select,
        string name,
        IReadOnlyList<string>? live)
    {
        if (live is not null)
        {
            // A hosted select is authoritative, including when nothing is selected —
            // a `multiple` list the user emptied submits no entry at all.
            foreach (string value in live)
                Append(body, name, value);

            return;
        }

        bool multiple = select.HasAttribute("multiple");
        DomElement? first = null;
        bool appended = false;

        foreach (DomElement option in Descendants(select))
        {
            if (!IsTag(option, "option") || option.HasAttribute("disabled"))
                continue;

            first ??= option;
            if (!option.HasAttribute("selected"))
                continue;

            Append(body, name, OptionValue(option));
            appended = true;
            if (!multiple)
                return;
        }

        // A single-select with nothing marked selected submits its first option.
        if (!appended && !multiple && first is not null)
            Append(body, name, OptionValue(first));
    }

    private static string OptionValue(DomElement option) =>
        option.GetAttribute("value") ?? (option.TextContent ?? string.Empty).Trim();

    private static void Append(List<FormEntry> body, string name, string value) =>
        body.Add(new FormEntry(name, value));

    private static void AppendFile(List<FormEntry> body, string name, string fileName, byte[]? content) =>
        body.Add(new FormEntry(name, string.Empty, fileName, content));

    /// <summary>A file chosen for an <c>&lt;input type="file"&gt;</c>.</summary>
    public sealed record SelectedFile(string FileName, byte[] Content);

    /// <summary>
    /// Percent-encodes for <c>application/x-www-form-urlencoded</c>, where a space is
    /// <c>+</c> rather than <c>%20</c>.
    /// </summary>
    private static string Encode(string text) =>
        Uri.EscapeDataString(text).Replace("%20", "+", StringComparison.Ordinal);

    private static IEnumerable<DomElement> Descendants(DomNode root)
    {
        foreach (DomNode child in root.ChildNodes)
        {
            if (child is DomElement element)
                yield return element;

            foreach (DomElement nested in Descendants(child))
                yield return nested;
        }
    }

    private static DomElement? FindElement(DomNode root, Func<DomElement, bool> predicate)
    {
        foreach (DomElement element in Descendants(root))
        {
            if (predicate(element))
                return element;
        }

        return null;
    }

    private static bool IsTag(DomElement element, string name) =>
        string.Equals(element.TagName, name, StringComparison.OrdinalIgnoreCase);

    private static string? TryGet(IReadOnlyDictionary<string, string> attributes, string key)
    {
        foreach (KeyValuePair<string, string> pair in attributes)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }
}
