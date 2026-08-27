using Broiler.App.Rendering;
using Broiler.Dom;
using Broiler.Dom.Html;

namespace Broiler.Browser;

/// <summary>
/// The browser's view of the page's forms: it parses the live document when a
/// submission needs serializing, and holds the control state the renderer's DOM
/// does not carry (checkbox and radio checked state).
/// </summary>
/// <remarks>
/// The renderer's serialized HTML is the source of truth for values — text the user
/// typed is committed into it before a submission, and hidden inputs, <c>select</c>
/// options and their <c>selected</c> flags all survive serialization. Checked state
/// is the exception: nothing writes it back into the document, so hosted checkboxes
/// and radios report theirs here and it is layered over the markup at submit time.
/// </remarks>
internal sealed class HtmlFormState
{
    /// <summary>Checked state by control key, for controls the user has toggled.</summary>
    private readonly Dictionary<string, bool> _checkedState = new(StringComparer.Ordinal);

    /// <summary>Selected option values by control key, for selects the user has changed.</summary>
    private readonly Dictionary<string, List<string>> _selectedValues = new(StringComparer.Ordinal);

    /// <summary>Chosen file paths by control key, for file inputs the user has picked for.</summary>
    private readonly Dictionary<string, List<string>> _selectedFiles = new(StringComparer.Ordinal);

    /// <summary>Forgets per-page state. Called when the page is replaced.</summary>
    public void Reset()
    {
        _checkedState.Clear();
        _selectedValues.Clear();
        _selectedFiles.Clear();
    }

    /// <summary>Records the state of a checkbox or radio the user toggled.</summary>
    public void SetChecked(string id, string name, string value, bool isChecked) =>
        _checkedState[ControlKey(id, name, value)] = isChecked;

    /// <summary>The recorded state of a checkbox or radio, or <c>null</c> if untouched.</summary>
    public bool? GetChecked(string id, string name, string value) =>
        _checkedState.TryGetValue(ControlKey(id, name, value), out bool state) ? state : null;

    /// <summary>Records the option a hosted single-choice <c>&lt;select&gt;</c> now has selected.</summary>
    public void SetSelectedValue(string id, string name, string value) =>
        _selectedValues[ControlKey(id, name, string.Empty)] = [value];

    /// <summary>
    /// Records everything a hosted <c>&lt;select multiple&gt;</c> now has selected.
    /// An empty list is a real state — the user deselected everything — and is kept
    /// as such rather than falling back to the markup.
    /// </summary>
    public void SetSelectedValues(string id, string name, IEnumerable<string> values) =>
        _selectedValues[ControlKey(id, name, string.Empty)] = [.. values];

    /// <summary>The recorded primary selection of a <c>&lt;select&gt;</c>, or <c>null</c> if untouched.</summary>
    public string? GetSelectedValue(string id, string name) =>
        GetSelectedValues(id, name) is [string first, ..] ? first : null;

    /// <summary>
    /// Everything recorded for a <c>&lt;select&gt;</c>, or <c>null</c> when the user
    /// has not touched it and the markup still decides.
    /// </summary>
    public IReadOnlyList<string>? GetSelectedValues(string id, string name) =>
        _selectedValues.TryGetValue(ControlKey(id, name, string.Empty), out List<string>? values) ? values : null;

    /// <summary>Records the single file chosen for an <c>&lt;input type="file"&gt;</c>.</summary>
    public void SetSelectedFile(string id, string name, string path) =>
        _selectedFiles[ControlKey(id, name, string.Empty)] = [path];

    /// <summary>
    /// Adds a file to a <c>multiple</c> file input's selection, so picking repeatedly
    /// accumulates rather than replacing. A path already chosen is not added twice.
    /// </summary>
    public void AddSelectedFile(string id, string name, string path)
    {
        string key = ControlKey(id, name, string.Empty);
        if (!_selectedFiles.TryGetValue(key, out List<string>? paths))
        {
            paths = [];
            _selectedFiles[key] = paths;
        }

        if (!paths.Contains(path, StringComparer.Ordinal))
            paths.Add(path);
    }

    /// <summary>Forgets everything chosen for a file input.</summary>
    public void ClearSelectedFiles(string id, string name) =>
        _selectedFiles.Remove(ControlKey(id, name, string.Empty));

