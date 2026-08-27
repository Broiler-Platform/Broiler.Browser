using System.Text.RegularExpressions;

namespace Broiler.Layout;

/// <summary>
/// Per-render (thread-local) document-mode state, mirroring the way
/// <c>Broiler.CSS.CssLengthParser</c> exposes the current viewport size for the
/// duration of a render. The host publishes the document's quirks-mode flag here
/// when it parses the page, so layout — which on the HTML-string parse path holds
/// no reference back to the source document — can consult it while sizing the
/// root and body boxes (the quirks-mode body/html fill-viewport behaviour,
/// https://quirks.spec.whatwg.org/).
///
/// Layout runs on the same thread immediately after the parse that set this, and
/// each parse overwrites it, so a stale <c>true</c> never leaks into a later
/// standards-mode render. <c>CssBox</c> additionally caches the value on the tree
/// root the first time it reads it, so it survives a re-layout pass that may run
/// on a different thread.
/// </summary>
public static partial class DocumentModeContext
{
    [ThreadStatic]
    private static bool _quirksMode;

    /// <summary>The quirks-mode flag of the document currently being laid out.</summary>
    /// <remarks>
    /// Both render paths publish this on every parse: the HtmlBridge DOM path at
    /// <c>DomBridge.HtmlParsing.cs</c>, and — since Phase 2 item #9 — the HTML-string path at
    /// <c>HtmlContainerInt.SetHtmlWithStyleSet</c>. Until the second of those existed the flag was
    /// simply never written on the string path, which was harmless only because one thread rendered
    /// one document at a time; a pooled thread arriving from a quirks-mode DOM render would have
    /// carried that <c>true</c> into a standards-mode string render. <see cref="AmbientRenderState"/>
    /// is the check that would have caught it, and now has nothing to catch here.
    /// </remarks>
    public static bool CurrentQuirksMode
    {
        get
        {
            AmbientRenderState.AssertEstablished(
                AmbientRenderState.Slots.DocumentMode, nameof(CurrentQuirksMode));
            return _quirksMode;
        }

        set
        {
            _quirksMode = value;
            AmbientRenderState.MarkEstablished(AmbientRenderState.Slots.DocumentMode);
        }
    }

    /// <summary>
    /// Quirks-mode determination from raw HTML, following the HTML Standard's DOCTYPE conditions.
    /// A document with no doctype, or one whose name is not <c>html</c>, is in quirks mode; a bare
    /// <c>&lt;!DOCTYPE html&gt;</c> selects standards mode; and a name of <c>html</c> carrying one
    /// of the legacy public identifiers selects quirks mode too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The public identifier half is not a refinement anyone can skip: the doctypes real legacy
    /// pages carry are exactly the ones it recognises. www.7-zip.org declares
    /// <c>&lt;!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.0 Transitional//EN"&gt;</c>, whose name *is*
    /// <c>html</c> — so on a name-only test it read as standards mode, and every quirks-mode
    /// behaviour keyed off this flag (the body/html fill, both table quirks) was silently inert on
    /// the pages that need them most.
    /// </para>
    /// <para>
    /// This reads the doctype out of the markup rather than from a parsed tree because both callers
    /// have only the string: the flag is published before the document is built. The regex takes the
    /// first <c>&lt;!doctype&gt;</c> in the source, as it always has.
    /// </para>
    /// </remarks>
    public static bool IsQuirksHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return true;

        var match = QuirksRegex().Match(html);
        if (!match.Success || !match.Groups["name"].Value.Equals("html", StringComparison.OrdinalIgnoreCase))
            return true;

        // "PUBLIC <public id> [<system id>]" versus "SYSTEM <system id>": the first quoted string
        // means different things in the two forms, so which group is which depends on the keyword.
        var kind = match.Groups["kind"].Value;
        var first = Unquote(match.Groups["first"].Value);
        var second = Unquote(match.Groups["second"].Value);

        bool isPublic = kind.Equals("public", StringComparison.OrdinalIgnoreCase);
        string publicId = isPublic ? first : string.Empty;
        string systemId = isPublic ? second : first;

