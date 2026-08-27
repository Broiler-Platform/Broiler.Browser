namespace Broiler.Layout;

/// <summary>
/// Lightweight descriptor of the source HTML element a layout box was built
/// from: its tag name, self-closing flag, and attribute bag. Relocated from the
/// renderer into <c>Broiler.Layout</c> with the layout code (see
/// <c>Broiler.Layout/docs/roadmap.md</c>). The attribute bag is
/// exposed read-only; controlled mutations go through <see cref="SetAttribute"/>.
/// </summary>
public sealed class HtmlTag
{
    private readonly Dictionary<string, string> _attributes;

    public HtmlTag(string name, bool isSingle, IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        IsSingle = isSingle;
        _attributes = attributes != null
            ? new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Attributes = _attributes;
    }

    public string Name { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public bool IsSingle { get; }
    public bool HasAttributes() => Attributes.Count > 0;
    public bool HasAttribute(string attribute) => Attributes.ContainsKey(attribute);
    public string? TryGetAttribute(string attribute, string? defaultValue = null) =>
        Attributes.TryGetValue(attribute, out string? value) ? value : defaultValue;

    public void SetAttribute(string attribute, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(attribute);
        _attributes[attribute] = value ?? string.Empty;
    }

    public override string ToString() => $"<{Name}>";
}
