using System;
using System.Threading;
using Broiler.Dom;

namespace Broiler.Layout.Engine;

/// <summary>
/// Decides whether a mutated <see cref="DomDocument"/> actually requires the render tree to be
/// rebuilt — the first increment of multithreading roadmap item #14, "layout dirty bits".
/// </summary>
/// <remarks>
/// <para>
/// <b>What this replaces.</b> <c>HtmlContainerInt</c> holds a bound document and a copy of its
/// <see cref="DomDocument.Version"/>, and rebuilds whenever the two differ:
/// <c>BuildBoundDocument</c> disposes the render tree and regenerates it — box tree and full
/// cascade — before the layout pass runs at all. The relayout profile measured that rebuild at
/// <b>60–97% of a relayout</b>, and 97.1% on the rule-heavy corpus page, so the version counter is
/// the most expensive boolean in the engine: it says "something changed" and nothing else.
/// </para>
/// <para>
/// <b>The signal it needed was already there.</b> The roadmap recorded item #14 as blocked on
/// giving <c>Broiler.DOM</c> "a way to say <em>what</em> changed", and therefore as starting with a
/// submodule change. It does not: <see cref="DomDocument.Mutated"/> has published a typed
/// <see cref="DomMutationRecord"/> — type, target node, added and removed nodes, attribute name,
/// old and new value — for as long as <c>MutationObserver</c> has worked, which is to say since
/// before the item was written. What was missing is a consumer: the container subscribed to none of
/// it and read the counter that the same call increments. This type is that consumer.
/// </para>
/// <para>
/// <b>What it elides today, and why that is provable rather than heuristic.</b> One rule: a record
/// whose target does not hang off the bound document cannot have changed anything the render tree
/// shows, because the box tree is built from that document's tree and nothing else. Building a
/// subtree with <c>createElement</c> and <c>appendChild</c> before inserting it, populating a
/// <c>DocumentFragment</c>, filling a <c>&lt;template&gt;</c>'s inert contents, editing a subtree
/// that has been detached for the duration — every one of those bumps the version once per node
/// touched today, and every one of them costs a full rebuild and re-cascade at the next layout.
/// The insertion that follows is a mutation on a <em>connected</em> parent and still rebuilds, so
/// nothing about the visible result changes; what goes away is the rebuild for the offscreen half.
/// </para>
/// <para>
/// <b>The second rule: a connected attribute write no stylesheet and no box can see.</b> Given a
/// <see cref="CascadeInvalidationSet"/> — which the container installs with each rebuild, describing
/// the sheets that rebuild used — an attribute write is elided when <em>both</em> halves of the
/// engine are blind to it. The cascade half is the set's question: could any rule match differently,
/// or any <c>attr()</c> resolve differently, if this attribute changed. The box half is
/// <see cref="IsInertToBoxConstruction"/>: the box tree reads attributes for its own reasons, none of
/// which go through a selector, so the set's answer alone is not enough. This is the row the relayout
/// profile sizes at <b>997.8 ms on the rule-heavy page</b> — one <c>data-*</c> write that nothing in
/// the document can reach, paid for with a whole-document re-cascade.
/// </para>
/// <para>
/// <b>What it still deliberately does not elide.</b> Anything connected that is not that case:
/// mutations to elements that produce no boxes (a <c>&lt;meta&gt;</c>, a <c>&lt;title&gt;</c>),
/// character-data and child-list edits anywhere in the page, and every attribute the box tree reads
/// directly. Narrowing those needs a <em>scoped</em> rebuild rather than a skipped one — the rest of
/// item #14 after this — and guessing at it here would be trading a measured ceiling for a wrong
/// render.
/// </para>
/// <para>
/// <b>The conservative direction is the safe one, and there is a backstop for the case this cannot
/// see.</b> Every classification failure has to fall towards "rebuild". So the ledger also records
/// the document version it observed each record at: if the version has moved further than the
/// records account for — a publish path that bumps the counter without reaching a subscriber, an
/// earlier subscriber that threw before this one ran, a document mutated before it was bound — the
/// answer is unconditionally "rebuild", which is exactly the behaviour that was there before. An
/// elision is therefore only possible when the ledger can account for every version bump since the
/// last build.
/// </para>
/// <para>
/// <b>Why it lives here and is called from there.</b> The only caller is <c>HtmlContainerInt</c>,
/// in the <c>Broiler.HTML</c> submodule. The type is on this side of the line for the reason
/// <c>CLAUDE.md</c> gives and <see cref="CssStyleRecalc"/> and
/// <c>Broiler.Layout.IR.TileParallelReplay</c> already follow: a submodule patch carrying a whole
/// feature breaks the main repository's build the moment the submodule tree is reverted, and the
/// tests and harness below name this type. The submodule half is a field and three call sites.
/// </para>
/// <para>
/// <b>Thread safety.</b> Records arrive on whatever thread mutated the document and the decision is
/// read on the layout thread, so the state is behind a lock. It is a handful of field writes per
/// mutation against a rebuild measured in hundreds of milliseconds. Item #15 makes the DOM
/// single-threaded in the hosts this repository ships, which makes the lock uncontended rather than
/// unnecessary — a <c>MutationObserver</c> callback is not the only thing that can publish.
/// </para>
/// </remarks>
public sealed class RenderTreeInvalidation : IDisposable
{
    private readonly DomDocument _document;
    private readonly object _gate = new();

