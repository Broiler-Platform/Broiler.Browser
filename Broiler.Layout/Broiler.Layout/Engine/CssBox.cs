using Broiler.CSS;
using Broiler.Graphics;
using Broiler.Layout.Diagnostics;
using System.Drawing;
using System.Globalization;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    private CssBox _parentBox;
    protected object _htmlContainer;
    private ILayoutEnvironment _layoutEnvironment;
    private ReadOnlyMemory<char> _text;

    internal bool _tableFixed;

    /// <summary>
    /// RF-BRIDGE-1b Track 3.2: for a nested-viewport-root box
    /// (<see cref="IsNestedViewportRoot"/>), the frame content-box size (the
    /// sub-viewport dimensions). Stored explicitly because the box's used
    /// <see cref="Size"/> is transiently 0 while its own subtree lays out
    /// (block heights resolve bottom-up, after out-of-flow descendants are placed),
    /// so a fixed descendant reading <c>Size.Height</c> for its viewport basis would
    /// see 0. The origin still tracks the live <see cref="Location"/> so the
    /// LayoutSubdocument translate composes it onto the frame content origin.
    /// </summary>
    internal SizeF NestedViewportSize;

    /// <summary>
    /// The canonical <see cref="Dom.DomElement"/> this box was built from,
    /// when the box tree was generated from a <see cref="Dom.DomDocument"/>
    /// (the <c>SetDocument</c> path). <c>null</c> on the legacy HTML-string parse path.
    /// Phase 5 uses this to run the shared <c>Broiler.CSS.Dom</c> cascade on the
    /// canonical element and project the result onto this box.
    /// </summary>
    internal Dom.DomElement SourceElement { get; set; }

    /// <summary>
    /// When the block-inside-inline correction (CSS2.1 §9.2.1.1) splits a
    /// positioned inline element into sibling anonymous blocks, the hoisted
    /// blocks lose their parent–child relationship with the positioned
    /// inline in the box tree.  This field links back to the original
    /// positioned ancestor so that <see cref="FindPositionedContainingBlock"/>
    /// can still find it.
    /// </summary>
    internal CssBox SplitPositionedAncestor { get; set; }

    /// <summary>
    /// RF-BRIDGE-1b Track 3.2: marks a materialised nested-browsing-context root
    /// (the <c>#subdoc-root</c> box of an <c>&lt;iframe&gt;</c>/<c>&lt;object&gt;</c>
    /// sub-document, positioned and sized to its frame's content box by
    /// <see cref="LayoutSubdocument"/>). Such a box acts as the <em>sub-viewport</em>
    /// (initial containing block) for its subtree: a <c>position:fixed</c> descendant
    /// anchors to it, and an absolutely-positioned descendant with no positioned
    /// ancestor resolves against it, rather than against the top-level viewport.
    /// Only ever set on boxes that exist in the shared-geometry render document (never
    /// in the paint document), so it cannot affect the top-level document layout or
    /// painting.
    /// </summary>
    internal bool IsNestedViewportRoot { get; set; }

    /// <summary>
    /// RF-BRIDGE-1b / HtmlBridge Phase 4 (P4.4b): marks the box that roots a nested
    /// browsing context (the sub-viewport of an <c>&lt;iframe&gt;</c>/<c>&lt;object&gt;</c>/
    /// <c>&lt;frame&gt;</c> sub-document). <see cref="LayoutNestedBrowsingContexts"/> keys off
    /// this flag — set by the DOM→box builder — instead of a <c>#subdoc-root</c> fake tag name,
    /// so the sub-document can be projected either from an in-tree materialised subtree (legacy)
    /// or synthesised from a referenced content <see cref="Dom.DomDocument"/> (Phase 4 sever).
    /// The box is placed and sized to its frame's content box by <see cref="LayoutSubdocument"/>,
    /// which also promotes it to an <see cref="IsNestedViewportRoot"/> sub-viewport.
    /// </summary>
    internal bool IsNestedBrowsingContextRoot { get; set; }

    /// <summary>
    /// When the block-inside-inline correction splits a positioned inline
    /// element, the original box loses its children to anonymous "left" and
    /// "right" copies.  This list tracks those copies so that
    /// <see cref="GetInlineBoundingBox"/> can compute the bounding box
    /// across <em>all</em> fragments, not just the (now-empty) original.
    /// Only populated on the original box that serves as
    /// <see cref="SplitPositionedAncestor"/> for hoisted descendants.
    /// </summary>
    internal List<CssBox> SplitFragments { get; private set; }

    /// <summary>
    /// Register a box as a fragment of this positioned inline that was
    /// created during the block-inside-inline split.
    /// </summary>
    internal void AddSplitFragment(CssBox fragment)
    {
        SplitFragments ??= [];
        SplitFragments.Add(fragment);
    }

    protected bool _wordsSizeMeasured;
    private CssBox _listItemBox;

    /// <summary>
    /// The generated list-item marker box (bullet / number glyph) for a
    /// <c>display:list-item</c> element, laid out by <see cref="CreateListItemBox"/>.
    /// Exposed so the fragment builder can emit it into the paint tree — the marker
    /// is not a member of <see cref="Boxes"/>, so it is otherwise never painted.
    /// <see langword="null"/> when the element is not a list item or its
    /// <c>list-style-type</c> is <c>none</c>.
    /// </summary>
    internal CssBox? ListItemMarkerBox => _listItemBox;
    private List<ILayoutImageLoader?>? _backgroundImageLoadHandlers;
    private bool _backgroundImagesInitialized;

    /// <summary>
    /// Background-layer completions the image prefetch (multithreading item #8) captured on a worker,
    /// waiting to be applied on the layout thread. Null on every box the prefetch did not claim, which
    /// is every box when the prefetch is off.
    /// </summary>
    private List<DeferredImageLoad>? _deferredBackgroundLoads;

    internal Dictionary<string, string> CustomProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal void SetCustomProperty(string propertyName, string value)
    {
        if (string.IsNullOrEmpty(propertyName))
            return;

        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "initial", StringComparison.OrdinalIgnoreCase))
        {
            CustomProperties[propertyName] = InvalidCustomPropertySentinel;
            return;
        }

        if (string.Equals(trimmed, "inherit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "unset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "revert", StringComparison.OrdinalIgnoreCase))
        {
            CustomProperties.Remove(propertyName);
            return;
        }

        CustomProperties[propertyName] = value;
    }

    /// <summary>
    /// Returns the loaded background image handle, or null if no background image is loaded.
    /// Used by <c>FragmentTreeBuilder</c> to capture background images for the new paint path.
    /// </summary>
    internal object? LoadedBackgroundImage
    {
        get
        {
            if (_backgroundImageLoadHandlers == null || _backgroundImageLoadHandlers.Count == 0)
                return null;

            if (_backgroundImageLoadHandlers.Count == 1)
                return _backgroundImageLoadHandlers[0]?.Image;

            var layers = new object?[_backgroundImageLoadHandlers.Count];
            bool hasImage = false;

            for (int i = 0; i < _backgroundImageLoadHandlers.Count; i++)
            {
                var image = _backgroundImageLoadHandlers[i]?.Image;
                layers[i] = image;
                hasImage |= image != null;
            }

            return hasImage ? layers : null;
        }
    }

    public CssBox(CssBox parentBox, HtmlTag tag, Uri baseUrl)
    {
        if (parentBox != null)
        {
            _parentBox = parentBox;
            _parentBox.Boxes.Add(this);
        }

        HtmlTag = tag;
        BaseUrl = baseUrl;
    }

    /// <summary>
    /// Opaque host-container handle (the renderer's <c>IHtmlContainerInt</c>),
    /// kept as the box→container link for renderer/serialization code that reads
    /// it externally (cast back to the concrete type). Layout itself no longer
    /// uses it — host services flow through <see cref="LayoutEnvironment"/>.
    /// Typed as <see cref="object"/> so layout stays free of the renderer's
    /// container interface (roadmap §4, Phase 4).
    /// </summary>
    internal object ContainerInt
    {
        get { return _htmlContainer ??= _parentBox?.ContainerInt; }
        set { _htmlContainer = value; }
    }

    private bool? _documentQuirksMode;

    /// <summary>
    /// Whether the document being laid out is in quirks mode. Captured once —
    /// from <see cref="Dom.DocumentModeContext"/>, which the HTML parser publishes
    /// on the parse thread — onto the tree root the first time layout consults it,
    /// then read from the root by every box. Caching on the root makes the value
    /// survive re-layout passes (which may run on a different thread, where the
    /// thread-local would have reset) without threading it through the box builder.
    /// </summary>
    internal bool DocumentQuirksMode
    {
        get
        {
            var root = this;

            while (root._parentBox != null)
                root = root._parentBox;

            root._documentQuirksMode ??= DocumentModeContext.CurrentQuirksMode;
            return root._documentQuirksMode.Value;
        }
    }

    /// <summary>
    /// Host-injected layout environment (see <see cref="ILayoutEnvironment"/>),
    /// resolved up the box tree like <see cref="ContainerInt"/>. Set on the root
    /// box at the start of each layout pass; supplies the initial-containing-block
    /// inputs (<see cref="ILayoutEnvironment.ViewportSize"/>,
    /// <see cref="ILayoutEnvironment.RootLocation"/>) and the
    /// <see cref="ILayoutEnvironment.ActualSize"/> output that layout reads/writes
    /// outside the <c>g</c>-threaded methods (roadmap §5, Phase 3).
    /// </summary>
    internal ILayoutEnvironment LayoutEnvironment
    {
        get { return _layoutEnvironment ??= _parentBox?.LayoutEnvironment; }
        set { _layoutEnvironment = value; }
    }

    public CssBox ParentBox
    {
        get { return _parentBox; }
        set
        {
            _parentBox?.Boxes.Remove(this);
            _parentBox = value;

            if (value != null)
                _parentBox.Boxes.Add(this);
        }
    }

    public List<CssBox> Boxes { get; } = [];

    public override bool AvoidGeometryAntialias => LayoutEnvironment?.AvoidGeometryAntialias ?? false;

    protected override bool TryGetCustomPropertyValue(string propertyName, out string value)
    {
        if (CustomProperties.TryGetValue(propertyName, out value))
            return true;

        if (ParentBox != null)
            return ParentBox.TryGetCustomPropertyValue(propertyName, out value);

        value = string.Empty;
        return false;
    }

    public bool IsBrElement => HtmlTag != null && HtmlTag.Name.Equals("br", StringComparison.InvariantCultureIgnoreCase);

    public bool IsInline => (Display == CssConstants.Inline || Display == CssConstants.InlineBlock
        || Display == "inline-flex" || Display == "inline-grid") && !IsBrElement;
    
    // CSS Display 3 §2.5: `flow-root` is block-level (its <display-outside> is
    // `block`); the keyword only changes the <display-inside> to "block container
    // that establishes a new BFC". Everything block layout does applies to it
    // unchanged, so it belongs in this predicate — omitting it left such a box
    // neither block nor inline, and it laid out and painted nothing at all.
    public bool IsBlock => Display == CssConstants.Block || Display == "flex"
        || Display == "grid" || Display == "flow-root";
    
    public virtual bool IsClickable
    {
        get
        {
            if (HtmlTag == null)
                return false;

            // <a> links (without only an id anchor)
            if (HtmlTag.Name == HtmlConstants.A && !HtmlTag.HasAttribute("id"))
                return true;

            // <button> elements
            if (HtmlTag.Name.Equals("button", StringComparison.OrdinalIgnoreCase))
                return true;

            // <input type="submit|button|reset"> elements
            if (HtmlTag.Name.Equals("input", StringComparison.OrdinalIgnoreCase))
            {
                var inputType = HtmlTag.TryGetAttribute("type")?.ToLowerInvariant() ?? "text";
                if (inputType is "submit" or "button" or "reset")
                    return true;
            }

            return false;
        }
    }

    public virtual bool IsFixed
    {
        get
        {
            if (Position == CssConstants.Fixed)
                return true;

            if (ParentBox == null)
                return false;

            CssBox parent = this;

            while (!(parent.ParentBox == null || parent == parent.ParentBox))
            {
                parent = parent.ParentBox;

                if (parent.Position == CssConstants.Fixed)
                    return true;
            }

            return false;
        }
    }

    public virtual string HrefLink => GetAttribute(HtmlConstants.Href);

    public HtmlTag HtmlTag { get; }

    public bool IsImage => Words.Count == 1 && Words[0].IsImage;

    /// <summary>
    /// Whether this <c>inline-block</c>'s baseline is its bottom margin edge rather than the
    /// baseline of a line box inside it — CSS2.1 §10.8.1.
    /// </summary>
    /// <remarks>
    /// The spec gives an <c>inline-block</c> the baseline of its <em>last in-flow line box</em>,
    /// and falls back to the bottom margin edge in two cases: it has no in-flow line box, or its
    /// <c>overflow</c> is not <c>visible</c>. Both are common and neither was modelled — an empty
    /// <c>inline-block</c> and an inline <c>&lt;svg&gt;</c> (which the parser gives
    /// <c>overflow: hidden</c>) were left at the top of the line instead of standing on its
    /// baseline, so two of different heights came out top-aligned rather than bottom-aligned.
    /// <para>
    /// An <c>inline-block</c> that does have line boxes and clips nothing is deliberately not
    /// covered: its baseline comes from its own text, which this engine does not yet track, and
    /// leaving it at the flow position is the answer it has always given.
    /// </para>
    /// </remarks>
    internal bool UsesBottomMarginEdgeBaseline =>
        Display == CssConstants.InlineBlock
        && ((!string.IsNullOrEmpty(Overflow) && Overflow != CssConstants.Visible)
            || !HasInFlowLineContent(this));

    /// <summary>
    /// Whether <paramref name="box"/> lays any text out in a line box of its own or of an in-flow
    /// descendant — the "has in-flow line boxes" side of CSS2.1 §10.8.1's inline-block baseline.
    /// </summary>
    /// <remarks>
    /// It asks for a line box carrying a <em>word</em> rather than for
    /// <c>LineBoxes.Count == 0</c>, because an empty <c>inline-block</c> is still given one line
    /// box here — for its strut — so the count is never zero and the spec's own test never fired.
    /// The walk skips out-of-flow and undisplayed children, which contribute no line box to the
    /// box they are written in.
    /// </remarks>
    private static bool HasInFlowLineContent(CssBox box)
    {
        foreach (var line in box.LineBoxes)
        {
            if (line.Words.Count > 0)
                return true;
        }

        foreach (var child in box.Boxes)
        {
            if (child.Display == CssConstants.None
                || child.Position == CssConstants.Absolute
                || child.Position == CssConstants.Fixed)
            {
                continue;
            }

            if (HasInFlowLineContent(child))
                return true;
        }

        return false;
    }

    public ReadOnlyMemory<char> Text
    {
        get { return _text; }
        set
        {
            _text = value;
            Words.Clear();
        }
    }

    internal List<CssLineBox> LineBoxes { get; } = [];

    internal Dictionary<CssLineBox, RectangleF> Rectangles { get; } = [];

    internal List<CssRect> Words { get; } = [];

    internal CssRect FirstWord => Words[0];

    internal CssLineBox FirstHostingLineBox { get; set; }

    internal CssLineBox LastHostingLineBox { get; set; }

    /// <summary>
    /// The floats that shorten this block's line boxes, recorded by
    /// <see cref="CssLayoutEngine.CreateLineBoxes"/> for the pass it is running so that every
    /// line it flows can consult the same geometry. Null on a box that has not flowed lines.
    /// </summary>
    internal LineFloatBands LineFloatBands { get; set; }

    internal Uri BaseUrl { get; set; }

    public void PerformLayout(ILayoutEnvironment g)
    {
        try
        {
            // Multithreading item #8: a document's images are loaded and decoded one at a time,
            // inline, at whichever MeasureWordsSize first reaches each of them. Issue them all here
            // instead, concurrently, and join before any measurement happens — so the pass below
            // finds every image already loaded and does exactly what it did before. At the root
            // only: a nested layout (a table cell re-measuring, a subdocument) shares this
            // document's boxes, and re-walking them would find nothing pending but pay for the walk.
            if (ParentBox == null)
            {
                PrefetchDocumentImages(this, g);

                // The quirks-mode "tables inherit color from body" rule, which needs the whole
                // cascaded tree to answer — the body it names is the document's, and a table can
                // sit outside it or even before it. Root only, and a no-op in standards mode or
                // with no table in the document. See TablesInheritColorFromBodyQuirk.
                TablesInheritColorFromBodyQuirk.Apply(this);
            }

            // PROTOTYPE (BROILER_VERTICAL_FLOW): a vertical-writing-mode rotation
            // root lays its whole subtree out in a logical (horizontal) frame.
            // While that frame layout runs, a transposed box's physical
            // border/padding insets are read as LOGICAL (frame) insets so they
            // land on the correct axis after the rotation swaps width↔height
            // (PushVerticalFrameLayout / UsesLogicalFrameInsets). The flag is
            // cleared before the rotation and the later paint pass, which read
            // the box's authored physical borders.
            bool isVerticalRoot = VerticalFlowPrototype.Enabled
                && IsVerticalWritingMode(WritingMode)
                && (ParentBox == null || !IsVerticalWritingMode(ParentBox.WritingMode));

            if (isVerticalRoot)
                PushVerticalFrameLayout();

            try
            {
                PerformLayoutImp(g);
            }
            finally
            {
                if (isVerticalRoot)
                    PopVerticalFrameLayout();
            }

            // Once a vertical-writing-mode root and its whole subtree have been
            // laid out in the logical (horizontal) frame, rotate the result into
            // physical space.
            if (isVerticalRoot)
            {
                ApplyVerticalWritingModeFlow();
            }

            // RF-BRIDGE-1b Track 3.2: after the main document is laid out, lay out any
            // materialised nested browsing contexts (an <iframe>/<object>/<frame> whose
            // sub-document DOM is present as a #subdoc-root subtree, as it is on the
            // shared-geometry render document). Each subtree is placed at its frame's
            // content-box origin and sized to the content box, so a subframe element
            // reports real geometry composed into the main coordinate frame. This runs
            // only at the document root and only touches #subdoc-root subtrees, which are
            // absent from the paint document (that serialises the sub-document into the
            // frame's srcdoc and rasterises it separately), so it cannot affect painting.
            if (ParentBox == null)
                LayoutNestedBrowsingContexts(this, g);

            // Phase 5 item 3 (P5.8c): native CSS anchor-positioning placement. A root
            // post-pass — every anchor now has final geometry — gated off by default.
            // Runs before the fragment tree is built (that happens after PerformLayout).
            if (ParentBox == null && NativeAnchorPlacement.Enabled)
            {
                // Native scroll offsets first (P5.8d.2b scroll expansion), so anchor geometry
                // (and everything downstream) sees the scrolled content.
                RunScrollSimulation(this);
                // position:sticky pinning (P5.8d.2b sticky expansion): after scroll so it reads
                // the scrolled geometry; independent of the anchor pass that follows.
                RunStickyPositioning(this);
                RunNativeAnchorPlacement(this);
                // CSS2.1 §10.6.4 block-axis auto-margin centring for out-of-flow boxes whose used
                // height is only final now (content / intrinsic-keyword heights) — the inline axis and
                // definite heights are centred in-line during layout. Runs last so it sees the final
                // (scrolled / anchor-placed) geometry. Powers content-height modal <dialog> vertical
                // centring; inert unless such a box exists.
                CenterOutOfFlowBlockAxis(this);
            }

            // Multithreading item #13: count how much of the finished tree sits under a subtree
            // the item proposes to claim. One walk at the root, only while the layout-composition
            // profiler is running; LayoutWorkTrace.Enabled is false everywhere else.
            if (ParentBox == null && LayoutWorkTrace.Enabled)
                LayoutTreeCensus.Record(this);
        }
        catch (Exception ex)
        {
            LayoutEnvironment.ReportLayoutError("Exception in box layout", ex);
        }
    }

    private static void LayoutNestedBrowsingContexts(CssBox box, ILayoutEnvironment g)
    {
        foreach (var child in box.Boxes)
        {
            if (child.IsNestedBrowsingContextRoot)
            {
                LayoutSubdocument(box, child, g);
            }

            // Descend either way so nested frames (a subframe that itself embeds a frame)
            // are laid out relative to their now-positioned parent subdocument.
            LayoutNestedBrowsingContexts(child, g);
        }
    }

    /// <summary>
    /// Lays out a materialised sub-document subtree (<paramref name="subdocRoot"/>, a
    /// <c>#subdoc-root</c> box) inside its containing frame (<paramref name="frame"/>):
    /// positions it at the frame's content-box origin, sizes it to the content box (the
    /// sub-viewport), and runs the normal block layout so its descendants report geometry
    /// in the main coordinate frame. The static (unscrolled) sub-document position is
    /// used — the bridge applies each frame's own scroll offset separately, exactly as it
    /// does for the main document.
    /// </summary>
    private static void LayoutSubdocument(CssBox frame, CssBox subdocRoot, ILayoutEnvironment g)
    {
        double contentLeft = frame.Location.X + frame.ActualBorderLeftWidth + frame.ActualPaddingLeft;
        double contentTop = frame.Location.Y + frame.ActualBorderTopWidth + frame.ActualPaddingTop;
        double contentWidth = frame.Size.Width
            - frame.ActualBorderLeftWidth - frame.ActualBorderRightWidth
            - frame.ActualPaddingLeft - frame.ActualPaddingRight;
        double contentHeight = frame.Size.Height
            - frame.ActualBorderTopWidth - frame.ActualBorderBottomWidth
            - frame.ActualPaddingTop - frame.ActualPaddingBottom;

        if (contentWidth <= 0 || contentHeight <= 0)
            return;

        // The #subdoc-root box acts as the sub-viewport: a block filling the frame's
        // content box, against which the sub-document's root element resolves. Marking
        // it a nested viewport root makes a position:fixed descendant anchor to this
        // frame content box (and an abspos descendant with no positioned ancestor
        // resolve against it) rather than the top-level viewport — RF-BRIDGE-1b Track 3.2.
        subdocRoot.Display = CssConstants.Block;
        subdocRoot.IsNestedViewportRoot = true;
        subdocRoot.NestedViewportSize = new SizeF((float)contentWidth, (float)contentHeight);
        // Pin the sub-viewport to the frame content box with explicit used sizes, so the
        // block-layout pass below does not shrink-wrap the height (which a fixed/abspos
        // descendant reads back as the sub-viewport for bottom/right/percentage
        // resolution).
        subdocRoot.Width = contentWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
        subdocRoot.Height = contentHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
        subdocRoot.Size = new SizeF((float)contentWidth, (float)contentHeight);
        subdocRoot.Location = new PointF((float)contentLeft, (float)contentTop);
        subdocRoot.ActualBottom = contentTop;

        subdocRoot.PerformLayoutImp(g);

        // PerformLayoutImp resolves the subtree relative to the containing block's flow,
        // which need not coincide with the frame content-box origin set above. Translate
        // the whole subtree so its origin lands exactly at the content box.
        double dx = contentLeft - subdocRoot.Location.X;
        double dy = contentTop - subdocRoot.Location.Y;
        if (dx != 0 || dy != 0)
            TranslateSubtree(subdocRoot, dx, dy);
    }

    private static void TranslateSubtree(CssBox box, double dx, double dy)
    {
        box.Location = new PointF((float)(box.Location.X + dx), (float)(box.Location.Y + dy));
        box.ActualBottom += dy;

        foreach (var line in box.LineBoxes)
        {
            var keys = new List<CssBox>(line.Rectangles.Keys);
            foreach (var key in keys)
            {
                var r = line.Rectangles[key];
                line.Rectangles[key] = new RectangleF((float)(r.X + dx), (float)(r.Y + dy), r.Width, r.Height);
            }
            foreach (var word in line.Words)
            {
                word.Left += dx;
                word.Top += dy;
            }
        }

        foreach (var word in box.Words)
        {
            word.Left += dx;
            word.Top += dy;
        }

        foreach (var child in box.Boxes)
            TranslateSubtree(child, dx, dy);
    }

    public void SetBeforeBox(CssBox before)
    {
        int index = _parentBox.Boxes.IndexOf(before);

        if (index < 0)
            throw new Exception("before box doesn't exist on parent");

        _parentBox.Boxes.Remove(this);
        _parentBox.Boxes.Insert(index, this);
    }

    public void SetAllBoxes(CssBox fromBox)
    {
        foreach (var childBox in fromBox.Boxes)
            childBox._parentBox = this;

        Boxes.AddRange(fromBox.Boxes);
        fromBox.Boxes.Clear();
    }

    public virtual void Dispose()
    {
        if (_backgroundImageLoadHandlers != null)
        {
            foreach (var imageLoadHandler in _backgroundImageLoadHandlers)
                imageLoadHandler?.Dispose();
        }

        foreach (var childBox in Boxes)
            childBox.Dispose();
    }

    protected override sealed CssBoxProperties GetParent() => _parentBox;

    private int GetIndexForList()
    {
        // Phase 2: Read list attributes from CssBoxProperties instead of GetAttribute().
        bool reversed = ParentBox.ListReversed;

        int index;
        if (ParentBox.ListStart.HasValue)
        {
            index = ParentBox.ListStart.Value;
        }
        else if (reversed)
        {
            index = 0;
            foreach (CssBox b in ParentBox.Boxes)
            {
                if (b.Display == CssConstants.ListItem)
                    index++;
            }
        }
        else
        {
            index = 1;
        }

        foreach (CssBox b in ParentBox.Boxes)
        {
            if (b.Equals(this))
                return index;

            if (b.Display == CssConstants.ListItem)
                index += reversed ? -1 : 1;
        }

        return index;
    }

    private void CreateListItemBox(ILayoutEnvironment g)
    {
        if (Display != CssConstants.ListItem || ListStyleType == CssConstants.None)
            return;

        if (_listItemBox == null)
        {
            _listItemBox = new CssBox(null, null, BaseUrl);
            _listItemBox.InheritStyle(this);
            _listItemBox.Display = CssConstants.Inline;
            _listItemBox._htmlContainer = ContainerInt;
            _listItemBox._layoutEnvironment = LayoutEnvironment;

            if (ListStyleType.Equals(CssConstants.Disc, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = "•".AsMemory();
            }
            else if (ListStyleType.Equals(CssConstants.Circle, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = "o".AsMemory();
            }
            else if (ListStyleType.Equals(CssConstants.Square, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = "♠".AsMemory();
            }
            else if (ListStyleType.Equals(CssConstants.Decimal, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = (GetIndexForList().ToString(CultureInfo.InvariantCulture) + ".").AsMemory();
            }
            else if (ListStyleType.Equals(CssConstants.DecimalLeadingZero, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = (GetIndexForList().ToString("00", CultureInfo.InvariantCulture) + ".").AsMemory();
            }
            else
            {
                _listItemBox.Text = (LayoutEnvironment.FormatListMarker(GetIndexForList(), ListStyleType) + ".").AsMemory();
            }

            _listItemBox.ParseToWords();

            _listItemBox.PerformLayoutImp(g);
            _listItemBox.Size = new SizeF((float)_listItemBox.Words[0].Width, (float)_listItemBox.Words[0].Height);
        }

        _listItemBox.Words[0].Left = Location.X - _listItemBox.Size.Width - 5;
        _listItemBox.Words[0].Top = Location.Y + ActualPaddingTop; // +FontAscent;
    }

    internal string GetAttribute(string attribute) => GetAttribute(attribute, string.Empty);
    internal string GetAttribute(string attribute, string defaultValue) => HtmlTag != null ? HtmlTag.TryGetAttribute(attribute, defaultValue) : defaultValue;

    internal new void InheritStyle(CssBox box = null, bool everything = false) => base.InheritStyle(box ?? ParentBox, everything);

    protected override ILayoutFont GetCachedFont(string fontFamily, double fsize, LayoutFontStyle st, string fontFeatures) => LayoutEnvironment.GetFont(fontFamily, fsize, st, fontFeatures);

    /// <summary>
    /// Resolves the CSS <c>ch</c> unit by measuring the advance of the "0" glyph
    /// in this box's font (CSS Values 3 §5.1.1). Returns <see cref="double.NaN"/>
    /// when no layout environment is wired up yet so the caller can fall back to
    /// the generic approximation.
    /// </summary>
    protected override double GetChWidth()
    {
        var env = LayoutEnvironment;
        if (env is null)
            return double.NaN;

        double width = env.MeasureText(ActualFont, "0").Width;
        return width > 0 ? width : double.NaN;
    }

    /// <summary>
    /// Resolves a colour declaration to pixels, rewriting the CSS Color 4 functions the canonical
    /// parser does not read (<c>oklch()</c>, <c>lab()</c>, <c>color()</c>, <c>color-mix()</c>, …)
    /// into the <c>rgba()</c> form it does — see <see cref="IR.CssColor4"/> for why the conversion
    /// lives on this side of the boundary rather than inside the parser.
    /// </summary>
    /// <remarks>
    /// The rewrite has to happen before <c>ParseColor</c> rather than after it, because the
    /// unresolved path does not fail visibly: an unrecognised value folds to <i>opaque black</i>,
    /// so a colour merely unknown to the parser paints a solid black box rather than nothing.
    /// </remarks>
    protected override BColor GetActualColor(string colorStr) =>
        LayoutEnvironment.ParseColor(
            IR.CssColor4.NormalizeColorFunctions(colorStr, ResolveCurrentColorFor(colorStr)));

    /// <summary>
    /// This element's <c>color</c> in a spelling <see cref="IR.CssColor4"/> can read, but only when
    /// the value being resolved actually names <c>currentcolor</c> inside a colour function — the
    /// bare keyword is substituted by each property's own accessor, and computing
    /// <see cref="CssBoxProperties.ActualColor"/> speculatively would cost a cascade read on every
    /// colour in the document.
    /// </summary>
    /// <remarks>
    /// Re-entrancy is real rather than theoretical: <c>ActualColor</c> resolves the <c>color</c>
    /// property through this same method, so <c>color: color-mix(in srgb, currentcolor, red)</c>
    /// would ask itself for its own answer. The guard returns null on the inner call, which leaves
    /// that mix unconverted — the honest outcome, since the value is self-referential.
    /// </remarks>
    /// <inheritdoc/>
    protected override string? ResolveCurrentColorFor(string? value)
    {
        if (!MayNeedCurrentColor(value) || _resolvingCurrentColor || LayoutEnvironment is null)
            return null;

        _resolvingCurrentColor = true;
        try
        {
            var current = ActualColor;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"rgba({current.R}, {current.G}, {current.B}, {(current.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})");
        }
        finally
        {
            _resolvingCurrentColor = false;
        }
    }

    private bool _resolvingCurrentColor;

    protected override PointF GetActualLocation(string X, string Y)
    {
        var vpSize = LayoutEnvironment.ViewportSize;
        var left = CssLengthParser.ParseLength(X, vpSize.Width, GetEmHeight(), null);
        var top = CssLengthParser.ParseLength(Y, vpSize.Height, GetEmHeight(), null);

        return new PointF((float)left, (float)top);
    }

    public override string ToString()
    {
        var tag = HtmlTag != null ? $"<{HtmlTag.Name}>" : "anon";

        if (IsBlock)
        {
            return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} Block {FontSize}, Children:{Boxes.Count}";
        }
        else if (Display == CssConstants.None)
        {
            return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} None";
        }
        else
        {
            return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} {Display}: {Text}";
        }
    }
}
