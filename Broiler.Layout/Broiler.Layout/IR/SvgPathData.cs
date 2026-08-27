#nullable disable
using System.Drawing;
using System.Globalization;

namespace Broiler.Layout.IR;

/// <summary>
/// Flattens an SVG <c>&lt;path&gt;</c>'s <c>d</c> attribute into straight-line subpaths.
/// </summary>
/// <remarks>
/// <para>
/// <c>&lt;path&gt;</c> is the commonest shape in SVG and the renderer drew <em>none</em> of them:
/// the only pass over <c>&lt;path&gt;</c> harvested each one's start point so a
/// <c>&lt;textPath&gt;</c> could be positioned, and there is no path display item for a backend to
/// draw. Flattening to polylines reuses <see cref="DrawSvgPolygonItem"/> and
/// <see cref="DrawSvgPolylineItem"/>, which the raster backend already fills and strokes, so path
/// support needs no new backend primitive.
/// </para>
/// <para>
/// The output is in the path's own user units; the caller maps it to page space, which is why the
/// curve subdivision is length-based rather than pixel-based — the scale is not known here. The
/// segment counts are chosen so that a curve spanning a whole 480-unit view box is smooth at the
/// sizes WPT renders at.
/// </para>
/// </remarks>
internal static class SvgPathData
{
    /// <summary>One subpath: the points it visits, and whether a <c>Z</c> closed it.</summary>
    internal readonly record struct SubPath(List<PointF> Points, bool Closed);

    /// <summary>Shortest curve subdivision, so even a tiny curve is not a single chord.</summary>
    private const int MinCurveSegments = 8;

    /// <summary>Longest curve subdivision, bounding the work one pathological path can cause.</summary>
    private const int MaxCurveSegments = 96;