    /// <summary>
    /// What the stylesheets behind the current render tree can see of an element's attributes.
    /// Null until a rebuild installs one, and null means "assume everything matters".
    /// </summary>
    /// <remarks>
    /// Volatile rather than lock-guarded on the read path: classification walks the mutated node's
    /// parent chain, and doing that inside <see cref="_gate"/> would put a tree walk in the critical
    /// section that the rest of this type keeps to a handful of field writes. The field is written
    /// under the lock, so a reader sees either the previous set or the new one — and both are
    /// answers about a tree that was standing, which is what the version backstop is for.
    /// </remarks>
    private volatile CascadeInvalidationSet? _cascadeDependencies;

    /// <summary>The document version as of the last record this ledger observed.</summary>
    private ulong _accountedVersion;

    /// <summary>Whether any record since the last build could have changed the render tree.</summary>
    private bool _renderAffected;

    private long _observedMutations;
    private long _elidedMutations;
    private bool _disposed;

    /// <summary>
    /// Starts observing <paramref name="document"/>. The caller is expected to build the render
    /// tree immediately afterwards; the ledger takes the document's current version as the state
    /// that build reflects.
    /// </summary>
    public RenderTreeInvalidation(DomDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _accountedVersion = document.Version;
        document.Mutated += OnMutated;
    }

    /// <summary>Records classified as reaching the render tree, since construction.</summary>
    public long RenderAffectingMutations
    {
        get { lock (_gate) return _observedMutations - _elidedMutations; }
    }

    /// <summary>Records classified as unable to reach the render tree, since construction.</summary>
    public long ElidedMutations
    {
        get { lock (_gate) return _elidedMutations; }
    }

    /// <summary>Every record this ledger has seen, since construction.</summary>
    public long ObservedMutations
    {
        get { lock (_gate) return _observedMutations; }
    }

    /// <summary>
    /// Whether the render tree has to be rebuilt to reflect everything published since the last
    /// <see cref="MarkRebuilt()"/>. <see langword="false"/> only when every version bump since then is
    /// accounted for by a record that cannot have changed what the tree shows.
    /// </summary>
    /// <remarks>
    /// A pure query, for tests and diagnostics. The render path calls
    /// <see cref="TrySkipRebuild"/> instead, which cannot be interleaved with an arriving mutation.
    /// </remarks>
    public bool RequiresRebuild()
    {
        lock (_gate)
            return RequiresRebuildLocked();
    }

    /// <summary>
    /// The render path's decision: <see langword="true"/> when the rebuild can be skipped, and in
    /// that case the ledger is marked current as part of the same operation.
    /// </summary>
    /// <remarks>
    /// Check and mark have to be one step. Split into
    /// <c>if (!RequiresRebuild()) { …; MarkRebuilt(); }</c> they leave a window in which a mutation
    /// arriving between the two sets the flag and has it cleared unread — a dropped invalidation,
    /// which is a stale render rather than a slow one. Item #15's single-threaded event loop makes
    /// that window unreachable in the hosts this repository ships; it is closed here anyway,
    /// because "unreachable today" is not a property the render path should depend on and the fix
    /// costs one lock instead of two.
    /// </remarks>
    public bool TrySkipRebuild()
    {
        bool skip;
        lock (_gate)
        {
            skip = Elision.Enabled && !RequiresRebuildLocked();
            if (skip)
                MarkRebuiltLocked();
        }

        if (skip)
            Interlocked.Increment(ref _rebuildsElided);
        else
            Interlocked.Increment(ref _rebuildsRequired);

        return skip;
    }

