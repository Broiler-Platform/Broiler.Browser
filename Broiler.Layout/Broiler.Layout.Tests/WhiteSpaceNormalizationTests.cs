using Broiler.Layout.Engine;

namespace Broiler.Layout.Tests;

/// <summary>
/// Covers <see cref="CssUtils.NormalizeWhiteSpaceValue"/>: the CSS Text 4
/// <c>white-space</c> shorthand (a shorthand for <c>white-space-collapse</c> and
/// <c>text-wrap-mode</c>) is folded onto the legacy single keyword the layout
/// engine keys off, so modern longhand-style values are no longer dropped.
/// </summary>
public sealed class WhiteSpaceNormalizationTests
{
    [Theory]
    // Legacy single keywords pass through untouched.
    [InlineData("normal", "normal")]
    [InlineData("nowrap", "nowrap")]
    [InlineData("pre", "pre")]
    [InlineData("pre-wrap", "pre-wrap")]
    [InlineData("pre-line", "pre-line")]
    // Modern single white-space-collapse keywords (text-wrap-mode defaults to wrap).
    [InlineData("collapse", "normal")]
    [InlineData("preserve", "pre-wrap")]
    [InlineData("preserve-breaks", "pre-line")]
    // Modern single text-wrap-mode keywords (white-space-collapse defaults to collapse).
    [InlineData("wrap", "normal")]
    // Two-longhand form, either order.
    [InlineData("collapse wrap", "normal")]
    [InlineData("collapse nowrap", "nowrap")]
    [InlineData("preserve wrap", "pre-wrap")]
    [InlineData("preserve nowrap", "pre")]
    [InlineData("nowrap preserve", "pre")]
    [InlineData("preserve-breaks wrap", "pre-line")]
    // No exact legacy equivalent: fold onto the closest keyword.
    [InlineData("preserve-breaks nowrap", "pre-line")]
    // break-spaces has no legacy keyword and is passed through for the engine.
    [InlineData("break-spaces", "break-spaces")]
    [InlineData("break-spaces nowrap", "break-spaces")]
    // Case and surrounding whitespace are normalized.
    [InlineData("  PRESERVE-BREAKS  ", "pre-line")]
    public void NormalizeWhiteSpaceValue_FoldsShorthandOntoLegacyKeyword(string input, string expected)
    {
        Assert.Equal(expected, CssUtils.NormalizeWhiteSpaceValue(input));
    }

    [Theory]
    // Unrecognized/malformed values are left untouched (the declaration is either
    // dropped upstream at validation or handled elsewhere).
    [InlineData("x-bogus")]
    [InlineData("preserve preserve")]
    [InlineData("wrap nowrap")]
    public void NormalizeWhiteSpaceValue_LeavesUnrecognizedValuesUntouched(string input)
    {
        Assert.Equal(input, CssUtils.NormalizeWhiteSpaceValue(input));
    }
}
