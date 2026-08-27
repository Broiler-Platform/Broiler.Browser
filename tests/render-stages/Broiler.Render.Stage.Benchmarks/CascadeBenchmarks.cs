using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Broiler.CSS;
using Broiler.CSS.Dom;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.HTML.Image;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// P0-a harness (1): the cascade and computed style on their own, driven through
/// <c>CssStyleEngine</c> rather than through a render.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the benchmark multithreading item #11 has to be argued from.</b> The claim there is
/// that <c>CssStyleEngine.CollectFromRules</c> scans every rule of every sheet for every element, so
/// per-element cost grows with the *total* rule count rather than with the number of rules that
/// match. <see cref="RuleScalingBenchmarks"/> tests exactly that by holding the element set fixed and varying
/// the sheet size: linear growth is the defect, flat is the rule index working.
/// </para>
/// <para>
/// <b>Every case computes on a cold engine.</b> <c>GetComputedStyle</c> memoizes per element, so a
/// second pass over the same elements measures a dictionary lookup and nothing else — which is a
/// real and interesting number, and is why <see cref="ComputedStyleCacheHit"/> exists as its own
/// case rather than being allowed to contaminate the others.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class CascadeBenchmarks
{
    /// <summary>Corpus page under test.</summary>
    [ParamsSource(nameof(PageNames))]
    public string Page { get; set; } = "rules";

    /// <summary>Parameter source; see <see cref="Corpus.Names"/>.</summary>
    public static string[] PageNames() => Corpus.Names();

    private DomDocument _document = null!;
    private DomElement[] _elements = null!;
    private CssStyleSheet _sheet = null!;
    private CssEnvironment _environment;
    private CssStyleEngine _warmEngine = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var page = Corpus.ByName(Page);

        _document = new HtmlDocumentParser().ParseDocument(page.Html).Document;
        _elements = _document.Descendants().OfType<DomElement>().ToArray();

        // The author sheet only. The user-agent sheet is a fixed cost shared by every page and
        // adding it would make the rule-count axis in RuleScaling read against a moving floor.
        _sheet = HtmlRender.ParseStyleSheetModel(page.Css, combineWithDefault: false);
        _environment = new CssEnvironment(Corpus.ViewportWidth, Corpus.ViewportHeight);

        _warmEngine = NewEngine(_sheet);
        foreach (var element in _elements)
            _warmEngine.GetComputedStyle(element, _environment);
    }

    private CssStyleEngine NewEngine(CssStyleSheet sheet)
    {
        var engine = new CssStyleEngine();
        if (sheet.Rules.Count > 0)
            engine.AddStyleSheet(sheet, CssOrigin.Author);
        engine.UpdateEnvironment(_environment);
        return engine;
    }

    /// <summary>How many elements the page contributes — the multiplier in item #11's O(elements x rules).</summary>
    [Benchmark(Description = "element count (control, not a timing)")]
    public int ElementCount() => _elements.Length;

    /// <summary>
    /// A cold cascade over every element of the page: one engine, no memoized styles, every element
    /// resolved once. This is the figure the render's <c>parse+cascade</c> stage is mostly made of.
    /// </summary>
    [Benchmark(Baseline = true, Description = "cold cascade over every element")]
    public int ColdCascade()
    {
        var engine = NewEngine(_sheet);
        var total = 0;
        foreach (var element in _elements)
            total += engine.GetComputedStyle(element, _environment).GetPropertyValue("color").Length;
        return total;
    }

    /// <summary>
    /// The same pass against an engine that has already resolved every element: the memoized read
    /// path. The gap between this and <see cref="ColdCascade"/> is what the cascade actually costs,
    /// as opposed to what the surrounding bookkeeping costs.
    /// </summary>
    [Benchmark(Description = "warm cascade (cache hit) over every element")]
    public int ComputedStyleCacheHit()
    {
        var total = 0;
        foreach (var element in _elements)
            total += _warmEngine.GetComputedStyle(element, _environment).GetPropertyValue("color").Length;
        return total;
    }
}

/// <summary>
/// The rule-count axis of P0-a harness (1), split out because it needs its own parameter and would
/// otherwise multiply out against every corpus page.
/// </summary>
/// <remarks>
/// Element set and matched-rule count are both held fixed; only the number of rules that <em>cannot</em>
/// match grows. Item #11 predicts the per-element cost tracks <see cref="RuleCount"/> anyway, because
/// the scan is linear in the sheet. Any future rule index has to flatten this curve to be worth its
/// complexity, and this is the case that says whether it did.
/// </remarks>
[MemoryDiagnoser]
public class RuleScalingBenchmarks
{
    /// <summary>
    /// Total rules in the sheet. The spread is wide on purpose: two adjacent points cannot tell
    /// linear from constant, and 100 to 3200 is five doublings.
    /// </summary>
    [Params(100, 400, 1600, 3200)]
    public int RuleCount { get; set; } = 400;

    private const int Elements = 400;

    private DomElement[] _elements = null!;
    private CssStyleSheet _sheet = null!;
    private CssEnvironment _environment;
    private DomDocument _document = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var body = new System.Text.StringBuilder(Elements * 48);
        for (var i = 0; i < Elements; i++)
            body.Append("<div class=\"m").Append(i % 4).Append("\"><span>x</span></div>");

        _document = new HtmlDocumentParser()
            .ParseDocument("<!DOCTYPE html><html><body>" + body + "</body></html>").Document;
        _elements = _document.Descendants().OfType<DomElement>().ToArray();

        // Four rules that match, and RuleCount-4 that cannot: the matched set is constant across
        // every point on the axis, so the only variable is how many rules have to be rejected.
        var css = new System.Text.StringBuilder(RuleCount * 40);
        for (var i = 0; i < 4; i++)
            css.Append(".m").Append(i).Append("{color:#0").Append(i).Append('0').Append(i).Append('}');
        for (var i = 4; i < RuleCount; i++)
            css.Append(".unmatched-").Append(i).Append("{color:#123;margin-left:").Append(i % 9).Append("px}");

        _sheet = HtmlRender.ParseStyleSheetModel(css.ToString(), combineWithDefault: false);
        _environment = new CssEnvironment(Corpus.ViewportWidth, Corpus.ViewportHeight);
    }

    /// <summary>Cold cascade over a fixed element set against a sheet of <see cref="RuleCount"/> rules.</summary>
    [Benchmark(Description = "cold cascade, fixed elements, growing sheet")]
    public int ColdCascade()
    {
        var engine = new CssStyleEngine();
        engine.AddStyleSheet(_sheet, CssOrigin.Author);
        engine.UpdateEnvironment(_environment);

        var total = 0;
        foreach (var element in _elements)
            total += engine.GetComputedStyle(element, _environment).GetPropertyValue("color").Length;
        return total;
    }
}