    /// <summary>
    /// Declares the render tree current with the document as it stands. Called after a rebuild;
    /// <see cref="TrySkipRebuild"/> does it for itself when it skips one.
    /// </summary>
    public void MarkRebuilt()
    {
        lock (_gate)
            MarkRebuiltLocked();
    }

    /// <summary>
    /// Declares the render tree current and installs the stylesheet dependencies it was built from.
    /// </summary>
    /// <param name="dependencies">
    /// The set describing the sheets that rebuild cascaded, or <see langword="null"/> to go back to
    /// assuming every attribute matters.
    /// </param>
    /// <remarks>
    /// Installing the set is part of marking the rebuild rather than a separate setter, because the
    /// two are one fact: the set describes the tree standing right now. Installed independently it
    /// could describe a stylesheet set the tree does not reflect — a sheet that arrived after the
    /// build — and mutations would then be classified against rules the page is not showing.
    /// </remarks>
    public void MarkRebuilt(CascadeInvalidationSet? dependencies)
    {
        lock (_gate)
        {
            _cascadeDependencies = dependencies;
            MarkRebuiltLocked();
        }
    }

    /// <summary>
    /// The stylesheet dependencies of the render tree as last built, for tests and diagnostics.
    /// </summary>
    public CascadeInvalidationSet? CascadeDependencies
    {
        get { lock (_gate) return _cascadeDependencies; }
    }

    private bool RequiresRebuildLocked() =>
        // The version moving further than the records account for is the one case that has to
        // rebuild regardless of what the records said: something changed that this ledger did not
        // see, and "did not see" is not "cannot matter".
        _renderAffected || _accountedVersion != _document.Version;

    private void MarkRebuiltLocked()
    {
        _renderAffected = false;
        _accountedVersion = _document.Version;
    }

