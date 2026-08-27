using Broiler.CSS;
using Broiler.Dom;
using Broiler.Layout.Engine;

namespace Broiler.Layout.Tests;

/// <summary>
/// The classification behind multithreading item #14's first slice: which DOM mutations can be
/// answered without rebuilding the render tree.
/// </summary>
/// <remarks>
/// <para>
/// Every case here is written the way the elision has to be argued — one side asserts that a
/// mutation the render tree <em>can</em> see forces a rebuild, and the other that one it cannot see
/// does not. The first side is the one that matters: a false "no rebuild" is a stale render, and a
/// false "rebuild" is only slow. So the connected cases outnumber the detached ones, and the
/// awkward shapes (a node moved out of the tree, a mutation this ledger never saw) are on the
/// connected side by construction.
/// </para>
/// <para>
/// What these cannot reach is the container: <c>HtmlContainerInt</c> lives in the
/// <c>Broiler.HTML</c> submodule and its render tree needs a bitmap surface. That the container
/// actually skips the rebuild — rather than merely being told it may — is asserted by the relayout
/// harness, which reports the decision per row (<c>--relayout-profile</c>, the <c>rebuilt?</c>
/// column).
/// </para>
/// </remarks>
public class RenderTreeInvalidationTests
{
    /// <summary>A document with a root element holding one paragraph with one text node.</summary>
    private static (DomDocument Document, DomElement Root, DomElement Paragraph, DomText Text) Tree()
    {
        var document = new DomDocument();
        var root = document.CreateElement("html");
        document.AppendChild(root);

        var paragraph = document.CreateElement("p");
        root.AppendChild(paragraph);

        var text = document.CreateTextNode("hello");
        paragraph.AppendChild(text);

        return (document, root, paragraph, text);
    }