    /// <summary>
    /// Parses <paramref name="d"/> and returns its subpaths. Returns an empty list for markup this
    /// cannot follow, so an unparseable path draws nothing rather than drawing something wrong.
    /// </summary>
    public static List<SubPath> Flatten(string d)
    {
        var result = new List<SubPath>();
        if (string.IsNullOrWhiteSpace(d))
            return result;

        var reader = new Reader(d);
        List<PointF> current = null;

        // The current point, the subpath's start (where Z returns to), and the previous segment's
        // second control point — which S and T reflect to get their implied one (SVG 1.1 §8.3.6).
        float cx = 0, cy = 0, startX = 0, startY = 0;
        float lastControlX = 0, lastControlY = 0;
        char lastCommand = '\0';

        void Begin(float x, float y)
        {
            Flush(false);
            current = [new PointF(x, y)];
            startX = x;
            startY = y;
        }

        void Flush(bool closed)
        {
            if (current is { Count: > 1 })
                result.Add(new SubPath(current, closed));
            current = null;
        }

        void LineTo(float x, float y)
        {
            // A path may begin with a command other than M; SVG treats that as an error, but a
            // subpath rooted at the origin is closer to the intent than dropping the whole path.
            current ??= [new PointF(cx, cy)];
            current.Add(new PointF(x, y));
        }

        while (reader.TryReadCommand(out char command))
        {
            bool relative = char.IsLower(command);
            char kind = char.ToUpperInvariant(command);

            // Z takes no parameters; every other command takes at least one parameter set, and
            // repeats for as many further sets as follow it.
            if (kind == 'Z')
            {
                if (current is { Count: > 1 })
                {
                    Flush(true);
                    cx = startX;
                    cy = startY;
                }

                lastCommand = kind;
                continue;
            }

            bool first = true;
            while (true)
            {
                if (!reader.PeekIsNumber())
                {
                    // A command letter with no parameters at all is malformed; one that has already
                    // consumed a set has simply run out of repeats.
                    break;
                }

                switch (kind)
                {
                    case 'M':
                    {
                        if (!reader.TryReadPoint(out float x, out float y))
                            goto done;

                        if (relative) { x += cx; y += cy; }

                        // Only the first pair of an M is a moveto; the rest are implicit linetos
                        // into the same subpath (SVG 1.1 §8.3.2).
                        if (first)
                            Begin(x, y);
                        else
                            LineTo(x, y);

                        cx = x; cy = y;
                        break;
                    }

                    case 'L':
                    {
                        if (!reader.TryReadPoint(out float x, out float y))
                            goto done;

                        if (relative) { x += cx; y += cy; }
                        LineTo(x, y);
                        cx = x; cy = y;
                        break;
                    }

                    case 'H':
                    {
                        if (!reader.TryReadNumber(out float x))
                            goto done;

                        if (relative) x += cx;
                        LineTo(x, cy);
                        cx = x;
                        break;
                    }

                    case 'V':
                    {
                        if (!reader.TryReadNumber(out float y))
                            goto done;

                        if (relative) y += cy;
                        LineTo(cx, y);
                        cy = y;
                        break;
                    }

                    case 'C':
                    case 'S':
                    {
                        float x1, y1;
                        if (kind == 'C')
                        {
                            if (!reader.TryReadPoint(out x1, out y1))
                                goto done;
                            if (relative) { x1 += cx; y1 += cy; }
                        }
                        else
                        {
                            // S implies the reflection of the previous cubic's second control point
                            // about the current point; after anything else it is the current point.
                            bool reflect = lastCommand is 'C' or 'S';
                            x1 = reflect ? 2 * cx - lastControlX : cx;
                            y1 = reflect ? 2 * cy - lastControlY : cy;
                        }

                        if (!reader.TryReadPoint(out float x2, out float y2)
                            || !reader.TryReadPoint(out float x, out float y))
                        {
                            goto done;
                        }

                        if (relative) { x2 += cx; y2 += cy; x += cx; y += cy; }

                        AppendCubic(ref current, cx, cy, x1, y1, x2, y2, x, y);
                        lastControlX = x2; lastControlY = y2;
                        cx = x; cy = y;
                        lastCommand = kind;
                        break;
                    }

                    case 'Q':
                    case 'T':
                    {
                        float qx, qy;
                        if (kind == 'Q')
                        {
                            if (!reader.TryReadPoint(out qx, out qy))
                                goto done;
                            if (relative) { qx += cx; qy += cy; }
                        }
                        else
                        {
                            bool reflect = lastCommand is 'Q' or 'T';
                            qx = reflect ? 2 * cx - lastControlX : cx;
                            qy = reflect ? 2 * cy - lastControlY : cy;
                        }

                        if (!reader.TryReadPoint(out float x, out float y))
                            goto done;

                        if (relative) { x += cx; y += cy; }

                        AppendQuadratic(ref current, cx, cy, qx, qy, x, y);
                        lastControlX = qx; lastControlY = qy;
                        cx = x; cy = y;
                        lastCommand = kind;
                        break;
                    }

                    case 'A':
                    {
                        if (!reader.TryReadNumber(out float rx)
                            || !reader.TryReadNumber(out float ry)
                            || !reader.TryReadNumber(out float rotation)
                            || !reader.TryReadFlag(out bool largeArc)
                            || !reader.TryReadFlag(out bool sweep)
                            || !reader.TryReadPoint(out float x, out float y))
                        {
                            goto done;
                        }

                        if (relative) { x += cx; y += cy; }

                        AppendArc(ref current, cx, cy, rx, ry, rotation, largeArc, sweep, x, y);
                        cx = x; cy = y;
                        break;
                    }

                    default:
                        // An unknown command letter: the rest of the data is unreliable, so stop
                        // rather than mis-attribute its numbers to the wrong command.
                        goto done;
                }

                if (kind is not ('C' or 'S' or 'Q' or 'T'))
                    lastCommand = kind;

                first = false;
            }
        }

    done:
        Flush(false);
        return result;

        void AppendCubic(
            ref List<PointF> points, float x0, float y0,
            float x1, float y1, float x2, float y2, float x3, float y3)
        {
            points ??= [new PointF(x0, y0)];
            int steps = SegmentsFor(
                Distance(x0, y0, x1, y1) + Distance(x1, y1, x2, y2) + Distance(x2, y2, x3, y3));

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1 - t;
                float a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, e = t * t * t;
                points.Add(new PointF(
                    a * x0 + b * x1 + c * x2 + e * x3,
                    a * y0 + b * y1 + c * y2 + e * y3));
            }
        }

