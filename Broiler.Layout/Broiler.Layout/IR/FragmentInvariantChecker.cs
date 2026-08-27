namespace Broiler.Layout.IR;

/// <summary>
/// Walks a <see cref="Fragment"/> tree and checks structural layout
/// invariants. Returns violations as plain strings — no test-framework
/// dependency. Phase 3 deliverable (extracted from xUnit-based checker).
/// </summary>
public static class FragmentInvariantChecker
{
    /// <summary>
    /// Checks all layout invariants on the given fragment tree.
    /// Returns a list of violation descriptions (empty = valid).
    /// </summary>
    public static IReadOnlyList<string> Check(Fragment root)
    {
        var violations = new List<string>();
        CheckFragment(root, "root", violations);
        return violations;
    }

    private static void CheckFragment(Fragment f, string path, List<string> violations)
    {
        // 1. No NaN/Inf geometry
        CheckFinite(f.Location.X, $"{path}.Location.X", violations);
        CheckFinite(f.Location.Y, $"{path}.Location.Y", violations);
        CheckFinite(f.Size.Width, $"{path}.Size.Width", violations);
        CheckFinite(f.Size.Height, $"{path}.Size.Height", violations);

        // 2. Non-negative dimensions
        if (f.Size.Width < 0)
            violations.Add($"{path}.Size.Width is negative ({f.Size.Width})");
        if (f.Size.Height < 0)
            violations.Add($"{path}.Size.Height is negative ({f.Size.Height})");

        // 3. BoxEdges finite check
        CheckBoxEdgesFinite(f.Margin, $"{path}.Margin", violations);
        CheckBoxEdgesFinite(f.Border, $"{path}.Border", violations);
        CheckBoxEdgesFinite(f.Padding, $"{path}.Padding", violations);

        // 4. Lines ordered vertically
        if (f.Lines is { Count: > 1 })
        {
            for (int i = 0; i < f.Lines.Count - 1; i++)
            {
                if (f.Lines[i].Y > f.Lines[i + 1].Y)
                {
                    violations.Add(
                        $"{path}.Lines[{i}].Y ({f.Lines[i].Y}) > Lines[{i + 1}].Y ({f.Lines[i + 1].Y}) — lines not ordered vertically");
                }
            }
        }

        // 5. Check line fragments
        if (f.Lines is not null)
        {
            for (int i = 0; i < f.Lines.Count; i++)
            {
                CheckLineFragment(f.Lines[i], $"{path}.Lines[{i}]", violations);
            }
        }

        // 6. Block children stack vertically
        if (f.Children.Count > 1)
        {
            for (int i = 0; i < f.Children.Count - 1; i++)
            {
                var c1 = f.Children[i];
                var c2 = f.Children[i + 1];
                if (c1.Style.Float == "none" && c1.Style.Position == "static" &&
                    c2.Style.Float == "none" && c2.Style.Position == "static" &&
                    c1.Style.Display == "block" && c2.Style.Display == "block")
                {
                    if (c2.Location.Y < c1.Location.Y)
                    {
                        violations.Add(
                            $"{path}.Children[{i + 1}].Y ({c2.Location.Y}) < Children[{i}].Y ({c1.Location.Y}) — block children not stacked vertically");
                    }
                }
            }
        }

        // Recurse into children
        for (int i = 0; i < f.Children.Count; i++)
        {
            CheckFragment(f.Children[i], $"{path}.Children[{i}]", violations);
        }
    }

    private static void CheckLineFragment(LineFragment line, string path, List<string> violations)
    {
        CheckFinite(line.X, $"{path}.X", violations);
        CheckFinite(line.Y, $"{path}.Y", violations);
        CheckFinite(line.Width, $"{path}.Width", violations);
        CheckFinite(line.Height, $"{path}.Height", violations);
        CheckFinite(line.Baseline, $"{path}.Baseline", violations);

        if (line.Width < 0)
            violations.Add($"{path}.Width is negative ({line.Width})");
        if (line.Height < 0)
            violations.Add($"{path}.Height is negative ({line.Height})");

        if (line.Height > 0 && line.Baseline < 0)
            violations.Add($"{path}.Baseline ({line.Baseline}) is negative");
        if (line.Height > 0 && line.Baseline > line.Height)
            violations.Add($"{path}.Baseline ({line.Baseline}) exceeds line Height ({line.Height})");

        for (int i = 0; i < line.Inlines.Count; i++)
        {
            CheckInlineFragment(line.Inlines[i], $"{path}.Inlines[{i}]", violations);
        }
    }

    private static void CheckInlineFragment(InlineFragment inf, string path, List<string> violations)
    {
        CheckFinite(inf.X, $"{path}.X", violations);
        CheckFinite(inf.Y, $"{path}.Y", violations);
        CheckFinite(inf.Width, $"{path}.Width", violations);
        CheckFinite(inf.Height, $"{path}.Height", violations);

        if (inf.Width < 0)
            violations.Add($"{path}.Width is negative ({inf.Width})");
        if (inf.Height < 0)
            violations.Add($"{path}.Height is negative ({inf.Height})");
    }

    private static void CheckBoxEdgesFinite(BoxEdges edges, string path, List<string> violations)
    {
        CheckFinite(edges.Top, $"{path}.Top", violations);
        CheckFinite(edges.Right, $"{path}.Right", violations);
        CheckFinite(edges.Bottom, $"{path}.Bottom", violations);
        CheckFinite(edges.Left, $"{path}.Left", violations);
    }

    private static void CheckFinite(double value, string name, List<string> violations)
    {
        if (double.IsNaN(value))
            violations.Add($"{name} is NaN");
        else if (double.IsInfinity(value))
            violations.Add($"{name} is Infinity");
    }

    private static void CheckFinite(float value, string name, List<string> violations)
    {
        if (float.IsNaN(value))
            violations.Add($"{name} is NaN");
        else if (float.IsInfinity(value))
            violations.Add($"{name} is Infinity");
    }
}