    [Fact(Timeout = 600000)]
    public void An_untouched_document_needs_no_rebuild()
    {
        var (document, _, _, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        Assert.False(ledger.RequiresRebuild());
        Assert.Equal(0, ledger.ObservedMutations);
    }

    [Fact(Timeout = 600000)]
    public void An_attribute_write_on_a_connected_element_requires_a_rebuild()
    {
        var (document, _, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        paragraph.SetAttribute("class", "highlight");

        Assert.True(ledger.RequiresRebuild());
        Assert.Equal(1, ledger.RenderAffectingMutations);
    }

    [Fact(Timeout = 600000)]
    public void An_attribute_write_on_a_detached_element_does_not()
    {
        var (document, _, _, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        var orphan = document.CreateElement("div");
        orphan.SetAttribute("class", "highlight");

        // The version moved — creation does not publish, but the attribute write does — and the
        // answer is still "no rebuild", which is the whole claim.
        Assert.True(document.Version > 0);
        Assert.False(ledger.RequiresRebuild());
        Assert.Equal(1, ledger.ElidedMutations);
    }

    [Fact(Timeout = 600000)]
    public void A_text_write_is_classified_by_whether_the_text_node_is_in_the_tree()
    {
        var (document, _, _, text) = Tree();
        var orphanText = document.CreateTextNode("offscreen");

        using (var ledger = new RenderTreeInvalidation(document))
        {
            orphanText.Data = "changed";
            Assert.False(ledger.RequiresRebuild());
        }

        using (var ledger = new RenderTreeInvalidation(document))
        {
            text.Data = "changed";
            Assert.True(ledger.RequiresRebuild());
        }
    }

    [Fact(Timeout = 600000)]
    public void Building_a_subtree_off_the_document_requires_no_rebuild()
    {
        var (document, _, _, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        var detached = document.CreateElement("div");
        for (var i = 0; i < 8; i++)
        {
            var row = document.CreateElement("p");
            row.SetAttribute("class", "row");
            row.AppendChild(document.CreateTextNode("row"));
            detached.AppendChild(row);
        }

        Assert.Equal(24, ledger.ObservedMutations);
        Assert.Equal(24, ledger.ElidedMutations);
        Assert.False(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Inserting_that_subtree_requires_one()
    {
        var (document, _, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        var detached = document.CreateElement("div");
        detached.AppendChild(document.CreateElement("span"));
        Assert.False(ledger.RequiresRebuild());

        paragraph.AppendChild(detached);

        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Removing_a_connected_child_requires_a_rebuild()
    {
        var (document, root, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        root.RemoveChild(paragraph);

        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Moving_a_connected_node_into_a_detached_parent_requires_a_rebuild()
    {
        // The trap this classification could have fallen into: the *insertion* record names a
        // detached parent, so an implementation that looked only at insertions would elide a
        // mutation that empties part of the page. It is the removal record — published against the
        // still-connected old parent — that makes the answer right.
        var (document, _, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        var elsewhere = document.CreateElement("div");
        elsewhere.AppendChild(paragraph);

        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Editing_a_subtree_after_it_leaves_the_tree_requires_only_the_removals_rebuild()
    {
        var (document, root, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        root.RemoveChild(paragraph);
        Assert.True(ledger.RequiresRebuild());

        // The container rebuilds and says so; everything after that is offscreen editing.
        ledger.MarkRebuilt();
        Assert.False(ledger.RequiresRebuild());

        paragraph.SetAttribute("class", "offscreen");
        paragraph.AppendChild(document.CreateElement("span"));

        Assert.False(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Mutations_inside_a_document_fragment_require_no_rebuild()
    {
        var (document, _, _, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        var fragment = document.CreateDocumentFragment();
        var child = document.CreateElement("li");
        fragment.AppendChild(child);
        child.SetAttribute("class", "item");

        Assert.False(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void MarkRebuilt_clears_the_verdict_and_a_later_connected_write_sets_it_again()
    {
        var (document, _, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        paragraph.SetAttribute("class", "one");
        Assert.True(ledger.RequiresRebuild());

        ledger.MarkRebuilt();
        Assert.False(ledger.RequiresRebuild());

        paragraph.SetAttribute("class", "two");
        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void A_version_bump_the_ledger_never_saw_requires_a_rebuild()
    {
        // The backstop, and the case that makes the elision safe against publish paths this type
        // does not know about. An earlier subscriber that throws is the reachable way to produce
        // one: DomDocument.PublishMutation increments the version *before* invoking the event, so
        // the counter moves and the ledger's handler never runs.
        var (document, _, paragraph, _) = Tree();
        document.Mutated += _ => throw new InvalidOperationException("earlier subscriber");
        using var ledger = new RenderTreeInvalidation(document);

        Assert.Throws<InvalidOperationException>(() => paragraph.SetAttribute("class", "highlight"));

        Assert.Equal(0, ledger.ObservedMutations);
        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void A_same_value_write_never_reaches_the_ledger_at_all()
    {
        // Not this type's doing, and worth pinning because the roadmap named "a mutation that
        // changes nothing observable" as the cheapest demonstration of item #14's value: at the
        // value level Broiler.DOM already returns before publishing, so the version does not move
        // and no rebuild was ever going to happen. What is left for a ledger to elide is the
        // *detached* case, which is why that is the one it elides.
        var (document, _, paragraph, text) = Tree();
        paragraph.SetAttribute("class", "highlight");

        var versionBefore = document.Version;
        using var ledger = new RenderTreeInvalidation(document);

        paragraph.SetAttribute("class", "highlight");
        text.Data = "hello";

        Assert.Equal(versionBefore, document.Version);
        Assert.Equal(0, ledger.ObservedMutations);
        Assert.False(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Disposing_stops_observing()
    {
        var (document, _, paragraph, _) = Tree();
        var ledger = new RenderTreeInvalidation(document);
        ledger.Dispose();
        ledger.Dispose();

        paragraph.SetAttribute("class", "highlight");

        Assert.Equal(0, ledger.ObservedMutations);

        // The version still moved, so the backstop keeps the answer conservative even with the
        // subscription gone. A disposed ledger cannot elide anything, which is the right failure.
        Assert.True(ledger.RequiresRebuild());
    }

    /// <remarks>
    /// <c>Decisions</c> is process-wide, so this case is only sound while this class is the
    /// assembly's sole user of it: xUnit runs classes in parallel but a class's own cases serially.
    /// A second class that touches those counters needs both in one collection.
    /// </remarks>
    [Fact(Timeout = 600000)]
    public void Decisions_counts_rebuilds_required_and_elided()
    {
        var (document, _, paragraph, _) = Tree();
        RenderTreeInvalidation.Decisions.Reset();
        using var ledger = new RenderTreeInvalidation(document);

        var orphan = document.CreateElement("div");
        orphan.SetAttribute("class", "offscreen");
        Assert.True(ledger.TrySkipRebuild());

        paragraph.SetAttribute("class", "highlight");
        Assert.False(ledger.TrySkipRebuild());

        Assert.Equal(1, RenderTreeInvalidation.Decisions.Elided);
        Assert.Equal(1, RenderTreeInvalidation.Decisions.Required);

        // RequiresRebuild is a query and must not move the counters — otherwise every assertion
        // above about a count is really an assertion about how often this test looked.
        ledger.RequiresRebuild();
        Assert.Equal(1, RenderTreeInvalidation.Decisions.Elided);
        Assert.Equal(1, RenderTreeInvalidation.Decisions.Required);
    }

    // ---- The second rule: a connected attribute write nothing can see ----------------------
    //
    // Everything above is the connectivity rule, and every case below is a *connected* mutation —
    // the side the first slice could not elide at all. The stakes are higher here for the same
    // reason: the element is on screen, so a wrong "no rebuild" shows.

    /// <summary>A ledger over <see cref="Tree"/> whose render tree was built from <paramref name="css"/>.</summary>
    private static (DomDocument Document, DomElement Paragraph, RenderTreeInvalidation Ledger) Styled(string css)
    {
        var (document, _, paragraph, _) = Tree();
        var ledger = new RenderTreeInvalidation(document);
        ledger.MarkRebuilt(CascadeInvalidationSet.Build([new CssParser().ParseStyleSheet(css)]));
        return (document, paragraph, ledger);
    }

    [Fact(Timeout = 600000)]
    public void A_connected_write_of_an_attribute_no_sheet_can_see_requires_no_rebuild()
    {
        // The row the relayout profile sizes at 997.8 ms on the rule-heavy page.
        var (_, paragraph, ledger) = Styled(".a{color:red}");
        using var _held = ledger;

        paragraph.SetAttribute("data-relayout-probe", "1");

        Assert.False(ledger.RequiresRebuild());
        Assert.Equal(1, ledger.ElidedMutations);
    }

    [Fact(Timeout = 600000)]
    public void A_connected_write_of_an_attribute_a_selector_filters_on_requires_one()
    {
        var (_, paragraph, ledger) = Styled("[data-k=\"3\"]{color:red}");
        using var _held = ledger;

        paragraph.SetAttribute("data-k", "3");

        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Without_a_set_every_connected_write_still_rebuilds()
    {
        // The default, and what a container that never installs a set gets: the first slice's
        // behaviour exactly. A ledger that elided on "no stylesheet information" would elide most
        // aggressively in the case it knows least about.
        var (document, _, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        paragraph.SetAttribute("data-relayout-probe", "1");

        Assert.Null(ledger.CascadeDependencies);
        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void An_unstyled_class_token_is_elidable_and_a_styled_one_is_not()
    {
        var (_, paragraph, ledger) = Styled(".styled{color:red}");
        using var _held = ledger;

        paragraph.SetAttribute("class", "unstyled");
        Assert.False(ledger.RequiresRebuild());

        paragraph.SetAttribute("class", "unstyled styled");
        Assert.True(ledger.RequiresRebuild());
    }

    [Theory]
    // Names the cascade may not mention and box construction reads anyway. `id` is the one that
    // looks most elidable: no #id selector means no cascade dependency, and the box tree still
    // carries it for anchor resolution and the PDF link map.
    [InlineData("id")]
    [InlineData("src")]
    [InlineData("colspan")]
    [InlineData("style")]
    [InlineData("hidden")]
    // The engine's own channel into the layout engine, inside the otherwise inert data-* namespace.
    [InlineData("data-broiler-scroll-top")]
    public void An_attribute_the_box_tree_reads_requires_a_rebuild_whatever_the_sheet_says(string name)
    {
        var (_, paragraph, ledger) = Styled(".a{color:red}");
        using var _held = ledger;

        paragraph.SetAttribute(name, "1");

        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Nothing_inside_an_svg_subtree_is_elidable()
    {
        // FragmentTreeBuilder serializes an inline <svg> subtree back to markup attribute by
        // attribute and hands it to a renderer with its own idea of what class and data-* mean, so
        // the allow-list — a statement about this engine's HTML box construction — does not apply.
        var (document, _, paragraph, _) = Tree();
        var svg = document.CreateElementNS(DomNamespaces.Svg, "svg");
        var rect = document.CreateElementNS(DomNamespaces.Svg, "rect");
        svg.AppendChild(rect);
        paragraph.AppendChild(svg);

        using var ledger = new RenderTreeInvalidation(document);
        ledger.MarkRebuilt(CascadeInvalidationSet.Build([new CssParser().ParseStyleSheet(".a{color:red}")]));

        rect.SetAttribute("data-relayout-probe", "1");

        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void A_namespaced_attribute_is_never_elidable()
    {
        var (document, _, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);
        ledger.MarkRebuilt(CascadeInvalidationSet.Build([new CssParser().ParseStyleSheet(".a{color:red}")]));

        paragraph.SetAttributeNS("http://www.w3.org/1999/xlink", "xlink:data-probe", "1");

        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void Child_list_and_text_edits_are_not_reached_by_the_attribute_rule()
    {
        // The set answers a question about attributes, and the classification must not let it
        // answer one about structure: a sheet that mentions nothing still has to rebuild for an
        // inserted node or a changed text run.
        var (document, _, paragraph, text) = Tree();
        using var ledger = new RenderTreeInvalidation(document);
        ledger.MarkRebuilt(CascadeInvalidationSet.Build([new CssParser().ParseStyleSheet(".a{color:red}")]));

        paragraph.AppendChild(document.CreateElement("span"));
        Assert.True(ledger.RequiresRebuild());

        ledger.MarkRebuilt();
        text.Data = "changed";
        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void A_sheet_the_scanner_gave_up_on_elides_nothing()
    {
        var (_, paragraph, ledger) = Styled(".a\\.b{color:red}");
        using var _held = ledger;

        Assert.True(ledger.CascadeDependencies!.IsConservative);

        paragraph.SetAttribute("data-relayout-probe", "1");

        Assert.True(ledger.RequiresRebuild());
    }

    [Fact(Timeout = 600000)]
    public void The_installed_set_describes_the_tree_that_was_built()
    {
        // MarkRebuilt(set) is one call because the two facts are one fact. A mutation classified
        // before the install is classified against whatever the previous build knew, which is the
        // stylesheet set the tree on screen was actually built from.
        var (document, _, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        paragraph.SetAttribute("data-relayout-probe", "1");
        Assert.True(ledger.RequiresRebuild());

        ledger.MarkRebuilt(CascadeInvalidationSet.Build([new CssParser().ParseStyleSheet(".a{color:red}")]));
        Assert.False(ledger.RequiresRebuild());

        paragraph.SetAttribute("data-relayout-probe", "2");
        Assert.False(ledger.RequiresRebuild());

        // A parameterless MarkRebuilt keeps the set: a rebuild is not a reason to forget what the
        // sheets say, and clearing it there would silently turn the elision off after the first one.
        ledger.MarkRebuilt();
        Assert.NotNull(ledger.CascadeDependencies);
    }

    [Fact(Timeout = 600000)]
    public void TrySkipRebuild_marks_current_only_when_it_skips()
    {
        var (document, _, paragraph, _) = Tree();
        using var ledger = new RenderTreeInvalidation(document);

        var orphan = document.CreateElement("div");
        orphan.SetAttribute("class", "offscreen");

        // Skipping marks current, so a second call has nothing left to account for.
        Assert.True(ledger.TrySkipRebuild());
        Assert.True(ledger.TrySkipRebuild());

        // Refusing does not mark current: the caller has not rebuilt yet, and a ledger that
        // cleared its own verdict here would let the next call skip the rebuild it just demanded.
        paragraph.SetAttribute("class", "highlight");
        Assert.False(ledger.TrySkipRebuild());
        Assert.False(ledger.TrySkipRebuild());

        ledger.MarkRebuilt();
        Assert.True(ledger.TrySkipRebuild());
    }
}
