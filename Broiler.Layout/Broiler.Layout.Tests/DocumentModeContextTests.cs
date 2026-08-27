using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// <see cref="DocumentModeContext.IsQuirksHtml"/> against the HTML Standard's DOCTYPE conditions
/// ("The initial insertion mode"). The public-identifier cases are the point: a doctype whose *name*
/// is <c>html</c> can still select quirks mode, and every legacy page that relies on quirks-mode
/// behaviour is written that way.
/// </summary>
public sealed class DocumentModeContextTests
{
    [Theory]
    // No doctype at all, and a doctype that is not html.
    [InlineData("<html><body>x</body></html>", true)]
    [InlineData("<!DOCTYPE foo><html></html>", true)]
    // The standards-mode doctype, in the forms a page actually writes it.
    [InlineData("<!DOCTYPE html><html></html>", false)]
    [InlineData("<!doctype html>\n<html></html>", false)]
    [InlineData("<!DOCTYPE HTML>", false)]
    // about:legacy-compat is standards mode: the name is html and neither identifier is listed.
    [InlineData("""<!DOCTYPE html SYSTEM "about:legacy-compat">""", false)]
    // www.7-zip.org's doctype, and the whole HTML 4.0 family with it.
    [InlineData("""<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.0 Transitional//EN">""", true)]
    [InlineData("""<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.0 Frameset//EN">""", true)]
    [InlineData("""<!DOCTYPE html PUBLIC "-//IETF//DTD HTML 2.0//EN">""", true)]
    [InlineData("""<!DOCTYPE html PUBLIC "-//W3C//DTD HTML 3.2 Final//EN">""", true)]
    // Exact-match public identifiers.
    [InlineData("""<!DOCTYPE html PUBLIC "HTML">""", true)]
    [InlineData("""<!DOCTYPE html PUBLIC "-/W3C/DTD HTML 4.0 Transitional/EN">""", true)]
    // The listed system identifier selects quirks whatever the public identifier says.
    [InlineData("""<!DOCTYPE html PUBLIC "" "http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd">""", true)]
    // HTML 4.01 Transitional/Frameset: full quirks without a system identifier, limited-quirks with
    // one — and limited-quirks is not what this predicate reports.
    [InlineData("""<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01 Transitional//EN">""", true)]
    [InlineData("""<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01 Transitional//EN" "http://www.w3.org/TR/html4/loose.dtd">""", false)]
    [InlineData("""<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01 Frameset//EN" "http://www.w3.org/TR/html4/frameset.dtd">""", false)]
    // HTML 4.01 Strict is standards mode either way — it is on neither list.
    [InlineData("""<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01//EN" "http://www.w3.org/TR/html4/strict.dtd">""", false)]
    // XHTML 1.0 Transitional is limited-quirks, so not full quirks.
    [InlineData("""<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">""", false)]
    [InlineData("""<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Strict//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd">""", false)]
    public void Classifies(string html, bool quirks) =>
        Assert.Equal(quirks, DocumentModeContext.IsQuirksHtml(html));

    // The spec compares both identifiers ASCII case-insensitively, and the keyword and quoting style
    // vary across real documents.
    [Theory]
    [InlineData("""<!doctype html public "-//w3c//dtd html 4.0 transitional//en">""")]
    [InlineData("""<!DOCTYPE HTML PUBLIC '-//W3C//DTD HTML 4.0 Transitional//EN'>""")]
    [InlineData("""<!DoCtYpE   hTmL   pUbLiC   "-//W3C//DTD HTML 4.0 Transitional//EN"  >""")]
    public void Identifier_Matching_Is_Case_Insensitive(string html) =>
        Assert.True(DocumentModeContext.IsQuirksHtml(html));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_Input_Is_Quirks(string? html) =>
        Assert.True(DocumentModeContext.IsQuirksHtml(html!));
}
