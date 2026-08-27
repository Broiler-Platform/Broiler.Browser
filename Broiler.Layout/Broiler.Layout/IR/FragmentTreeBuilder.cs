using Broiler.Layout.Engine;
using System.Drawing;
using System.Text;
using CssConstants = Broiler.CSS.CssConstants;


namespace Broiler.Layout.IR;

/// <summary>
/// Walks a <see cref="CssBox"/> tree after layout and builds a read-only
/// <see cref="Fragment"/> tree that snapshots the layout geometry.
/// </summary>
internal static class FragmentTreeBuilder
{
    /// <summary>
    /// Builds a <see cref="Fragment"/> tree from the given root <see cref="CssBox"/>.
    /// Should be called after <c>PerformLayout</c> has completed.
    /// </summary>
    public static Fragment Build(CssBox root)
    {
        // Reset and repopulate the document-wide SVG definition tables for this render pass, so a
        // `filter="url(#id)"` or `clip-path: url(#id)` reference resolves even when the `<filter>` /
        // `<clipPath>` lives in a different `<svg>` subtree than the referencing element.
        SvgFilterTable.Reset();
        SvgClipPathTable.Reset();
        // Publish the host's font services for the pass: SvgRenderer has no box to ask for a font,
        // and a DrawSvgTextItem without one is dropped by the raster backend (see SvgTextEnvironment).
        SvgTextEnvironment.Reset(root.LayoutEnvironment);
        var tree = BuildFragment(root, parentHasTransform: false, isRoot: true,
            isolationObservable: DocumentHasBlending(root));
        CollectSvgDefinitions(tree);
        return tree;
    }

    /// <summary>
    /// Whether any box in the document blends with its backdrop — i.e. carries a
    /// <c>mix-blend-mode</c> other than <c>normal</c>. One pass over the tree, before the fragments
    /// are built, because the answer is the same for every box in the pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what decides whether <c>isolation: isolate</c> is worth honouring at paint time.
    /// CSS Compositing §2.2 gives an isolation group exactly one job: to stop a <em>descendant's</em>
    /// blending from reaching the backdrop outside the group. With nothing in the document blending
    /// at all, the group has no job to do, and compositing its contents through a
    /// <c>normal</c>-blend layer produces the very same pixels as drawing them straight onto the
    /// surface. So dropping it is not an approximation — it is the same picture, one layer cheaper.
    /// </para>
    /// <para>
    /// <c>background-blend-mode</c> is deliberately not consulted: it blends an element's own
    /// background layers with each other, never with the backdrop behind the element, so no ancestor's
    /// isolation can be observed through it.
    /// </para>
    /// <para>
    /// The isolation group still <em>creates a stacking context</em> either way — see
    /// <see cref="IsStackingContext"/>, which reads the box and is untouched by this. Only the paint
    /// layer goes away, so paint order is unchanged.
    /// </para>
    /// <para>
    /// <b>This also stands in for a renderer fix that cannot land here.</b> A compositing group
    /// whose contents are not raster-compatible — one transformed descendant is enough — used to be
    /// routed to a stub compat backend that dropped the group's entire subtree, so an isolation
    /// group over a page with a transform under it rendered as an empty viewport. That belongs in
    /// <c>Broiler.HTML</c>'s <c>GraphicsAdapter</c> and ships as the <c>patches/</c> entry
    /// "Keep a compositing group's contents when the group cannot use the raster canvas"; until that
    /// is applied, not emitting the unobservable group is what keeps such a page visible.
    /// <c>duckduckgo.com</c>'s start page is the case: all of its content sits under
    /// <c>#__next { isolation: isolate }</c>, it has transforms beneath that, and it uses no blend
    /// mode anywhere.
    /// </para>
    /// </remarks>
    private static bool DocumentHasBlending(CssBox box)
    {
        if (!string.IsNullOrEmpty(box.MixBlendMode)
            && !box.MixBlendMode.Equals("normal", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var child in box.Boxes)
        {
            if (DocumentHasBlending(child))
                return true;
        }

        return false;
    }

    /// <summary>Walks the fragment tree and registers every modelled SVG filter (see
    /// <see cref="SvgFilterTable"/>) and clip path (see <see cref="SvgClipPathTable"/>) from each
    /// fragment's serialized SVG content.</summary>
    private static void CollectSvgDefinitions(Fragment fragment)
    {
        if (!string.IsNullOrEmpty(fragment.SvgContent))
        {
            SvgRenderer.CollectFloodFilters(fragment.SvgContent);
            SvgRenderer.CollectColorFilters(fragment.SvgContent);
            SvgRenderer.CollectClipPaths(fragment.SvgContent);
        }

        foreach (var child in fragment.Children)
            CollectSvgDefinitions(child);
    }

    /// <summary>
    /// A bound on how many times a fixed-position subtree is repeated. The paged renderer composes
    /// a handful of pages, and a document long enough to exceed this is one where the pages past it
    /// are not being rasterised anyway — this only keeps a very long flow from building copies
    /// nothing will ever draw.
    /// </summary>
    private const int MaxRepeatedFixedPages = 32;

    /// <summary>
    /// The extra appearances of every fixed-position box: CSS Paged Media 3 makes the page area the
    /// fixed-positioning containing block, so a fixed box repeats on each page of the flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Paint-time only. The copies are fragments and never boxes, so nothing about the flow moves:
    /// a fixed box is out of flow, contributes no height, and the page count is what it was.
    /// </para>
    /// <para>
    /// Inert unless a page size is in force. A screen render leaves <c>PageSize</c> at its
    /// no-pagination default and a single-page render has one page, so in both cases there is
    /// nothing to repeat and the tree is exactly what it was.
    /// </para>
    /// <para>
    /// WPT's <c>css-page/fixedpos-001-print</c> through <c>-011</c> are written against this, and
    /// they say so in the markup they render — <c>"This should repeat on every page"</c>. Their
    /// references state the same layout with one absolutely-positioned copy per page.
    /// </para>
    /// </remarks>
    private static List<Fragment> RepeatedFixedFragments(CssBox root, bool hasTransformAncestor,
        bool isolationObservable)
    {
        var repeats = new List<Fragment>();

        if (root.LayoutEnvironment is not { } environment)
            return repeats;

        double pageHeight = environment.PageSize.Height;
        if (pageHeight <= 0 || pageHeight >= UnpaginatedPageExtent)
            return repeats;

        // The same count the paged renderer cuts its pages at, for the same reason: a fixed box
        // belongs on every page the flow actually fills, and on no page after them.
        int pages = (int)Math.Ceiling(environment.ActualSize.Height / pageHeight - 0.01);
        pages = Math.Min(pages, MaxRepeatedFixedPages);
        if (pages <= 1)
            return repeats;

        var fixedBoxes = new List<CssBox>();
        Collect(root);

        foreach (var box in fixedBoxes)
        {
            for (int page = 1; page < pages; page++)
            {
                box.OffsetTop(pageHeight);
                repeats.Add(BuildFragment(box, hasTransformAncestor,
                    isolationObservable: isolationObservable));
            }

            // Put it back where layout left it: the fragment already built for page one refers to
            // that position, and so does anything that reads the box tree afterwards.
            box.OffsetTop(-pageHeight * (pages - 1));
        }

        return repeats;

        void Collect(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                // A fixed box inside a fixed box is part of the subtree that repeats, not a
                // subtree of its own to repeat again.
                if (child.Position == CssConstants.Fixed)
                {
                    if (child.Display != CssConstants.None && !child.PositionHidden && !child.ClampedAway)
                        fixedBoxes.Add(child);

                    continue;
                }

                Collect(child);
            }
        }
    }

