namespace Broiler.Layout.IR;

/// <summary>
/// Read-only structural queries over a <see cref="Fragment"/> tree: locating a
/// descendant by HTML tag name and selecting the first block-level or first
/// visible child. These are used when resolving which box supplies canvas-level
/// rendering (e.g. CSS2.1 §14.2 background propagation), but carry no
/// paint-specific state, so they can serve any Fragment-tree consumer.
/// </summary>
public static class FragmentQuery
{
    /// <summary>
    /// Searches for a fragment with the given HTML tag name among direct
    /// children and, if not found, recursively through anonymous wrapper
    /// boxes (up to 3 levels deep).  This handles cases where CSS anonymous
    /// block boxing wraps the target element (e.g. body with display:inline
    /// containing block-level children).
    /// </summary>
    public static Fragment? FindFragmentByTag(Fragment parent, string tagName, int depth = 0)
    {
        if (depth > 3) return null;

        foreach (var child in parent.Children)
        {
            if (string.Equals(child.Style.TagName, tagName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        // Recurse into anonymous wrappers (no tag name) to find the target.
        foreach (var child in parent.Children)
        {
            if (child.Style.TagName == null && child.Style.Display != "none")
            {
                var found = FindFragmentByTag(child, tagName, depth + 1);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first child fragment that is a visible block-level element,
    /// skipping <c>display:none</c> children (e.g. <c>&lt;head&gt;</c>).
    /// </summary>
    public static Fragment? FindFirstBlockChild(Fragment parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.Style.Display == "none")
                continue;
            if (child.Style.Display is "block" or "list-item" or "table")
                return child;
        }
        return null;
    }

    /// <summary>
    /// Returns the first visible child fragment regardless of display type.
    /// Used as a fallback when the HTML parser doesn't generate block-level
    /// wrapper elements (<c>&lt;html&gt;</c> / <c>&lt;body&gt;</c>).
    /// </summary>
    public static Fragment? FindFirstVisibleChild(Fragment parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.Style.Display == "none")
                continue;
            return child;
        }
        return null;
    }
}
