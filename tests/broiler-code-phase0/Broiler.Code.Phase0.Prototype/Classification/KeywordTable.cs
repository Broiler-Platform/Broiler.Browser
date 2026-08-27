namespace Broiler.Code.Phase0.Prototype.Classification;

/// <summary>
/// The keyword set the portable classifier recognizes. Reserved words are
/// listed in full; contextual words are limited to the ones that are almost
/// never used as ordinary identifiers, so the fallback classifier does not
/// miscolor a local named <c>value</c>, <c>from</c>, or <c>where</c>.
/// </summary>
internal static class KeywordTable
{
    private static readonly string[] ControlKeywords =
    [
        "await", "break", "case", "catch", "continue", "default", "do", "else",
        "finally", "for", "foreach", "goto", "if", "return", "switch", "throw",
        "try", "while", "yield",
    ];

    private static readonly string[] Keywords =
    [
        "abstract", "as", "base", "bool", "byte", "char", "checked", "class",
        "const", "decimal", "delegate", "double", "dynamic", "enum", "event",
        "explicit", "extern", "false", "fixed", "float", "implicit", "in",
        "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "nint", "nuint", "null", "object", "operator", "out", "override",
        "params", "partial", "private", "protected", "public", "readonly",
        "record", "ref", "sbyte", "sealed", "short", "sizeof", "stackalloc",
        "static", "string", "struct", "this", "true", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void",
        "volatile",
    ];

    private static readonly HashSet<string> ControlSet =
        new(ControlKeywords, StringComparer.Ordinal);

    private static readonly HashSet<string> KeywordSet =
        new(Keywords, StringComparer.Ordinal);

    /// <summary>
    /// <c>async</c> is contextual but is a keyword wherever it can legally
    /// appear, so it is treated as one.
    /// </summary>
    static KeywordTable() => KeywordSet.Add("async");

    public static ClassificationKind Lookup(ReadOnlySpan<char> word)
    {
        // Every keyword is ASCII lower-case and at most 10 characters, so a
        // length and first-character check rejects the overwhelming majority of
        // identifiers before any hashing happens.
        if (word.Length is < 2 or > 10 || !char.IsAsciiLetterLower(word[0]))
            return ClassificationKind.None;

        var lookup = ControlSet.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.Contains(word))
            return ClassificationKind.ControlKeyword;

        return KeywordSet.GetAlternateLookup<ReadOnlySpan<char>>().Contains(word)
            ? ClassificationKind.Keyword
            : ClassificationKind.None;
    }
}