    /// <summary>
    /// A page size this large means "not paginated" — the value the container installs for an
    /// ordinary screen render. Kept in step with <c>CssBox.UnpaginatedPageExtent</c>.
    /// </summary>
    private const double UnpaginatedPageExtent = 90000;

    private static Fragment BuildFragment(CssBox box, bool parentHasTransform, bool isRoot = false,
        bool isolationObservable = true)
    {
        var style = ComputedStyleBuilder.FromBox(box, box.HtmlTag?.Name, isolationObservable);
        bool hasTransformAncestor = parentHasTransform
            || (!string.IsNullOrEmpty(style.Transform)
            && !style.Transform.Equals("none", StringComparison.OrdinalIgnoreCase));

        // CSS Containment §4 (content-visibility: hidden): the element's
        // contents are skipped — not painted — while the element's own box
        // (background, border, box-model size) still renders. Build the box
        // fragment with no child fragments and no line fragments so the whole
        // subtree, including any promoted top-layer content nested under it
        // (e.g. a modal <dialog> and its ::backdrop), is left unpainted.
        bool contentHidden = string.Equals(
            style.ContentVisibility, "hidden", StringComparison.OrdinalIgnoreCase);

        var children = new List<Fragment>(contentHidden ? 0 : box.Boxes.Count);
        if (!contentHidden)
        {
            foreach (var child in box.Boxes)
            {
                // position-visibility (P5.8d.2b): the native anchor post-pass sets
                // PositionHidden on an anchor-positioned box whose anchor is not visible.
                // Exclude it (and its subtree) from the paint tree after layout — the
                // engine equivalent of the bridge writing display:none.
                if (child.PositionHidden)
                    continue;
                // CSS Overflow 4 §5 (`continue: discard`): a box whose content a
                // line clamp discarded outright paints nothing — not its text,
                // and not its own background or borders either.
                if (child.ClampedAway)
                    continue;
                children.Add(BuildFragment(child, hasTransformAncestor,
                    isolationObservable: isolationObservable));
            }
        }

        // In paged media a fixed-position box is fixed to the *page area*, so it appears once on
        // every page rather than once in the document. The box was laid out on page one, which is
        // where its containing block is; the rest of its appearances are copies of that subtree,
        // one page further down each time.
        if (isRoot && !contentHidden)
            children.AddRange(RepeatedFixedFragments(box, hasTransformAncestor, isolationObservable));

        List<LineFragment>? lines = null;
        if (!contentHidden && box.LineBoxes.Count > 0)
        {
            lines = new List<LineFragment>(box.LineBoxes.Count);
            foreach (var lineBox in box.LineBoxes)
                lines.Add(BuildLineFragment(lineBox));
        }

        // Emit the generated list-item marker (bullet / number). It is laid out on
        // box.ListItemMarkerBox (not a member of box.Boxes), so nothing else paints
        // it — build a dedicated line fragment for its glyph word at the marker's
        // resolved position (already in absolute layout coordinates, like every
        // other word). content-visibility:hidden suppresses generated content too.
        if (!contentHidden && box.ListItemMarkerBox is { } markerBox && markerBox.Words.Count > 0)
        {
            var markerLine = BuildListMarkerLineFragment(markerBox);
            if (markerLine != null)
                (lines ??= []).Add(markerLine);
        }

        // Phase 3: Capture background image handle for the new paint path
        object? bgImage = box.LoadedBackgroundImage;

        // Phase 3: Capture replaced image handle (e.g. <img> elements)
        object? imgHandle = null;
        RectangleF imgSourceRect = RectangleF.Empty;
        SizeF imgIntrinsicSize = SizeF.Empty;
        float imgIntrinsicRatio = 0;
        string svgContent = null;
        if (box is CssBoxImage imgBox)
        {
            imgHandle = imgBox.Image;
            // CssBoxImage stores source rect on its internal CssRectImage word
            if (imgBox.Words.Count > 0 && imgBox.Words[0] is CssRectImage rectImage)
                imgSourceRect = rectImage.ImageRectangle;

            // The natural size and ratio `object-fit` scales against. Layout has already consumed
            // them to size the box, but not in a form paint can recover — a box sized by the CSS
            // alone keeps no trace of either — so they are carried onto the fragment. Content with
            // a ratio and no size reports the default object size here, which is not intrinsic and
            // must not be read as though it were.
            if (imgHandle != null && box.LayoutEnvironment?.GetImageIntrinsics(imgHandle) is { } intrinsics)
            {
                // The natural size `object-fit: none` draws at is the *density-corrected* one
                // (HTML §4.8.4.3) — the same size layout sized the box from — so a `2x` candidate
                // is drawn at half its decoded pixels rather than overflowing the box it was
                // deliberately sized to fit. The density is 1 for every image without a srcset.
                double density = box is CssBoxImage { Words: [CssRectImage word, ..] } && word.PixelDensity > 0
                    ? word.PixelDensity
                    : 1.0;

                if (intrinsics.HasIntrinsicSize)
                {
                    imgIntrinsicSize = new SizeF(
                        (float)(intrinsics.Width / density), (float)(intrinsics.Height / density));
                }

                if (intrinsics.HasIntrinsicRatio)
                    imgIntrinsicRatio = (float)intrinsics.AspectRatio;
            }
        }

        // Check for <object> elements referencing SVG content.  When the
        // image loader cannot decode the data (e.g. SVG, which is not a
        // raster format), imgHandle will be null.  If the data attribute
        // ends with ".svg" or is a data:image/svg+xml URI, try to load
        // the SVG content so that PaintWalker can render it via SvgRenderer.
        if (imgHandle == null && box.HtmlTag != null &&
            box.HtmlTag.Name.Equals("object", StringComparison.OrdinalIgnoreCase))
        {
            string dataAttr = box.GetAttribute("data");
            if (!string.IsNullOrEmpty(dataAttr))
            {
                svgContent = TryLoadSvgContent(dataAttr, box.BaseUrl);
            }
        }

        // Inline <svg> elements: serialise the SVG subtree back to markup
        // so that PaintWalker can render it via SvgRenderer.
        if (svgContent == null && box.HtmlTag != null &&
            box.HtmlTag.Name.Equals("svg", StringComparison.OrdinalIgnoreCase))
        {
            svgContent = SerializeSvgSubtree(box);
        }

        // Nested browsing contexts: <object type="text/html">, <iframe>, and
        // <frame> render a separate document into their content box.  Load the
        // referenced document's HTML here; the image renderer rasterises it at
        // the element's used size and composites it over the box.
        //
        // visibility:hidden (or collapse) on the box — including the value
        // inherited from an enclosing <frameset> — hides this replaced element,
        // so its nested document must not paint.  Skipping the load here keeps
        // the composited output empty without the image renderer needing to
        // re-check visibility.  CSS inheritance does not cross the frame
        // boundary, so a hidden host frame suppresses the whole nested document
        // regardless of that document's own styles.
        string embeddedHtml = null;
        string embeddedBaseUrl = null;
        if (svgContent == null && imgHandle == null && box.HtmlTag != null
            && string.Equals(style.Visibility, "visible", StringComparison.OrdinalIgnoreCase))
        {
            (embeddedHtml, embeddedBaseUrl) = TryLoadEmbeddedDocument(box);
        }

        // Capture per-line-box rectangles for inline elements (used for backgrounds/borders)
        List<RectangleF>? inlineRects = null;
        if (box.Rectangles.Count > 0)
        {
            inlineRects = [.. box.Rectangles.Values];
        }

        // CssBox.Size.Height is never set for block-level boxes during layout;
        // the actual rendered height is tracked via ActualBottom instead.
        // Compute the correct border-box height so that PaintWalker can
        // draw backgrounds and borders (which skip Height <= 0 rects).
        var size = box.Size;

        // Sanitise NaN width: auto-width absolutely positioned elements
        // may still have NaN if ComputeShrinkToFitWidth could not resolve
        // a finite value (e.g. deeply nested inline objects).  Fall back
        // to ActualRight - Location.X which is layout-computed.
        if (float.IsNaN(size.Width))
        {
            float layoutWidth = (float)(box.ActualRight - box.Location.X);
            size = new SizeF(layoutWidth > 0 ? layoutWidth : 0, size.Height);
        }

        float layoutHeight = (float)(box.ActualBottom - box.Location.Y);
        if (layoutHeight > size.Height)
            size = new SizeF(size.Width, layoutHeight);

        return new Fragment
        {
            Location = box.Location,
            Size = size,
            Margin = style.Margin,
            Border = style.Border,
            Padding = style.Padding,
            Lines = lines,
            Children = children,
            Style = style,
            CreatesStackingContext = IsStackingContext(box),
            StackLevel = GetStackLevel(box),
            TopLayerOrder = GetTopLayerOrder(box),
            HasTransformAncestor = hasTransformAncestor,
            BackgroundImageHandle = bgImage,
            ImageHandle = imgHandle,
            ImageSourceRect = imgSourceRect,
            ImageIntrinsicSize = imgIntrinsicSize,
            ImageIntrinsicRatio = imgIntrinsicRatio,
            SvgContent = svgContent,
            EmbeddedDocumentHtml = embeddedHtml,
            EmbeddedDocumentBaseUrl = embeddedBaseUrl,
            InlineRects = inlineRects,
        };
    }

