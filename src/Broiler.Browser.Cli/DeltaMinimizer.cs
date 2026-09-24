using System;
using System.Collections.Generic;

namespace Broiler.Cli;

/// <summary>
/// Basic delta-reduction minimizer for HTML documents. Removes child
/// elements one at a time and keeps removals whose violation persists.
/// </summary>
/// <remarks>
/// Restored beside <c>--fuzz-layout</c>, its only caller, for the reason
/// <see cref="HtmlCssGenerator"/> gives: Broiler.HTML deleted it with the generator in 4c5a9d58.
/// </remarks>
internal static class DeltaMinimizer
{
    /// <summary>
    /// Attempts to minimize <paramref name="html"/> while the invariant
    /// violation reported by <paramref name="stillFails"/> persists.
    /// </summary>
    /// <param name="html">The full HTML document.</param>
    /// <param name="stillFails">
    /// A predicate that returns <c>true</c> when the (possibly smaller) HTML
    /// still triggers the layout invariant violation.
    /// </param>
    /// <param name="maxPasses">
    /// Maximum number of full passes over the HTML to attempt. Default 10.
    /// </param>
    /// <returns>The minimized HTML that still triggers the violation.</returns>
    public static string Minimize(string html, Func<string, bool> stillFails, int maxPasses = 10)
    {
        string current = html;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            var children = FindTopLevelDivs(current);
            if (children.Count == 0)
                break;

            bool madeProgress = false;
            // Walk in reverse so index offsets stay valid after removal.
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var (start, length) = children[i];
                string candidate = current.Remove(start, length);
                
                if (!stillFails(candidate))
                    continue;
                
                current = candidate;
                madeProgress = true;
                break; // restart from fresh child list
            }

            if (!madeProgress)
                break;
        }

        return current;
    }

    /// <summary>
    /// Finds top-level <c>&lt;div …&gt;…&lt;/div&gt;</c> ranges inside the
    /// outermost container. This is a simple depth-based scanner suitable
    /// for the well-formed output of <see cref="HtmlCssGenerator"/>.
    /// </summary>
    private static List<(int Start, int Length)> FindTopLevelDivs(string html)
    {
        var results = new List<(int Start, int Length)>();
        // Match balanced <div...>...</div> at the shallowest nesting.
        // We track depth manually.
        int pos = 0;
        while (pos < html.Length)
        {
            int openIdx = html.IndexOf("<div", pos, StringComparison.OrdinalIgnoreCase);
            if (openIdx < 0) break;

            int depth = 0;
            int scan = openIdx;
            int elementEnd = -1;

            while (scan < html.Length)
            {
                int nextOpen = html.IndexOf("<div", scan + 1, StringComparison.OrdinalIgnoreCase);
                int nextClose = html.IndexOf("</div>", scan + (scan == openIdx ? 0 : 1), StringComparison.OrdinalIgnoreCase);

                if (nextClose < 0) break; // malformed

                if (nextOpen >= 0 && nextOpen < nextClose)
                {
                    depth++;
                    scan = nextOpen;
                }
                else
                {
                    if (depth == 0)
                    {
                        elementEnd = nextClose + "</div>".Length;
                        break;
                    }
                    depth--;
                    scan = nextClose + "</div>".Length;
                }
            }

            if (elementEnd > openIdx)
            {
                results.Add((openIdx, elementEnd - openIdx));
                pos = elementEnd;
            }
            else
            {
                pos = openIdx + 1;
            }
        }

        // Skip the first match (outermost wrapper) if there are nested children.
        // The generator wraps everything in a root <div style='width:500px;'>.
        // We want to minimize children inside that wrapper.
        if (results.Count > 1)
        {
            // If the first div spans the entire content, return only inner divs.
            var (Start, Length) = results[0];
            if (Start + Length >= html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase))
            {
                // Return all but the outermost wrapper
                results.RemoveAt(0);
            }
        }

        return results;
    }
}
