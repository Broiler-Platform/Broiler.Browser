namespace Broiler.Code.Phase0.Prototype.Classification;

/// <summary>
/// The control-facing classification vocabulary. It is deliberately
/// language-neutral: the CodeEditor maps these kinds to theme tokens and must
/// never learn C# token kinds, and a second language can reuse the same set
/// without changing the control.
/// </summary>
public enum ClassificationKind : byte
{
    /// <summary>Default foreground; never emitted as a span.</summary>
    None = 0,
    Comment,
    DocumentationComment,
    Keyword,
    ControlKeyword,
    PreprocessorKeyword,
    PreprocessorText,
    StringLiteral,
    EscapeSequence,
    CharacterLiteral,
    NumericLiteral,
    Operator,
    Punctuation,
}