    private static LineFragment BuildLineFragment(CssLineBox lineBox)
    {
        var inlines = new List<InlineFragment>();

        // CSS2.1 Appendix E: within a stacking context, in-flow inline content
        // (step 5) paints beneath positioned descendants (steps 6–7).  An
        // out-of-flow positioned element nested in inline content has its words
        // reported to this ancestor block's line box (its static position is
        // resolved during inline layout), so they would otherwise paint in
        // document order — i.e. potentially *under* later in-flow siblings.
        // Defer such words to the end of the line so they paint above the
        // in-flow text, matching the positioned-paint phase.
        List<InlineFragment>? positionedInlines = null;

        foreach (var word in lineBox.Words)
        {
            var ownerStyle = ComputedStyleBuilder.FromBox(word.OwnerBox);

            // PROTOTYPE Stage 2 (BROILER_VERTICAL_FLOW): in a vertical writing
            // mode, text-orientation:mixed rotates the run 90° clockwise.  The
            // layout transform already stacked glyphs down the column; rotating
            // each glyph completes the sideways orientation. sideways-lr is the
            // exception: its glyphs face the other way (90° counter-clockwise),
            // matching its bottom→top inline flow (CSS Writing Modes 4 §5.1).
            float glyphRotation = 0f;
            if (VerticalFlowPrototype.Enabled
                && CssBoxProperties.IsVerticalWritingMode(word.OwnerBox.WritingMode))
            {
                glyphRotation = string.Equals(word.OwnerBox.WritingMode?.Trim(), "sideways-lr",
                    StringComparison.OrdinalIgnoreCase)
                    ? -90f
                    : 90f;
            }

            var inlineFragment = new InlineFragment
            {
                X = (float)word.Left,
                Y = (float)word.Top,
                Width = (float)word.Width,
                Height = (float)word.Height,
                Text = word.IsSpaces
                    ? (word.OwnerBox.WhiteSpace is CssConstants.Pre or CssConstants.PreWrap
                        ? word.Text   // CSS2.1 §16.6: preserve space sequences in pre/pre-wrap
                        : " ")
                    : word.Text,
                Style = ownerStyle,
                GlyphRotationDeg = glyphRotation,
                FontHandle = word.OwnerBox.ActualFont,
                Selected = word.Selected,
                SelectedStartOffset = word.SelectedStartOffset,
                SelectedEndOffset = word.SelectedEndOffset,
            };

            if (IsOutOfFlowPositioned(word.OwnerBox, lineBox.OwnerBox))
            {
                positionedInlines ??= [];
                positionedInlines.Add(inlineFragment);
            }
            else
            {
                inlines.Add(inlineFragment);
            }
        }

        if (positionedInlines != null)
            inlines.AddRange(positionedInlines);

        // Compute line bounds from all rectangles in this line box
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxR = float.MinValue, maxB = float.MinValue;

        foreach (var rect in lineBox.Rectangles.Values)
        {
            if (rect.X < minX) minX = rect.X;
            if (rect.Y < minY) minY = rect.Y;
            if (rect.Right > maxR) maxR = rect.Right;
            if (rect.Bottom > maxB) maxB = rect.Bottom;
        }

        if (lineBox.Rectangles.Count == 0)
            minX = minY = maxR = maxB = 0;

        return new LineFragment
        {
            X = minX,
            Y = minY,
            Width = maxR - minX,
            Height = maxB - minY,
            Baseline = 0,
            Inlines = inlines,
        };
    }