        void AppendQuadratic(
            ref List<PointF> points, float x0, float y0, float qx, float qy, float x1, float y1)
        {
            points ??= [new PointF(x0, y0)];
            int steps = SegmentsFor(Distance(x0, y0, qx, qy) + Distance(qx, qy, x1, y1));

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1 - t;
                points.Add(new PointF(
                    u * u * x0 + 2 * u * t * qx + t * t * x1,
                    u * u * y0 + 2 * u * t * qy + t * t * y1));
            }
        }

        // SVG 1.1 §F.6.5: the endpoint parameterisation the `A` command uses converted to the
        // centre parameterisation the sampling needs.
        void AppendArc(
            ref List<PointF> points, float x0, float y0,
            float rx, float ry, float rotationDegrees, bool largeArc, bool sweep, float x1, float y1)
        {
            points ??= [new PointF(x0, y0)];

            // Out-of-range radii: zero means a straight line, negatives take their magnitude
            // (§F.6.6), and radii too small to span the endpoints are scaled up until they fit.
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx == 0 || ry == 0 || (x0 == x1 && y0 == y1))
            {
                points.Add(new PointF(x1, y1));
                return;
            }

            double phi = rotationDegrees * Math.PI / 180.0;
            double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);

            double dx2 = (x0 - x1) / 2.0, dy2 = (y0 - y1) / 2.0;
            double x1p = cosPhi * dx2 + sinPhi * dy2;
            double y1p = -sinPhi * dx2 + cosPhi * dy2;

            double lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
            if (lambda > 1)
            {
                double scale = Math.Sqrt(lambda);
                rx = (float)(rx * scale);
                ry = (float)(ry * scale);
            }

            double sign = largeArc == sweep ? -1 : 1;
            double numerator = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
            double denominator = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
            double coefficient = denominator == 0 ? 0 : sign * Math.Sqrt(Math.Max(0, numerator / denominator));

            double cxp = coefficient * rx * y1p / ry;
            double cyp = -coefficient * ry * x1p / rx;

            double centreX = cosPhi * cxp - sinPhi * cyp + (x0 + x1) / 2.0;
            double centreY = sinPhi * cxp + cosPhi * cyp + (y0 + y1) / 2.0;

            double startAngle = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            double sweepAngle = Angle(
                (x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);

            if (!sweep && sweepAngle > 0)
                sweepAngle -= 2 * Math.PI;
            else if (sweep && sweepAngle < 0)
                sweepAngle += 2 * Math.PI;

            int steps = SegmentsFor((float)(Math.Abs(sweepAngle) * Math.Max(rx, ry)));
            for (int i = 1; i <= steps; i++)
            {
                double angle = startAngle + sweepAngle * i / steps;
                double px = rx * Math.Cos(angle), py = ry * Math.Sin(angle);
                points.Add(new PointF(
                    (float)(cosPhi * px - sinPhi * py + centreX),
                    (float)(sinPhi * px + cosPhi * py + centreY)));
            }
        }
    }

    /// <summary>The angle from one vector to another, signed, as §F.6.5's <c>θ</c> function.</summary>
    private static double Angle(double ux, double uy, double vx, double vy)
    {
        double dot = ux * vx + uy * vy;
        double lengths = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        if (lengths == 0)
            return 0;

        double angle = Math.Acos(Math.Clamp(dot / lengths, -1.0, 1.0));
        return ux * vy - uy * vx < 0 ? -angle : angle;
    }

    private static float Distance(float x0, float y0, float x1, float y1)
        => MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    /// <summary>How many chords a curve of the given rough length is flattened into.</summary>
    private static int SegmentsFor(float length)
        => Math.Clamp((int)MathF.Ceiling(length / 2f), MinCurveSegments, MaxCurveSegments);

    /// <summary>
    /// A reader over path data. Numbers may run together without separators
    /// (<c>10-20</c>, <c>.5.5</c>) and the arc flags are single digits that need no separator at
    /// all, so the scanning is done here rather than by splitting on whitespace and commas.
    /// </summary>
    private sealed class Reader(string text)
    {
        private readonly string _text = text;
        private int _index;

        private void SkipSeparators()
        {
            while (_index < _text.Length && (char.IsWhiteSpace(_text[_index]) || _text[_index] == ','))
                _index++;
        }

        public bool TryReadCommand(out char command)
        {
            SkipSeparators();
            if (_index < _text.Length && char.IsLetter(_text[_index]))
            {
                command = _text[_index++];
                return true;
            }

            command = '\0';
            return false;
        }

        /// <summary>Whether a number starts here, without consuming it.</summary>
        public bool PeekIsNumber()
        {
            SkipSeparators();
            if (_index >= _text.Length)
                return false;

            char c = _text[_index];
            return char.IsAsciiDigit(c) || c is '-' or '+' or '.';
        }

        public bool TryReadNumber(out float value)
        {
            SkipSeparators();
            value = 0;
            int start = _index;

            if (_index < _text.Length && (_text[_index] == '-' || _text[_index] == '+'))
                _index++;

            while (_index < _text.Length && char.IsAsciiDigit(_text[_index]))
                _index++;

            if (_index < _text.Length && _text[_index] == '.')
            {
                _index++;
                while (_index < _text.Length && char.IsAsciiDigit(_text[_index]))
                    _index++;
            }

            // An exponent only counts when digits actually follow it, so the `e` of a stray token is
            // not swallowed into the number.
            if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
            {
                int save = _index;
                _index++;
                if (_index < _text.Length && (_text[_index] == '-' || _text[_index] == '+'))
                    _index++;

                if (_index < _text.Length && char.IsAsciiDigit(_text[_index]))
                {
                    while (_index < _text.Length && char.IsAsciiDigit(_text[_index]))
                        _index++;
                }
                else
                {
                    _index = save;
                }
            }

            if (_index == start)
                return false;

            if (float.TryParse(
                _text.AsSpan(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture,
                out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryReadPoint(out float x, out float y)
        {
            y = 0;
            return TryReadNumber(out x) && TryReadNumber(out y);
        }

        /// <summary>
        /// An arc flag: exactly one character, <c>0</c> or <c>1</c>. Read separately because
        /// <c>a1 1 0 011 1</c> is legal and a general number scan would take <c>011</c> as one value.
        /// </summary>
        public bool TryReadFlag(out bool value)
        {
            SkipSeparators();
            if (_index < _text.Length && (_text[_index] == '0' || _text[_index] == '1'))
            {
                value = _text[_index++] == '1';
                return true;
            }

            value = false;
            return false;
        }
    }
}
