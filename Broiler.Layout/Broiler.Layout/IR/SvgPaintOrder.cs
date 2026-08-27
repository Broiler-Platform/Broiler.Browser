#nullable disable
using System.Drawing;

namespace Broiler.Layout.IR;

/// <summary>
/// Collects each element's display items against the document offset of its start tag, so the
/// finished list paints in document order however the per-element passes are sequenced.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SvgRenderer"/> finds elements with one regex per shape type, each sweeping the whole
/// markup — so every <c>&lt;rect&gt;</c> was emitted, then every <c>&lt;circle&gt;</c>, and text
/// last of all. SVG paints in document order (SVG 1.1 §3.3), and a display list is drawn in the
/// order it is built, so an element that a later sibling must cover was painted <em>over</em> that
/// sibling instead: in <c>conformance-checkers/html-svg/color-prop-04-t</c> the dropdown panel's
/// opaque rectangles sit after the paragraph text in the markup and must hide it, and the text
/// showed through them.
/// </para>
/// <para>
/// Segments rather than single items, because one element can emit several — a pattern fill is a
/// clip, its tiles and a restore, and a blend mode wraps its shape in a layer pair — and those must
/// stay adjacent and in the order the element produced them. Sorting is stable on the offset so a
/// container and the content it expands to (a <c>&lt;use&gt;</c> and its referent) keep the order
/// they were opened in when both report the same offset.
/// </para>
/// </remarks>
internal sealed class SvgPaintOrder
{
    private readonly List<Segment> _segments = [];

    private sealed class Segment(int offset, int sequence, List<DisplayItem> items)
    {
        public int Offset { get; } = offset;

        public int Sequence { get; } = sequence;

        public List<DisplayItem> Items { get; } = items;

        /// <summary>The region this element's drawing is clipped to, when it has one.</summary>
        public RectangleF? Clip { get; set; }
    }

    /// <summary>
    /// Clips everything the most recently opened element emits to <paramref name="rect"/>.
    /// </summary>
    /// <remarks>
    /// Recorded against the segment rather than pushed into it by the caller so that the clip
    /// encloses <em>all</em> of the element's items however many it turns out to emit — a pattern
    /// fill's tiles and a blend layer's pair as well as the shape itself.
    /// </remarks>
    public void ClipLast(RectangleF rect)
    {
        if (_segments.Count > 0)
            _segments[^1].Clip = rect;
    }

    /// <summary>
    /// Opens a buffer for the element whose start tag begins at <paramref name="documentOffset"/>.
    /// The caller fills it; it is placed by offset when the list is flushed.
    /// </summary>
    public List<DisplayItem> Open(int documentOffset)
    {
        var items = new List<DisplayItem>();
        _segments.Add(new Segment(documentOffset, _segments.Count, items));
        return items;
    }

    /// <summary>Appends every segment to <paramref name="destination"/> in document order.</summary>
    public void Flush(List<DisplayItem> destination)
    {
        // List.Sort is unstable, so the open sequence is part of the key rather than relied on.
        _segments.Sort(static (a, b) =>
            a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : a.Sequence.CompareTo(b.Sequence));

        foreach (var segment in _segments)
        {
            if (segment.Items.Count == 0)
                continue;

            if (segment.Clip is not { } clip)
            {
                destination.AddRange(segment.Items);
                continue;
            }

            destination.Add(new ClipItem { Bounds = clip, ClipRect = clip });
            destination.AddRange(segment.Items);
            destination.Add(new RestoreItem { Bounds = clip });
        }
    }
}
