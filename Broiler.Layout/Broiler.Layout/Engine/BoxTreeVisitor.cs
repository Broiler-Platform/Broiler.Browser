namespace Broiler.Layout.Engine;

/// <summary>
/// Provides a visitor-pattern API for traversing the <see cref="CssBox"/>
/// tree in a read-only fashion.  External consumers subclass this type and
/// override the <see cref="VisitBox"/> method to inspect each node.
/// </summary>
internal abstract class BoxTreeVisitor
{
    /// <summary>
    /// Called for every <see cref="CssBox"/> in the tree.  Return
    /// <c>true</c> to continue descending into the box's children, or
    /// <c>false</c> to skip the subtree.
    /// </summary>
    /// <param name="box">The current box being visited.</param>
    /// <param name="depth">Zero-based depth of <paramref name="box"/> relative to the traversal root.</param>
    /// <returns><c>true</c> to visit children; <c>false</c> to skip.</returns>
    protected abstract bool VisitBox(CssBox box, int depth);

    /// <summary>
    /// Walks the tree rooted at <paramref name="root"/> depth-first,
    /// invoking <see cref="VisitBox"/> for each box.
    /// </summary>
    public void Walk(CssBox root)
    {
        ArgumentNullException.ThrowIfNull(root);
        WalkCore(root, 0);
    }

    private void WalkCore(CssBox box, int depth)
    {
        if (!VisitBox(box, depth))
            return;

        foreach (var child in box.Boxes)
            WalkCore(child, depth + 1);
    }

    /// <summary>
    /// Collects every box in the tree into a flat list using depth-first order.
    /// </summary>
    public static List<CssBox> Flatten(CssBox root)
    {
        ArgumentNullException.ThrowIfNull(root);
        
        var result = new List<CssBox>();
        FlattenCore(root, result);
        
        return result;
    }

    private static void FlattenCore(CssBox box, List<CssBox> result)
    {
        result.Add(box);
        
        foreach (var child in box.Boxes)
            FlattenCore(child, result);
    }
}
