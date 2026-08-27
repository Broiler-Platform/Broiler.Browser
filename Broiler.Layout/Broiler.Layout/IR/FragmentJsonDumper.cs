using System.Globalization;
using System.Text;

namespace Broiler.Layout.IR;

/// <summary>
/// Serialises a <see cref="Fragment"/> tree to deterministic JSON for
/// golden-file comparison and debugging. Coordinates are rounded to 2
/// decimal places for stability across runs. Object references
/// (<see cref="Fragment.Style"/>, <c>FontHandle</c>, <c>ImageHandle</c>)
/// are excluded.
/// </summary>
public static class FragmentJsonDumper
{
    /// <summary>
    /// Serialises <paramref name="fragment"/> to indented, deterministic JSON.
    /// </summary>
    public static string ToJson(Fragment fragment)
    {
        var sb = new StringBuilder();
        WriteFragment(sb, fragment, indent: 0);
        sb.AppendLine();
        return sb.ToString();
    }

    // ── Fragment ────────────────────────────────────────────────────

    private static void WriteFragment(StringBuilder sb, Fragment f, int indent)
    {
        var pad = new string(' ', indent);
        var pad2 = new string(' ', indent + 2);

        sb.Append(pad).AppendLine("{");

        sb.Append(pad2).Append("\"x\": ").Append(Round(f.Location.X)).AppendLine(",");
        sb.Append(pad2).Append("\"y\": ").Append(Round(f.Location.Y)).AppendLine(",");
        sb.Append(pad2).Append("\"width\": ").Append(Round(f.Size.Width)).AppendLine(",");
        sb.Append(pad2).Append("\"height\": ").Append(Round(f.Size.Height)).AppendLine(",");

        sb.Append(pad2).Append("\"margin\": ");
        WriteBoxEdges(sb, f.Margin);
        sb.AppendLine(",");

        sb.Append(pad2).Append("\"border\": ");
        WriteBoxEdges(sb, f.Border);
        sb.AppendLine(",");

        sb.Append(pad2).Append("\"padding\": ");
        WriteBoxEdges(sb, f.Padding);
        sb.AppendLine(",");

        sb.Append(pad2).Append("\"stackLevel\": ").Append(f.StackLevel).AppendLine(",");
        sb.Append(pad2).Append("\"createsStackingContext\": ")
            .Append(f.CreatesStackingContext ? "true" : "false").AppendLine(",");

        // Lines
        sb.Append(pad2).Append("\"lines\": ");
        if (f.Lines is null || f.Lines.Count == 0)
        {
            sb.AppendLine("[],");
        }
        else
        {
            sb.AppendLine("[");
            for (int i = 0; i < f.Lines.Count; i++)
            {
                WriteLineFragment(sb, f.Lines[i], indent + 4);
                sb.AppendLine(i < f.Lines.Count - 1 ? "," : "");
            }
            sb.Append(pad2).AppendLine("],");
        }

        // Children
        sb.Append(pad2).Append("\"children\": ");
        if (f.Children.Count == 0)
        {
            sb.AppendLine("[]");
        }
        else
        {
            sb.AppendLine("[");
            for (int i = 0; i < f.Children.Count; i++)
            {
                WriteFragment(sb, f.Children[i], indent + 4);
                sb.AppendLine(i < f.Children.Count - 1 ? "," : "");
            }
            sb.Append(pad2).AppendLine("]");
        }

        sb.Append(pad).Append('}');
    }

    // ── LineFragment ───────────────────────────────────────────────

    private static void WriteLineFragment(StringBuilder sb, LineFragment line, int indent)
    {
        var pad = new string(' ', indent);
        var pad2 = new string(' ', indent + 2);

        sb.Append(pad).AppendLine("{");
        sb.Append(pad2).Append("\"x\": ").Append(Round(line.X)).AppendLine(",");
        sb.Append(pad2).Append("\"y\": ").Append(Round(line.Y)).AppendLine(",");
        sb.Append(pad2).Append("\"width\": ").Append(Round(line.Width)).AppendLine(",");
        sb.Append(pad2).Append("\"height\": ").Append(Round(line.Height)).AppendLine(",");
        sb.Append(pad2).Append("\"baseline\": ").Append(Round(line.Baseline)).AppendLine(",");

        sb.Append(pad2).Append("\"inlines\": ");
        if (line.Inlines.Count == 0)
        {
            sb.AppendLine("[]");
        }
        else
        {
            sb.AppendLine("[");
            for (int i = 0; i < line.Inlines.Count; i++)
            {
                WriteInlineFragment(sb, line.Inlines[i], indent + 4);
                sb.AppendLine(i < line.Inlines.Count - 1 ? "," : "");
            }
            sb.Append(pad2).AppendLine("]");
        }

        sb.Append(pad).Append('}');
    }

    // ── InlineFragment ─────────────────────────────────────────────

    private static void WriteInlineFragment(StringBuilder sb, InlineFragment inf, int indent)
    {
        var pad = new string(' ', indent);
        var pad2 = new string(' ', indent + 2);

        sb.Append(pad).AppendLine("{");
        sb.Append(pad2).Append("\"x\": ").Append(Round(inf.X)).AppendLine(",");
        sb.Append(pad2).Append("\"y\": ").Append(Round(inf.Y)).AppendLine(",");
        sb.Append(pad2).Append("\"width\": ").Append(Round(inf.Width)).AppendLine(",");
        sb.Append(pad2).Append("\"height\": ").Append(Round(inf.Height)).AppendLine(",");

        sb.Append(pad2).Append("\"text\": ");
        if (inf.Text is null)
            sb.AppendLine("null");
        else
            sb.Append('"').Append(EscapeJsonString(inf.Text)).AppendLine("\"");

        sb.Append(pad).Append('}');
    }

    // ── BoxEdges ───────────────────────────────────────────────────

    private static void WriteBoxEdges(StringBuilder sb, BoxEdges edges)
    {
        sb.Append("{ \"top\": ").Append(Round(edges.Top))
          .Append(", \"right\": ").Append(Round(edges.Right))
          .Append(", \"bottom\": ").Append(Round(edges.Bottom))
          .Append(", \"left\": ").Append(Round(edges.Left))
          .Append(" }");
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static string Round(double value)
    {
        return Math.Round(value, 2).ToString("G", CultureInfo.InvariantCulture);
    }

    private static string Round(float value)
    {
        return Math.Round(value, 2).ToString("G", CultureInfo.InvariantCulture);
    }

    private static string EscapeJsonString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }
}