    /// <summary>The first path chosen for a file input, or <c>null</c> if none has been.</summary>
    public string? GetSelectedFile(string id, string name) =>
        GetSelectedFiles(id, name) is [string first, ..] ? first : null;

    /// <summary>Every path chosen for a file input, in the order they were picked.</summary>
    public IReadOnlyList<string> GetSelectedFiles(string id, string name) =>
        _selectedFiles.TryGetValue(ControlKey(id, name, string.Empty), out List<string>? paths)
            ? paths
            : [];

    /// <summary>
    /// Builds the request a submission should make, or <c>null</c> when the click is
    /// not a form submission — an ordinary link, or a control outside any form.
    /// </summary>
    /// <param name="pageHtml">The live document, as serialized by the renderer.</param>
    /// <param name="attributes">Attributes of the clicked control, as reported by the renderer.</param>
    /// <param name="resolvedAction">The action URL the renderer already resolved.</param>
    public PageRequest? TryBuildSubmitRequest(
        string pageHtml,
        IReadOnlyDictionary<string, string>? attributes,
        string resolvedAction)
    {
        if (string.IsNullOrEmpty(pageHtml) || attributes is null)
            return null;

        if (!IsSubmitControl(attributes))
            return null;

        DomElement? root = TryParse(pageHtml);
        if (root is null)
            return null;

        DomElement? submitter = HtmlFormSerializer.FindControl(root, attributes);
        DomElement? form = HtmlFormSerializer.FindEnclosingForm(submitter);
        if (form is null)
            return null;

        return BuildRequest(form, submitter, resolvedAction);
    }

    /// <summary>
    /// Builds the request for a submission triggered from a field rather than a
    /// button — pressing Enter in a text control submits its form. Returns
    /// <c>null</c> when the field is not in a form.
    /// </summary>
    /// <param name="baseUrl">Page URL, used to resolve a relative form action.</param>
    public PageRequest? TryBuildFieldSubmitRequest(string pageHtml, string fieldId, string fieldName, string baseUrl)
    {
        if (string.IsNullOrEmpty(pageHtml))
            return null;

        DomElement? root = TryParse(pageHtml);
        if (root is null)
            return null;

        Dictionary<string, string> attributes = [];
        if (fieldId.Length > 0)
            attributes["id"] = fieldId;
        if (fieldName.Length > 0)
            attributes["name"] = fieldName;
        if (attributes.Count == 0)
            return null;

        DomElement? field = HtmlFormSerializer.FindControl(root, attributes);
        DomElement? form = HtmlFormSerializer.FindEnclosingForm(field);
        if (form is null)
            return null;

        // No submitter: pressing Enter in a field submits the form without any
        // button contributing its own name and value.
        return BuildRequest(form, submitter: null, ResolveAction(form.GetAttribute("action"), baseUrl));
    }

    /// <summary>
    /// Turns a form and its submitter into a navigation: a GET puts the form data set
    /// in the query, a POST carries it as the request body.
    /// </summary>
    private PageRequest BuildRequest(DomElement form, DomElement? submitter, string action)
    {
        IReadOnlyList<HtmlFormSerializer.FormEntry> entries =
            HtmlFormSerializer.BuildEntryList(form, submitter, ResolveOverride, ResolveFiles, ResolveSelection);

        // A GET form always puts its data in the query, whatever enctype says — the
        // enctype only applies to a body, and a GET has none.
        if (HtmlFormSerializer.IsGetSubmission(form))
            return new PageRequest(HtmlFormSerializer.ApplyQuery(action, HtmlFormSerializer.EncodeUrlEncoded(entries)));

        string encoding = HtmlFormSerializer.ResolveEncoding(form);
        if (encoding == HtmlFormSerializer.MultipartFormData)
        {
            string boundary = HtmlFormSerializer.CreateMultipartBoundary();
            return new PageRequest(
                action,
                PageRequest.Post,
                $"{HtmlFormSerializer.MultipartFormData}; boundary={boundary}")
            {
                BinaryBody = HtmlFormSerializer.EncodeMultipart(entries, boundary),
            };
        }

        string body = encoding == HtmlFormSerializer.TextPlain
            ? HtmlFormSerializer.EncodeTextPlain(entries)
            : HtmlFormSerializer.EncodeUrlEncoded(entries);

        return new PageRequest(action, PageRequest.Post, encoding, body);
    }