    /// <summary>
    /// Builds a one-inline line fragment for a generated list-item marker glyph.
    /// The marker box carries a single word positioned in absolute layout
    /// coordinates (to the left of the principal box), styled by inheritance from
    /// the list item; emit it exactly like a normal inline word so PaintWalker
    /// draws the bullet / number.
    /// </summary>
    private static LineFragment? BuildListMarkerLineFragment(CssBox markerBox)
    {
        var word = markerBox.Words[0];
        var markerStyle = ComputedStyleBuilder.FromBox(markerBox);

        var inline = new InlineFragment
        {
            X = (float)word.Left,
            Y = (float)word.Top,
            Width = (float)word.Width,
            Height = (float)word.Height,
            Text = word.Text,
            Style = markerStyle,
            FontHandle = markerBox.ActualFont,
        };

        return new LineFragment
        {
            X = (float)word.Left,
            Y = (float)word.Top,
            Width = (float)word.Width,
            Height = (float)word.Height,
            Baseline = 0,
            Inlines = [inline],
        };
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ownerBox"/> lies
    /// inside an absolutely- or fixed-positioned element somewhere between it
    /// and <paramref name="lineOwner"/> (the block that owns the line box).
    /// Such words are out of flow: their static position is resolved on this
    /// line, but for painting they belong to the positioned phase, not the
    /// in-flow inline phase (CSS2.1 Appendix E).
    /// </summary>
    private static bool IsOutOfFlowPositioned(CssBox ownerBox, CssBox lineOwner)
    {
        for (var b = ownerBox; b != null && b != lineOwner; b = b.ParentBox)
        {
            if (b.Position == CssConstants.Absolute || b.Position == CssConstants.Fixed)
                return true;
        }
        return false;
    }

    private static bool IsStackingContext(CssBox box)
    {
        // A box creates a stacking context if it is positioned with a z-index,
        // or has opacity < 1, or is a fixed/absolute-positioned element.
        if (box.Position == CssConstants.Absolute || box.Position == CssConstants.Fixed)
            return true;

        // CSS2.1 §9.9.1: A positioned element with a computed z-index
        // other than 'auto' establishes a new stacking context.
        if (box.Position == CssConstants.Relative
            && box.ZIndex != null && box.ZIndex != CssConstants.Auto
            && int.TryParse(box.ZIndex, out _))
            return true;

        if (double.TryParse(box.Opacity, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var opacity) && opacity < 1.0)
            return true;

        // CSS Compositing §3: Elements with a mix-blend-mode other than 'normal'
        // must create a stacking context.
        if (!string.IsNullOrEmpty(box.MixBlendMode)
            && !box.MixBlendMode.Equals("normal", StringComparison.OrdinalIgnoreCase))
            return true;

        // CSS Compositing §2.2: 'isolation: isolate' creates a stacking context.
        if (!string.IsNullOrEmpty(box.Isolation)
            && box.Isolation.Equals("isolate", StringComparison.OrdinalIgnoreCase))
            return true;

        // CSS Filter Effects §2: A filter other than 'none' creates a stacking context.
        if (!string.IsNullOrEmpty(box.Filter)
            && !box.Filter.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        // CSS Transforms §6.1: An element with a transform other than 'none'
        // creates a stacking context and a containing block.
        if (!string.IsNullOrEmpty(box.Transform)
            && !box.Transform.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Returns the computed stack level (z-index) for a box.
    /// CSS2.1 §9.9.1: 'auto' computes to 0 for painting order.
    /// </summary>
    private static int GetStackLevel(CssBox box)
    {
        if (box.ZIndex != null && box.ZIndex != CssConstants.Auto && int.TryParse(box.ZIndex, out int z))
            return z;

        return 0;
    }

    // CSS Position 4 §top-layer: a box the bridge has marked as top-layer (an open modal
    // <dialog>, an open popover, or a synthesized ::backdrop) carries a data-broiler-top-layer
    // attribute whose value is its top-layer order (a later-added element has a higher order and
    // paints over an earlier one). Absent/blank/unparseable → not in the top layer (null), so an
    // ordinary box is untouched. Projecting it here lets PaintWalker paint these in a real
    // top-layer pass instead of via the bridge's very-large-z-index emulation.
    private const string TopLayerAttr = "data-broiler-top-layer";

    private static int? GetTopLayerOrder(CssBox box)
    {
        // A renderer-generated top-layer box (e.g. a native ::backdrop) carries its order in the
        // box field directly — it has no element to hold the data-broiler-top-layer attribute.
        if (box.TopLayerOrder is int fieldOrder)
            return fieldOrder;

        var raw = box.GetAttribute(TopLayerAttr);
        if (!string.IsNullOrEmpty(raw) &&
            int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int order))
            return order;
        return null;
    }

    /// <summary>
    /// Attempts to load SVG content from a <c>data</c> attribute value.
    /// Supports <c>data:image/svg+xml</c> URIs and local <c>.svg</c> file references.
    /// </summary>
    private static string TryLoadSvgContent(string dataAttr, Uri baseUrl)
    {
        // data:image/svg+xml,<svg>...</svg>
        const string svgDataPrefix = "data:image/svg+xml";
        if (dataAttr.StartsWith(svgDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int comma = dataAttr.IndexOf(',');
            if (comma >= 0 && comma + 1 < dataAttr.Length)
                return Uri.UnescapeDataString(dataAttr[(comma + 1)..]);

            // base64 variant
            int semi = dataAttr.IndexOf(';');
            if (semi >= 0)
            {
                string encoding = dataAttr[(semi + 1)..];
                int commaB64 = encoding.IndexOf(',');
                if (commaB64 >= 0 && encoding[..commaB64].Equals("base64", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(encoding[(commaB64 + 1)..]);
                        return Encoding.UTF8.GetString(bytes);
                    }
                    catch { /* invalid base64 — fall through */ }
                }
            }

            return null;
        }

        // Local .svg file reference
        if (dataAttr.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && baseUrl != null)
        {
            try
            {
                string basePath = baseUrl.IsAbsoluteUri && baseUrl.IsFile
                    ? baseUrl.LocalPath
                    : baseUrl.OriginalString;
                string dir = Path.GetDirectoryName(basePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string svgPath = Path.GetFullPath(Path.Combine(dir, dataAttr));
                    if (File.Exists(svgPath))
                        return File.ReadAllText(svgPath);
                }
            }
            catch { /* path resolution failure — skip */ }
        }

        return null;
    }

    /// <summary>
    /// Attempts to load the document of a nested browsing context —
    /// <c>&lt;object type="text/html"&gt;</c>, <c>&lt;iframe&gt;</c>, or
    /// <c>&lt;frame&gt;</c> — from its <c>data</c>/<c>src</c> attribute.
    /// Returns the document HTML and its resolved base URL, or
    /// <c>(null, null)</c> when the element is not an embedded HTML document
    /// or the source cannot be read.
    /// </summary>
    /// <summary>
    /// The base URL a nested document with no URL of its own inherits from its container — an
    /// <c>about:srcdoc</c> or <c>data:</c> document. <see cref="Uri.AbsoluteUri"/> throws on a
    /// relative <see cref="Uri"/>, and a page rendered without a base URL has one, so reading it
    /// unguarded turned a frame into an exception out of the whole render.
    /// </summary>
    private static string ContainerBaseUrl(CssBox box) =>
        box.BaseUrl is { IsAbsoluteUri: true } baseUrl ? baseUrl.AbsoluteUri : null;

    private static (string Html, string BaseUrl) TryLoadEmbeddedDocument(CssBox box)
    {
        string tagName = box.HtmlTag.Name;

        // A nested browsing context that has been scripted is no longer what its `src` resource
        // says: its own scripts, or a parent reaching in through `frames[0]`, have moved the live
        // document on. Re-reading the file would paint the frame as it never was, so the bridge
        // stamps the live document here — the `src` counterpart of `srcdoc`, together with the URL
        // it was loaded from so relative references inside still resolve against the resource.
        // Only a frame that has actually diverged carries it; see DomBridge.FrameDocumentProjection.
        if (box.GetAttribute("data-broiler-frame-document") is { Length: > 0 } liveDocument)
        {
            string liveBaseUrl = box.GetAttribute("data-broiler-frame-base");
            return (liveDocument, string.IsNullOrEmpty(liveBaseUrl) ? ContainerBaseUrl(box) : liveBaseUrl);
        }

        string url;
        if (tagName.Equals("object", StringComparison.OrdinalIgnoreCase))
        {
            url = box.GetAttribute("data");
            if (string.IsNullOrEmpty(url))
                return (null, null);

            // Only text/html objects are nested documents.  Image/SVG data is
            // handled elsewhere; honour an explicit type, else fall back to the
            // data URL's extension.
            string type = box.GetAttribute("type");
            bool isHtml = !string.IsNullOrEmpty(type)
                ? type.Trim().StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
                  || type.Trim().StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase)
                : HasHtmlExtension(url);
            if (!isHtml)
                return (null, null);
        }
        else if (tagName.Equals("iframe", StringComparison.OrdinalIgnoreCase)
                 || tagName.Equals("frame", StringComparison.OrdinalIgnoreCase))
        {
            // HTML §4.8.5: an <iframe>'s `srcdoc` carries the nested document's markup inline, and
            // when present it is what the frame navigates to — `src` is not consulted at all. Only
            // `src` was read here, so every srcdoc frame rendered as an empty box and the parent
            // showed through it: the whole WPT css-view-transitions iframe family (issue #1552
            // problems 5 and 6) differed from its reference by exactly the frame's area.
            //
            // Its base URL is the container document's — an `about:srcdoc` document inherits the
            // base URL of the browsing context that created it (HTML §"the base element") — so a
            // relative link inside resolves the same way one in the parent would.
            //
            // An absent or empty `srcdoc` is not a document: the reference browser leaves the frame
            // showing nothing (its canvas is transparent), which is what returning null produces.
            // <frame> has no srcdoc content attribute, so this is scoped to <iframe>.
            if (tagName.Equals("iframe", StringComparison.OrdinalIgnoreCase)
                && box.GetAttribute("srcdoc") is { Length: > 0 } srcDoc)
            {
                return (srcDoc, ContainerBaseUrl(box));
            }

            url = box.GetAttribute("src");
            if (string.IsNullOrEmpty(url))
                return (null, null);
        }
        else
        {
            return (null, null);
        }

        // data:text/html,<markup>
        if (url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
        {
            int comma = url.IndexOf(',');
            if (comma >= 0 && comma + 1 < url.Length)
                return (Uri.UnescapeDataString(url[(comma + 1)..]), ContainerBaseUrl(box));
            return (null, null);
        }

        // An absolute URL naming a host the caller serves from the document root is that root's
        // file under a different name: dropping the origin leaves the root-relative URL for the
        // same resource. Without this a frame pointed at such a host is a network fetch a local
        // render cannot make, so it paints empty — which is what WPT's cross-origin reftests
        // (`http://not-web-platform.test:8000/…`, every host of theirs served from one checkout)
        // showed. No list set, no absolute URL resolves, and nothing changes.
        if (Engine.DocumentRoot.TryStripLocalOrigin(url) is { Length: > 0 } servedLocally)
            url = servedLocally;

        // HTML §"resolve a URL": a leading `/` is resolved against the document's origin, not
        // against the directory the containing page sits in. A file:// render has no origin to
        // ask, so the root comes from the host (DocumentRoot) — and joining such a URL onto the
        // containing directory is worse than not resolving it at all, because Path.Combine
        // discards its left operand when the right one is rooted and yields a path at the
        // filesystem root.
        if (IsRootRelative(url))
            return LoadEmbeddedDocumentFromRoot(url);

        if (box.BaseUrl == null)
            return (null, null);

        try
        {
            string basePath = box.BaseUrl.IsAbsoluteUri && box.BaseUrl.IsFile
                ? box.BaseUrl.LocalPath
                : box.BaseUrl.OriginalString;
            string dir = Path.GetDirectoryName(basePath);
            if (string.IsNullOrEmpty(dir))
                return (null, null);

            // Only the *path* of the URL names a file. A document-relative src is as entitled to
            // carry a query or a fragment as a root-relative one — `src="support/x.sub.html?a=b"`,
            // `src="inner.html#frag"` — and joining the whole URL onto the directory produced a
            // path with `?a=b` in the file name, which exists nowhere: the frame painted empty
            // with no error, exactly as if the file were missing. The root-relative loader below
            // has always stripped them (and percent-decoded what is left); this branch had not, so
            // the two disagreed on the same URL depending only on whether it began with a slash.
            // WPT's img-element `sizes` tests are the visible shape of it — each is a page whose
            // whole content is one `<iframe src="support/sizes-iframed.sub.html?doctype=…">`.
            if (ResolveRelativeDocumentPath(url) is not { Length: > 0 } relativePath)
                return (null, null);

            string docPath = Path.GetFullPath(Path.Combine(dir, relativePath));
            if (!File.Exists(docPath))
                return (null, null);

            string docUrl = new Uri(docPath).AbsoluteUri;
            string markup = BuildEmbeddedDocumentMarkup(docPath, docUrl);
            return markup == null ? (null, null) : (markup, docUrl);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Whether <paramref name="url"/> is root-relative — one leading slash, resolved against the
    /// document root. Two slashes is a scheme-relative URL (<c>//host/path</c>), which names another
    /// origin and is not ours to read off the local disk.
    /// </summary>
    private static bool IsRootRelative(string url) =>
        url.Length > 1 && url[0] == '/' && url[1] != '/';

    /// <summary>
    /// The filesystem-relative path a document-relative URL names: its path component, percent
    /// decoded and with the URL's <c>/</c> separators turned into the platform's. <c>null</c> when
    /// the URL has no path at all (a bare <c>?query</c> or <c>#fragment</c>, which addresses the
    /// containing document rather than a new one).
    /// </summary>
    /// <remarks>
    /// The query and fragment address a server that is not there; the path alone names the file.
    /// WPT leans on this — <c>?pipe=</c> and <c>?doctype=</c> decorate the URL of a resource that
    /// is still just a file on disk. Shared with <see cref="LoadEmbeddedDocumentFromRoot"/> so the
    /// document-relative and root-relative branches cannot drift apart again.
    /// </remarks>
    private static string ResolveRelativeDocumentPath(string url)
    {
        string path = url.Split('?', '#')[0];
        if (path.Length == 0)
            return null;

        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            // Malformed escape — fall back to the raw path rather than failing the resolve.
        }

        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Loads a root-relative sub-document from the host's document root
    /// (<see cref="Engine.DocumentRoot.Current"/>), or <c>(null, null)</c> when no root is set or
    /// the path names nothing under it — the empty frame this case has always painted.
    /// </summary>
    private static (string Html, string BaseUrl) LoadEmbeddedDocumentFromRoot(string url)
    {
        if (Engine.DocumentRoot.Current is not { Length: > 0 } root)
            return (null, null);

        try
        {
            // The path component alone names the file, the same way it does for a document-relative
            // src — and the runner's other root-relative loaders strip it identically
            // (WptTestRunner.TryResolveWptRootRelativePath). A root-relative URL is its leading
            // slash plus something, so a one-character path is the document root itself.
            if (ResolveRelativeDocumentPath(url) is not { Length: > 1 } path)
                return (null, null);

            string docPath = Path.GetFullPath(
                Path.Combine(root, path.TrimStart(Path.DirectorySeparatorChar, '/')));

            // A `..` segment must not walk out of the root: served over HTTP it could not, and the
            // frame would 404 rather than reach an unrelated file.
            if (!IsUnderRoot(docPath, root) || !File.Exists(docPath))
                return (null, null);

            string docUrl = new Uri(docPath).AbsoluteUri;
            string markup = BuildEmbeddedDocumentMarkup(docPath, docUrl);
            return markup == null ? (null, null) : (markup, docUrl);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Whether <paramref name="path"/> sits inside <paramref name="root"/>.</summary>
    private static bool IsUnderRoot(string path, string root)
    {
        string full = Path.GetFullPath(root);
        if (!full.EndsWith(Path.DirectorySeparatorChar))
            full += Path.DirectorySeparatorChar;

        return path.StartsWith(full, StringComparison.Ordinal);
    }

    /// <summary>
    /// The markup a nested browsing context renders for the resource at
    /// <paramref name="path"/>, or <c>null</c> when the resource has no document
    /// representation (the frame then paints empty).
    /// </summary>
    /// <remarks>
    /// Only an HTML resource is its own document. Navigating a frame to an image,
    /// to media, or to plain text does not parse the resource as markup: the UA
    /// synthesises a document that presents it (HTML §"read-image" /
    /// §"read-media" / §"read-text"). Feeding such a resource to the HTML parser
    /// instead is not merely wrong output — the tokeniser mints tag names out of
    /// binary noise, and a name like <c>n&#x5;4&#x353;:</c> (real bytes from a
    /// WebM file) reaches <c>DomDocument.CreateElement</c> and throws, taking down
    /// the whole page render (WPT
    /// <c>navigation-timing/dom-interactive-media-document.html</c>).
    /// </remarks>
    private static string BuildEmbeddedDocumentMarkup(string path, string docUrl)
    {
        switch (ClassifyEmbeddedResource(path))
        {
            case EmbeddedResourceKind.Html:
                return File.ReadAllText(path);

            case EmbeddedResourceKind.Image:
                // Image document: the image alone on the canvas, at its natural size.
                return "<!DOCTYPE html><html><head><style>html,body{margin:0}</style></head>"
                     + $"<body><img src=\"{AttributeEscaped(docUrl)}\"></body></html>";

            case EmbeddedResourceKind.Media:
                // Media document: one media element alone on a black canvas, as a
                // top-level media document paints it — a <video> for audio too,
                // which is the element a UA media document builds either way.
                return "<!DOCTYPE html><html><head><style>html,body{margin:0;background:#000}</style></head>"
                     + $"<body><video controls src=\"{AttributeEscaped(docUrl)}\"></video></body></html>";

            case EmbeddedResourceKind.PlainText:
                return BuildPlainTextDocument(File.ReadAllText(path));

            case EmbeddedResourceKind.Unknown:
                // No extension to classify by (common for generated WPT resources):
                // sniff the bytes rather than guess. Text is treated as markup — an
                // extensionless HTML resource is the usual case — and binary is not.
                return LooksBinary(path) ? null : File.ReadAllText(path);

            default:
                // A resource we can classify but cannot present (PDF, a font, an
                // archive): no document, so no markup.
                return null;
        }
    }

    /// <summary>A plain-text document: the text preserved in a <c>&lt;pre&gt;</c>, as a
    /// text document's UA stylesheet presents it.</summary>
    private static string BuildPlainTextDocument(string text)
    {
        var sb = new StringBuilder(
            "<!DOCTYPE html><html><head><style>html,body{margin:0}"
            + "pre{margin:0;white-space:pre-wrap;word-wrap:break-word}</style></head><body><pre>");
        AppendXmlEscaped(sb, text);
        sb.Append("</pre></body></html>");
        return sb.ToString();
    }

    private enum EmbeddedResourceKind
    {
        /// <summary>Markup — parse it as the frame's document.</summary>
        Html,
        Image,
        /// <summary>Audio or video.</summary>
        Media,
        PlainText,
        /// <summary>Classified, but with no document representation (PDF, font, …).</summary>
        Opaque,
        /// <summary>Not classifiable from the file name.</summary>
        Unknown,
    }

    /// <summary>Classifies an embedded resource by file extension, the only signal a
    /// file:// load carries (there is no Content-Type header).</summary>
    private static EmbeddedResourceKind ClassifyEmbeddedResource(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            "" => EmbeddedResourceKind.Unknown,
            ".html" or ".htm" or ".xhtml" or ".xht" or ".shtml" or ".svg" => EmbeddedResourceKind.Html,
            ".txt" or ".text" or ".csv" or ".md" or ".js" or ".mjs" or ".css" or ".json"
                => EmbeddedResourceKind.PlainText,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".ico" or ".avif"
                => EmbeddedResourceKind.Image,
            ".webm" or ".mp4" or ".m4v" or ".ogv" or ".mov" or ".mkv"
                or ".mp3" or ".m4a" or ".wav" or ".ogg" or ".oga" or ".flac" or ".opus"
                => EmbeddedResourceKind.Media,
            ".pdf" or ".woff" or ".woff2" or ".ttf" or ".otf" or ".eot"
                or ".zip" or ".gz" or ".wasm" => EmbeddedResourceKind.Opaque,
            // .py/.asis/.sub and friends: WPT server-generated resources whose
            // payload is usually markup, and anything else unrecognised.
            _ => EmbeddedResourceKind.Unknown,
        };

    /// <summary>
    /// True when the head of <paramref name="path"/> does not decode as text — a NUL
    /// byte or an invalid UTF-8 sequence. Both are impossible in a text resource and
    /// characteristic of a binary one, so this is the last guard that keeps binary
    /// bytes out of the HTML parser when the file name says nothing.
    /// </summary>
    private static bool LooksBinary(string path)
    {
        const int SniffLength = 1024;
        byte[] head;
        try
        {
            using var stream = File.OpenRead(path);
            head = new byte[(int)Math.Min(SniffLength, Math.Max(stream.Length, 0))];
            int read = stream.Read(head, 0, head.Length);
            if (read < head.Length)
                Array.Resize(ref head, read);
        }
        catch
        {
            return true;
        }

        if (Array.IndexOf(head, (byte)0) >= 0)
            return true;

        try
        {
            // Decode all but a possibly truncated trailing sequence: a multi-byte
            // character straddling the sniff boundary is not evidence of binary.
            int end = head.Length;
            for (int i = 0; i < 4 && end > 0 && (head[end - 1] & 0x80) != 0; i++)
                end--;
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(head, 0, end);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>Escapes <paramref name="value"/> for a double-quoted attribute.</summary>
    private static string AttributeEscaped(string value)
    {
        var sb = new StringBuilder(value.Length);
        AppendXmlEscaped(sb, value);
        return sb.ToString();
    }

    private static bool HasHtmlExtension(string url)
    {
        // Strip any query/fragment before checking the extension.
        int cut = url.IndexOfAny(['?', '#']);
        string path = cut >= 0 ? url[..cut] : url;
        return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".xht", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Serialises an inline <c>&lt;svg&gt;</c> CssBox subtree back to SVG
    /// markup so that PaintWalker can render it via
    /// SvgRenderer.
    /// </summary>
    private static string SerializeSvgSubtree(CssBox svgBox)
    {
        var sb = new StringBuilder();
        SerializeSvgBox(svgBox, sb);
        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static void SerializeSvgBox(CssBox box, StringBuilder sb)
    {
        if (box.HtmlTag != null)
        {
            sb.Append('<').Append(box.HtmlTag.Name);
            if (box.HtmlTag.HasAttributes())
            {
                foreach (var attr in box.HtmlTag.Attributes)
                {
                    sb.Append(' ').Append(attr.Key).Append("=\"");
                    AppendXmlEscaped(sb, attr.Value);
                    sb.Append('"');
                }
            }

            if (box.HtmlTag.IsSingle)
            {
                sb.Append("/>");
                return;
            }

            sb.Append('>');
        }

        if (!box.Text.IsEmpty)
            AppendXmlEscaped(sb, box.Text.ToString());

        foreach (var child in box.Boxes)
            SerializeSvgBox(child, sb);

        if (box.HtmlTag != null && !box.HtmlTag.IsSingle)
            sb.Append("</").Append(box.HtmlTag.Name).Append('>');
    }

    /// <summary>
    /// Appends <paramref name="text"/> to <paramref name="sb"/>, escaping
    /// the five XML special characters: <c>&amp;</c>, <c>&lt;</c>,
    /// <c>&gt;</c>, <c>&quot;</c>, and <c>&apos;</c>.
    /// </summary>
    private static void AppendXmlEscaped(StringBuilder sb, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&':  sb.Append("&amp;");  break;
                case '<':  sb.Append("&lt;");   break;
                case '>':  sb.Append("&gt;");   break;
                case '"':  sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default:   sb.Append(ch);       break;
            }
        }
    }
}