        return SelectsQuirksMode(publicId, systemId);
    }

    /// <summary>
    /// The HTML Standard's quirks-mode conditions for a DOCTYPE whose name is <c>html</c>
    /// (<see href="https://html.spec.whatwg.org/multipage/parsing.html#the-initial-insertion-mode">"The
    /// initial insertion mode"</see>). Both identifiers are compared ASCII case-insensitively, and an
    /// empty system identifier counts as missing, as the spec requires.
    /// </summary>
    /// <remarks>
    /// Limited-quirks mode is deliberately not modelled: this predicate answers "full quirks?", and
    /// the two limited-quirks families (XHTML 1.0 Transitional/Frameset, and HTML 4.01
    /// Transitional/Frameset <em>with</em> a system identifier) fall through to <c>false</c> — which
    /// is what the callers, all of them full-quirks behaviours, need.
    /// </remarks>
    private static bool SelectsQuirksMode(string publicId, string systemId)
    {
        foreach (var exact in QuirksPublicIdentifiers)
            if (publicId.Equals(exact, StringComparison.OrdinalIgnoreCase))
                return true;

        if (systemId.Equals(
                "http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd",
                StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var prefix in QuirksPublicIdentifierPrefixes)
            if (publicId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        // HTML 4.01 Transitional/Frameset are full quirks only without a system identifier; with one
        // they are limited-quirks, which is not this predicate's answer.
        if (systemId.Length == 0)
            foreach (var prefix in QuirksPublicIdentifierPrefixesWithoutSystemId)
                if (publicId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;

        return false;
    }

    /// <summary>Strips the surrounding quotes the DOCTYPE regex captures with an identifier.</summary>
    private static string Unquote(string value) =>
        value.Length >= 2 ? value[1..^1] : string.Empty;

    /// <summary>Public identifiers that select quirks mode by exact match.</summary>
    private static readonly string[] QuirksPublicIdentifiers =
    [
        "-//W3O//DTD W3 HTML Strict 3.0//EN//",
        "-/W3C/DTD HTML 4.0 Transitional/EN",
        "HTML",
    ];

    /// <summary>Public-identifier prefixes that select quirks mode regardless of system identifier.</summary>
    private static readonly string[] QuirksPublicIdentifierPrefixes =
    [
        "+//Silmaril//dtd html Pro v0r11 19970101//",
        "-//AS//DTD HTML 3.0 asWedit + extensions//",
        "-//AdvaSoft Ltd//DTD HTML 3.0 asWedit + extensions//",
        "-//IETF//DTD HTML 2.0 Level 1//",
        "-//IETF//DTD HTML 2.0 Level 2//",
        "-//IETF//DTD HTML 2.0 Strict Level 1//",
        "-//IETF//DTD HTML 2.0 Strict Level 2//",
        "-//IETF//DTD HTML 2.0 Strict//",
        "-//IETF//DTD HTML 2.0//",
        "-//IETF//DTD HTML 2.1E//",
        "-//IETF//DTD HTML 3.0//",
        "-//IETF//DTD HTML 3.2 Final//",
        "-//IETF//DTD HTML 3.2//",
        "-//IETF//DTD HTML 3//",
        "-//IETF//DTD HTML Level 0//",
        "-//IETF//DTD HTML Level 1//",
        "-//IETF//DTD HTML Level 2//",
        "-//IETF//DTD HTML Level 3//",
        "-//IETF//DTD HTML Strict Level 0//",
        "-//IETF//DTD HTML Strict Level 1//",
        "-//IETF//DTD HTML Strict Level 2//",
        "-//IETF//DTD HTML Strict Level 3//",
        "-//IETF//DTD HTML Strict//",
        "-//IETF//DTD HTML//",
        "-//Metrius//DTD Metrius Presentational//",
        "-//Microsoft//DTD Internet Explorer 2.0 HTML Strict//",
        "-//Microsoft//DTD Internet Explorer 2.0 HTML//",
        "-//Microsoft//DTD Internet Explorer 2.0 Tables//",
        "-//Microsoft//DTD Internet Explorer 3.0 HTML Strict//",
        "-//Microsoft//DTD Internet Explorer 3.0 HTML//",
        "-//Microsoft//DTD Internet Explorer 3.0 Tables//",
        "-//Netscape Comm. Corp.//DTD HTML//",
        "-//Netscape Comm. Corp.//DTD Strict HTML//",
        "-//O'Reilly and Associates//DTD HTML 2.0//",
        "-//O'Reilly and Associates//DTD HTML Extended 1.0//",
        "-//O'Reilly and Associates//DTD HTML Extended Relaxed 1.0//",
        "-//SQ//DTD HTML 2.0 HoTMetaL + extensions//",
        "-//SoftQuad Software//DTD HoTMetaL PRO 6.0::19990601::extensions to HTML 4.0//",
        "-//SoftQuad//DTD HoTMetaL PRO 4.0::19971010::extensions to HTML 4.0//",
        "-//Spyglass//DTD HTML 2.0 Extended//",
        "-//Sun Microsystems Corp.//DTD HotJava HTML//",
        "-//Sun Microsystems Corp.//DTD HotJava Strict HTML//",
        "-//W3C//DTD HTML 3 1995-03-24//",
        "-//W3C//DTD HTML 3.2 Draft//",
        "-//W3C//DTD HTML 3.2 Final//",
        "-//W3C//DTD HTML 3.2//",
        "-//W3C//DTD HTML 3.2S Draft//",
        "-//W3C//DTD HTML 4.0 Frameset//",
        "-//W3C//DTD HTML 4.0 Transitional//",
        "-//W3C//DTD HTML Experimental 19960712//",
        "-//W3C//DTD HTML Experimental 970421//",
        "-//W3C//DTD W3 HTML//",
        "-//W3O//DTD W3 HTML 3.0//",
        "-//WebTechs//DTD Mozilla HTML 2.0//",
        "-//WebTechs//DTD Mozilla HTML//",
    ];

    /// <summary>
    /// Public-identifier prefixes that select quirks mode only when the system identifier is missing
    /// or empty; with one present these are limited-quirks instead.
    /// </summary>
    private static readonly string[] QuirksPublicIdentifierPrefixesWithoutSystemId =
    [
        "-//W3C//DTD HTML 4.01 Frameset//",
        "-//W3C//DTD HTML 4.01 Transitional//",
    ];

    [GeneratedRegex(
        """<!doctype\s+(?<name>[^\s>]+)(?:\s+(?<kind>public|system)\s+(?<first>"[^"]*"|'[^']*')(?:\s+(?<second>"[^"]*"|'[^']*'))?)?""",
        RegexOptions.IgnoreCase)]
    private static partial Regex QuirksRegex();
}