    /// <summary>Resolves a form action against the page URL, leaving it as-is if that fails.</summary>
    internal static string ResolveAction(string? action, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(action))
            return baseUrl;

        // Resolve against the page first: an absolute action survives that unchanged,
        // while treating the action as absolute first would take "/search" for a
        // rooted file path on Unix and produce file:///search.
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? root) &&
            Uri.TryCreate(root, action, out Uri? resolved))
        {
            return resolved.ToString();
        }

        return Uri.TryCreate(action, UriKind.Absolute, out Uri? absolute) ? absolute.ToString() : action;
    }

    /// <summary>
    /// Reads the files the user chose for a control. A path that has gone away or
    /// cannot be read is dropped rather than failing the whole submission; when that
    /// leaves nothing, the control submits as "nothing selected".
    /// </summary>
    private IReadOnlyList<HtmlFormSerializer.SelectedFile> ResolveFiles(DomElement control)
    {
        IReadOnlyList<string> paths = GetSelectedFiles(
            control.GetAttribute("id") ?? string.Empty,
            control.GetAttribute("name") ?? string.Empty);

        if (paths.Count == 0)
            return [];

        List<HtmlFormSerializer.SelectedFile> files = [];
        foreach (string path in paths)
        {
            try
            {
                files.Add(new HtmlFormSerializer.SelectedFile(Path.GetFileName(path), File.ReadAllBytes(path)));
            }
            catch (Exception)
            {
                // Deleted or unreadable since it was chosen.
            }
        }

        return files;
    }

    /// <summary>The options a hosted select has selected, or null to use the markup's.</summary>
    private IReadOnlyList<string>? ResolveSelection(DomElement select) =>
        GetSelectedValues(
            select.GetAttribute("id") ?? string.Empty,
            select.GetAttribute("name") ?? string.Empty);

    private string? ResolveOverride(DomElement control)
    {
        if (!string.Equals(control.TagName, "input", StringComparison.OrdinalIgnoreCase))
            return null;

        string type = control.GetAttribute("type") ?? "text";
        if (!string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "radio", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        bool? state = GetChecked(
            control.GetAttribute("id") ?? string.Empty,
            control.GetAttribute("name") ?? string.Empty,
            control.GetAttribute("value") ?? string.Empty);

        return state switch
        {
            true => HtmlFormSerializer.CheckedOverride,
            false => HtmlFormSerializer.UncheckedOverride,
            null => null,
        };
    }

    /// <summary>
    /// Whether the clicked element is a submit control. The renderer raises the same
    /// event for ordinary links, and a link inside a <c>&lt;form&gt;</c> must not be
    /// turned into a submission.
    /// </summary>
    private static bool IsSubmitControl(IReadOnlyDictionary<string, string> attributes)
    {
        foreach (KeyValuePair<string, string> pair in attributes)
        {
            // An <a href> is a navigation, never a submission.
            if (string.Equals(pair.Key, "href", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (KeyValuePair<string, string> pair in attributes)
        {
            if (!string.Equals(pair.Key, "type", StringComparison.OrdinalIgnoreCase))
                continue;

            return string.Equals(pair.Value, "submit", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pair.Value, "image", StringComparison.OrdinalIgnoreCase);
        }

        // A <button> with no type defaults to submit, and so does a bare control the
        // renderer only resolves to a form action because it is a submit control.
        return true;
    }

    private static DomElement? TryParse(string html)
    {
        try
        {
            return HtmlDocumentParser.ParseDocument(html).Document.DocumentElement;
        }
        catch (Exception)
        {
            // A page the shared parser cannot take is not a reason to break the click;
            // the caller falls back to navigating the action verbatim.
            return null;
        }
    }

    /// <summary>
    /// Identifies a toggle across a page. The browsing path stamps an id on every
    /// checkbox and radio, so the id form is the normal one; the name/value form is
    /// the fallback for a control reached without one.
    /// </summary>
    private static string ControlKey(string id, string name, string value) =>
        id.Length > 0 ? "#" + id : name + "=" + value;
}