    /// <summary>Stops observing the document. Idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _document.Mutated -= OnMutated;
    }

    private void OnMutated(DomMutationRecord record)
    {
        var reaches = ReachesRenderTree(record, _document, _cascadeDependencies);

        lock (_gate)
        {
            _observedMutations++;
            if (reaches)
                _renderAffected = true;
            else
                _elidedMutations++;

            _accountedVersion = _document.Version;
        }
    }

    /// <summary>
    /// Whether <paramref name="record"/> could have changed what a render tree built from
    /// <paramref name="document"/> shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first test is connectivity, and it decides every record on its own: the box tree is
    /// generated by walking <paramref name="document"/>, so a node whose root is anything else — a
    /// <c>DocumentFragment</c>, a <c>&lt;template&gt;</c>'s contents, an orphaned subtree, another
    /// document — contributes no box, and neither do its descendants.
    /// </para>
    /// <para>
    /// A connected record is then decided by its type. Only an attribute write can be elided, and
    /// only by <see cref="AttributeReachesRenderTree"/> — a child-list or character-data edit inside
    /// the page changes the tree's shape or a text node's measured size, and neither question has an
    /// answer cheaper than the rebuild that is already there.
    /// </para>
    /// <para>
    /// A <see cref="DomMutationType.ChildList"/> record names the <em>parent</em> as its target,
    /// which is what makes the test sound for insertions and removals alike: nodes added to a
    /// detached parent are themselves detached, and a node removed from a connected parent is
    /// reported against that connected parent, not against the node that just left the tree. The
    /// same holds for a node moved out of the document — the removal record on the old, connected
    /// parent is what forces the rebuild, and the <see cref="DomMutationType.Adoption"/> record that
    /// follows it names a node that is by then detached.
    /// </para>
    /// <para>
    /// Records from a sub-document — an <c>&lt;iframe&gt;</c>'s content document, reached through
    /// the container's content-document resolver — never arrive here at all: they are published on
    /// their own <see cref="DomDocument"/>, which is not the one this ledger observes and not the
    /// one whose version the container compares. That is unchanged by this type, and it is the same
    /// gap it was before.
    /// </para>
    /// </remarks>
    private static bool ReachesRenderTree(
        DomMutationRecord record,
        DomDocument document,
        CascadeInvalidationSet? dependencies)
    {
        var target = record?.Target;
        if (target is null)
            return true;

        if (!ReferenceEquals(target.GetRootNode(), document))
            return false;

        return record!.Type != DomMutationType.Attributes
            || AttributeReachesRenderTree(record, dependencies);
    }

    /// <summary>
    /// Whether an attribute write on a <em>connected</em> element could have changed what the render
    /// tree shows: the second rule, and the one that needs to know about the stylesheets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two independent questions, both of which have to answer "no". The cascade's is
    /// <see cref="CascadeInvalidationSet.AffectsStyle"/>. The box tree's is
    /// <see cref="IsInertToBoxConstruction"/>, and it exists because box construction reads
    /// attributes without going through a selector at all — <c>src</c>, <c>href</c>, <c>colspan</c>,
    /// the presentational attributes <c>DomParser.TranslateAttributes</c> maps onto box properties.
    /// A set built from the stylesheets knows nothing about any of that, so its answer alone would
    /// elide a <c>&lt;td colspan&gt;</c> write on a page whose sheet never mentions <c>colspan</c>.
    /// </para>
    /// <para>
    /// A namespaced attribute (<c>xlink:href</c>) is never elided: the set files unprefixed names,
    /// so comparing a qualified name against it would be comparing two different things.
    /// </para>
    /// </remarks>
    private static bool AttributeReachesRenderTree(DomMutationRecord record, CascadeInvalidationSet? dependencies)
    {
        if (dependencies is null)
            return true;

        if (record.AttributeNamespace is not null)
            return true;

        var name = record.AttributeName;
        if (string.IsNullOrEmpty(name))
            return true;

        if (!IsInertToBoxConstruction(name))
            return true;

        if (IsInForeignContent(record.Target))
            return true;

        return dependencies.AffectsStyle(name, record.OldValue, record.NewValue);
    }

    /// <summary>
    /// Whether <paramref name="attributeName"/> is one that box construction and layout never read
    /// for a reason of their own — that is, one the cascade is the only consumer of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An allow-list, not a deny-list, and the direction is the point.</b> Listing the attributes
    /// the box tree <em>does</em> read would put every attribute nobody thought of on the elidable
    /// side, and the cost of missing one is a stale page. So only names established to be inert are
    /// here, and everything else — known or not — rebuilds.
    /// </para>
    /// <para>
    /// <b><c>class</c></b> is read by the cascade and by nothing else in this engine: no box
    /// construction path, no layout path and no fragment-tree path consults it, which is what makes
    /// the token-level question <see cref="CascadeInvalidationSet"/> answers the whole question for
    /// it.
    /// </para>
    /// <para>
    /// <b><c>data-*</c>, except <c>data-broiler-*</c>.</b> The custom-data namespace is inert to the
    /// engine by definition — and then the engine helped itself to part of it. <c>data-broiler-*</c>
    /// is how the script bridge tells the layout engine things it has no other channel for: scroll
    /// offsets (<c>data-broiler-scroll-top</c>), top-layer order, dialog backdrops, an iframe's live
    /// document, anchor-induced containing blocks, shadow parts. Those drive rendering directly, so
    /// the prefix is carved out. Eliding it would be eliding the engine's own messages to itself.
    /// </para>
    /// <para>
    /// <b><c>aria-*</c> and <c>role</c></b> are consumed by no rendering path here. They are listed
    /// because they are, with <c>class</c>, what scripts write most often without meaning to change
    /// the picture.
    /// </para>
    /// <para>
    /// <b><c>id</c> is deliberately absent</b>, and it is the case that looks elidable and is not.
    /// The box tree carries it — <c>HtmlContainerInt</c> reads <c>id</c> off boxes to build the link
    /// map a PDF export writes, and <c>DomUtils</c> resolves a fragment anchor by scanning boxes for
    /// it. An elided <c>id</c> write leaves those answering from the old tree, which is a wrong
    /// answer rather than a slow one. <see cref="CascadeInvalidationSet"/> still models ids, because
    /// the cascade's half of the question is a real one; this is the other half saying no.
    /// </para>
    /// </remarks>
    private static bool IsInertToBoxConstruction(string attributeName) =>
        attributeName.Equals("class", StringComparison.OrdinalIgnoreCase)
        || attributeName.Equals("role", StringComparison.OrdinalIgnoreCase)
        || attributeName.StartsWith("aria-", StringComparison.OrdinalIgnoreCase)
        || (attributeName.StartsWith("data-", StringComparison.OrdinalIgnoreCase)
            && !attributeName.StartsWith("data-broiler-", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether <paramref name="node"/> is inside an SVG or MathML subtree, where no attribute is
    /// inert.
    /// </summary>
    /// <remarks>
    /// Inline <c>&lt;svg&gt;</c> is not laid out attribute by attribute: <c>FragmentTreeBuilder</c>
    /// serializes the whole subtree back to markup — <em>every</em> attribute of every box, by name
    /// — and hands the string to the SVG renderer, which has its own view of what a
    /// <c>class</c> or a <c>data-*</c> means. The allow-list above is a statement about this
    /// engine's HTML box construction and does not transfer to a renderer that re-parses the markup,
    /// so foreign content is excluded wholesale rather than attribute by attribute.
    /// </remarks>
    private static bool IsInForeignContent(DomNode? node)
    {
        for (var current = node; current is not null; current = current.ParentNode)
        {
            if (current is not DomElement element)
                continue;

            var ns = element.NamespaceUri;
            if (ns is not null
                && !ns.Equals(DomNamespaces.Html, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The switch that turns every elision off, leaving the version compare that was there before
    /// this type existed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document's global exit gate asks every parallel path for a <c>--threads 1</c> equivalent
    /// that reproduces the sequential output exactly. This is that, for a path whose parallelism is
    /// <em>skipping work</em>: with it off, every version bump rebuilds, so a page rendered both ways
    /// is a direct test of the claim the classification makes — that what it skipped could not have
    /// changed the picture. <c>--relayout-parity</c> is the harness that does it, and a stale render
    /// is exactly the failure it exists to catch.
    /// </para>
    /// <para>
    /// It is also the switch to reach for from outside: <c>BROILER_RENDER_TREE_ELISION=0</c> in the
    /// environment restores the old behaviour without a rebuild of anything, which is what a bug
    /// suspected to be a stale render should be bisected with first.
    /// </para>
    /// </remarks>
    public static class Elision
    {
        private static bool _enabled = Environment.GetEnvironmentVariable("BROILER_RENDER_TREE_ELISION") is not "0";

        /// <summary>Whether <see cref="TrySkipRebuild"/> may answer "skip". Defaults to on.</summary>
        public static bool Enabled
        {
            get => Volatile.Read(ref _enabled);
            set => Volatile.Write(ref _enabled, value);
        }
    }

    private static long _rebuildsRequired;
    private static long _rebuildsElided;

    /// <summary>
    /// Process-wide decision counts, for the relayout harness and for tests that need to assert a
    /// rebuild did <em>not</em> happen — which is otherwise invisible, being the absence of work.
    /// </summary>
    /// <remarks>
    /// Counted per decision rather than per record: the container's own version fast-path means
    /// <see cref="TrySkipRebuild"/> is consulted once per batch of mutations, not once per layout
    /// call and not once per mutation, so these are counts of "relayouts that would have rebuilt".
    /// <see cref="RequiresRebuild"/> deliberately does not count — it is a query, and a diagnostic
    /// that a test can move by observing it is not a diagnostic.
    /// </remarks>
    public static class Decisions
    {
        /// <summary>Decisions that required a rebuild.</summary>
        public static long Required => Interlocked.Read(ref _rebuildsRequired);

        /// <summary>Decisions that skipped one.</summary>
        public static long Elided => Interlocked.Read(ref _rebuildsElided);

        /// <summary>Zeroes both counts.</summary>
        public static void Reset()
        {
            Interlocked.Exchange(ref _rebuildsRequired, 0);
            Interlocked.Exchange(ref _rebuildsElided, 0);
        }
    }
}
