using Broiler.CSS;
using Broiler.Layout.Diagnostics;
using System.Net;
using System.Text;
using System.Globalization;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    public void SetGeneratedTextContent(string text)
    {
        CssBox textBox = null;

        foreach (CssBox child in Boxes)
        {
            if (child.HtmlTag == null)
            {
                textBox = child;
                break;
            }
        }

        if (textBox == null)
        {
            textBox = new CssBox(this, null, BaseUrl);
            textBox.InheritStyle();
        }

        textBox.Text = (text ?? string.Empty).AsMemory();

        // Assigning Text clears the box's words, and words are otherwise only
        // built once, by the parser. Without re-splitting here the generated
        // content would never paint or serialize — a form control edited after
        // load would blank out instead of showing its new value.
        textBox.ParseToWords();
    }

    // CSS Text 3 §White Space Processing: only these five characters are
    // collapsible "white space" for the purpose of white-space collapsing and
    // soft-wrap opportunities — space, tab, line feed, carriage return, form
    // feed. Crucially this excludes U+00A0 NO-BREAK SPACE (&nbsp;) and the other
    // Unicode space separators (U+2000–U+200A, U+202F, U+205F, U+3000, U+1680, …),
    // which are NOT collapsible and carry their own advance width. `char.IsWhiteSpace`
    // returns true for all of those, so using it here collapsed a `&nbsp;`-only inline
    // box away entirely — dropping its background and box-shadow (WPT
    // css/css-view-transitions/inline-child-with-overflow-shadow and any nbsp-only span).
    private static bool IsCssWhiteSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    public void ParseToWords()
    {
        Words.Clear();

        // CSS2.1 §4.3.8: UAs should not render characters from the Unicode
        // "control characters" category (C0 U+0000–U+001F except tab/LF/CR,
        // and C1 U+007F–U+009F).  Strip them before word splitting.
        // Per HTML spec §13.2.2, U+0000 (NULL) is replaced with U+FFFD
        // (REPLACEMENT CHARACTER) so it remains visible.
        var textSpan = _text.Span;
        bool hasControl = false;

        for (int i = 0; i < textSpan.Length; i++)
        {
            char c = textSpan[i];
            if (c != '\t' && c != '\n' && c != '\r'
                && (char.IsControl(c) || (c >= '\u007F' && c <= '\u009F')))
            {
                hasControl = true;
                break;
            }
        }

        if (hasControl)
        {
            var sb = new StringBuilder(textSpan.Length);

            for (int i = 0; i < textSpan.Length; i++)
            {
                char c = textSpan[i];
                if (c == '\0')
                    sb.Append('\uFFFD'); // HTML spec: NULL → REPLACEMENT CHARACTER
                else if (c == '\t' || c == '\n' || c == '\r'
                    || (!char.IsControl(c) && (c < '\u007F' || c > '\u009F')))
                    sb.Append(c);
            }

            _text = sb.ToString().AsMemory();
        }

        int startIdx = 0;
        bool preserveSpaces = WhiteSpace == CssConstants.Pre || WhiteSpace == CssConstants.PreWrap;
        bool respoctNewline = preserveSpaces || WhiteSpace == CssConstants.PreLine;

        // CSS Text 3 §5.1/§5.4: both `word-break: break-all` and `line-break:
        // anywhere` introduce a soft-wrap opportunity between every pair of
        // typographic character units, so each character is materialized as its
        // own word. `anywhere` additionally overrides the no-break behaviour of
        // the WJ/ZW/GL/ZWJ classes (U+2060, U+FEFF, U+200B, NBSP, ZWJ); emitting
        // one word per character already yields those break opportunities.
        bool breakEveryChar = WordBreak == CssConstants.BreakAll
            || string.Equals(LineBreak, "anywhere", StringComparison.OrdinalIgnoreCase);

        // CSS Text 3 §2.1: apply text-transform (case mapping / full-width /
        // full-size-kana) as each word's text is materialized.  Null when the
        // computed value produces no glyph change (none / math-auto / unset).
        var transform = TextTransformer.For(TextTransform);

        textSpan = _text.Span;

        while (startIdx < textSpan.Length)
        {
            while (startIdx < textSpan.Length && textSpan[startIdx] == '\r')
                startIdx++;

            if (startIdx >= textSpan.Length)
                continue;

            var endIdx = startIdx;

            while (endIdx < textSpan.Length && IsCssWhiteSpace(textSpan[endIdx]) && textSpan[endIdx] != '\n')
                endIdx++;

            if (endIdx > startIdx)
            {
                // A collapsible/preserved white-space run is a word boundary for
                // the `capitalize` transform regardless of whether it is emitted.
                transform?.Break();

                if (preserveSpaces)
                {
                    // CSS2.1 §16.6: For pre-wrap, emit each space as a
                    // separate word so the layout engine can break lines
                    // at any space position.  For pre, emit the entire
                    // whitespace run as one word (no wrapping allowed).
                    if (WhiteSpace == CssConstants.PreWrap)
                    {
                        // Cache " " string to avoid per-char allocation
                        const string singleSpace = " ";
                        for (int i = startIdx; i < endIdx; i++)
                        {
                            var ch = _text.Slice(i, 1).ToString();
                            Words.Add(new CssRectWord(this, ch == " " ? singleSpace : ch, false, false));
                        }
                    }
                    else
                    {
                        Words.Add(new CssRectWord(this, WebUtility.HtmlDecode(_text[startIdx..endIdx].ToString()), false, false));
                    }
                }
            }
            else if (breakEveryChar)
            {
                // `word-break: break-all` / `line-break: anywhere` want a soft-wrap
                // opportunity between every typographic character unit. Gather the
                // whole run up to the next collapsible white space and decode its
                // HTML entities as one unit first — a single entity reference (e.g.
                // &#xFEFF;) must not be split mid-reference — then emit one word per
                // decoded code point (surrogate pairs stay intact).
                endIdx = startIdx;
                while (endIdx < textSpan.Length && !IsCssWhiteSpace(textSpan[endIdx]))
                    endIdx++;

                if (endIdx > startIdx)
                {
                    var runText = WebUtility.HtmlDecode(_text[startIdx..endIdx].ToString());
                    if (transform != null)
                        runText = transform.Transform(runText);

                    bool leadingSpace = !preserveSpaces && startIdx > 0 && Words.Count == 0 && IsCssWhiteSpace(textSpan[startIdx - 1]);
                    bool trailingSpace = !preserveSpaces && endIdx < textSpan.Length && IsCssWhiteSpace(textSpan[endIdx]);

                    for (int i = 0; i < runText.Length;)
                    {
                        int charLen = (i + 1 < runText.Length
                            && char.IsHighSurrogate(runText[i])
                            && char.IsLowSurrogate(runText[i + 1])) ? 2 : 1;

                        Words.Add(new CssRectWord(
                            this,
                            runText.Substring(i, charLen),
                            hasSpaceBefore: i == 0 && leadingSpace,
                            hasSpaceAfter: i + charLen >= runText.Length && trailingSpace));

                        i += charLen;
                    }
                }
            }
            else
            {
                endIdx = startIdx;

                while (endIdx < textSpan.Length && !IsCssWhiteSpace(textSpan[endIdx]) && textSpan[endIdx] != '-' && !CommonUtils.IsAsianCharecter(textSpan[endIdx]))
                    endIdx++;

                if (endIdx < textSpan.Length && (textSpan[endIdx] == '-' || CommonUtils.IsAsianCharecter(textSpan[endIdx])))
                {
                    endIdx++;

                    if (endIdx < textSpan.Length &&
                        char.IsHighSurrogate(textSpan[endIdx - 1]) &&
                        char.IsLowSurrogate(textSpan[endIdx]))
                    {
                        endIdx++;
                    }
                }

                if (endIdx > startIdx)
                {
                    var hasSpaceBefore = !preserveSpaces && startIdx > 0 && Words.Count == 0 && IsCssWhiteSpace(textSpan[startIdx - 1]);
                    var hasSpaceAfter = !preserveSpaces && endIdx < textSpan.Length && IsCssWhiteSpace(textSpan[endIdx]);

                    var wordText = WebUtility.HtmlDecode(_text[startIdx..endIdx].ToString());

                    if (transform != null)
                        wordText = transform.Transform(wordText);

                    Words.Add(new CssRectWord(this, wordText, hasSpaceBefore, hasSpaceAfter));
                }
            }

            // create new-line word so it will effect the layout
            if (endIdx < textSpan.Length && textSpan[endIdx] == '\n')
            {
                endIdx++;

                // A forced line break is also a word boundary for `capitalize`.
                transform?.Break();

                if (respoctNewline)
                    Words.Add(new CssRectWord(this, "\n", false, false));
            }

            startIdx = endIdx;
        }
    }

    /// <summary>
    /// Applies CSS Text 3 §2.1 <c>text-transform</c> to successive words of a box.
    /// Case transforms (<c>uppercase</c>/<c>lowercase</c>/<c>capitalize</c>) and the
    /// width transforms (<c>full-width</c>, <c>full-size-kana</c>) can combine. The
    /// <c>capitalize</c> transform titlecases the first letter of each word, so the
    /// instance carries the "at a word boundary" state across the box's words;
    /// <see cref="Break"/> marks an intervening white-space run or forced break.
    /// </summary>
    private sealed class TextTransformer
    {
        private readonly bool _capitalize;
        private readonly bool _upper;
        private readonly bool _lower;
        private readonly bool _fullWidth;
        private readonly bool _fullSizeKana;
        private bool _atWordStart = true;

        private TextTransformer(bool capitalize, bool upper, bool lower, bool fullWidth, bool fullSizeKana)
        {
            _capitalize = capitalize;
            _upper = upper;
            _lower = lower;
            _fullWidth = fullWidth;
            _fullSizeKana = fullSizeKana;
        }

        /// <summary>
        /// Builds a transformer for the computed <c>text-transform</c> value, or
        /// returns <c>null</c> when no keyword changes glyphs (<c>none</c>,
        /// <c>math-auto</c> — element-scoped MathML italicization not modelled here —
        /// or the CSS-wide keywords).
        /// </summary>
        public static TextTransformer For(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            bool capitalize = false, upper = false, lower = false, fullWidth = false, fullSizeKana = false;

            foreach (var token in value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (token.ToLowerInvariant())
                {
                    case "capitalize": capitalize = true; break;
                    case "uppercase": upper = true; break;
                    case "lowercase": lower = true; break;
                    case "full-width": fullWidth = true; break;
                    case "full-size-kana": fullSizeKana = true; break;
                        // none / math-auto / inherit / initial / unrecognized: no glyph change.
                }
            }

            if (!capitalize && !upper && !lower && !fullWidth && !fullSizeKana)
                return null;

            return new TextTransformer(capitalize, upper, lower, fullWidth, fullSizeKana);
        }

        /// <summary>Marks a word boundary between two transformed words.</summary>
        public void Break() => _atWordStart = true;

        public string Transform(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Case mapping first (uppercase/lowercase/capitalize are exclusive per
            // spec; guard order here defensively). String-based Upper/Lower handles
            // one-to-many mappings such as ß → SS.
            string s = text;

            if (_upper)
                s = s.ToUpperInvariant();
            else if (_lower)
                s = s.ToLowerInvariant();
            else if (_capitalize)
                s = Capitalize(s);

            if (_fullWidth || _fullSizeKana)
                s = MapWidth(s);

            return s;
        }

        private string Capitalize(string s)
        {
            var sb = new StringBuilder(s.Length);

            foreach (char c in s)
            {
                if (IsMidWord(c))
                {
                    // Apostrophes and combining marks stay inside the word (so
                    // "can't" → "Can't", not "Can'T") without changing the state.
                    sb.Append(c);
                }
                else if (char.IsLetterOrDigit(c))
                {
                    sb.Append(_atWordStart && char.IsLetter(c) ? char.ToUpperInvariant(c) : c);
                    _atWordStart = false;
                }
                else
                {
                    // White space, hyphens and other punctuation start a new word.
                    sb.Append(c);
                    _atWordStart = true;
                }
            }
            return sb.ToString();
        }

        private static bool IsMidWord(char c)
        {
            if (c == '\'' || c == '’')
                return true;

            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark;
        }

        private string MapWidth(string s)
        {
            var sb = new StringBuilder(s.Length);

            foreach (char c in s)
            {
                char m = c;

                if (_fullSizeKana)
                    m = ToFullSizeKana(m);

                if (_fullWidth)
                    m = ToFullWidth(m);

                sb.Append(m);
            }

            return sb.ToString();
        }

        // U+0021..U+007E → the fullwidth forms U+FF01..U+FF5E. The ASCII space is
        // left as U+0020 so white-space processing is unaffected.
        private static char ToFullWidth(char c)
            => c >= '!' && c <= '~' ? (char)(c - 0x0021 + 0xFF01) : c;

        // Small (sutegana) kana → their full-size equivalents (CSS Text 3 §2.1.1).
        private static char ToFullSizeKana(char c) => c switch
        {
            // Hiragana
            'ぁ' => 'あ',
            'ぃ' => 'い',
            'ぅ' => 'う',
            'ぇ' => 'え',
            'ぉ' => 'お',
            'っ' => 'つ',
            'ゃ' => 'や',
            'ゅ' => 'ゆ',
            'ょ' => 'よ',
            'ゎ' => 'わ',
            'ゕ' => 'か',
            'ゖ' => 'け',
            // Katakana
            'ァ' => 'ア',
            'ィ' => 'イ',
            'ゥ' => 'ウ',
            'ェ' => 'エ',
            'ォ' => 'オ',
            'ッ' => 'ツ',
            'ャ' => 'ヤ',
            'ュ' => 'ユ',
            'ョ' => 'ヨ',
            'ヮ' => 'ワ',
            'ヵ' => 'カ',
            'ヶ' => 'ケ',
            _ => c,
        };
    }

    internal virtual void MeasureWordsSize(ILayoutEnvironment g)
    {
        if (_wordsSizeMeasured)
            return;

        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.TextMeasure);

        LoadBackgroundImageIfNeeded();
        MeasureWordSpacing(g);

        if (Words.Count > 0)
        {
            foreach (var boxWord in Words)
            {
                boxWord.Width = boxWord.Text != "\n" ? g.MeasureText(ActualFont, boxWord.Text).Width : 0;
                boxWord.Height = ActualFont.Height;
            }
        }

        _wordsSizeMeasured = true;
    }

    /// <summary>
    /// Recursively calls <see cref="MeasureWordsSize"/> on all descendant
    /// boxes so that <c>ActualWordSpacing</c> and word dimensions are
    /// computed before <see cref="GetMinMaxWidth"/> is invoked for
    /// shrink-to-fit width (CSS2.1 §10.3.7).
    /// Note: the current box (<c>this</c>) is already measured by the
    /// <see cref="MeasureWordsSize"/> call at the start of
    /// <see cref="PerformLayoutImp"/>; only descendants need measuring.
    /// </summary>
    private void EnsureDescendantWordsMeasured(ILayoutEnvironment g)
    {
        var stack = new Stack<CssBox>();

        foreach (var child in Boxes)
            stack.Push(child);

        while (stack.Count > 0)
        {
            var box = stack.Pop();
            box.MeasureWordsSize(g);

            foreach (var child in box.Boxes)
                stack.Push(child);
        }
    }

    internal void InvalidateFontDependentSubtree()
    {
        InvalidateFontDependentValues();

        foreach (var child in Boxes)
            child.InvalidateFontDependentSubtree();
    }
}
