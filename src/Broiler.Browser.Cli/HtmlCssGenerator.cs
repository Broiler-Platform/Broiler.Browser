using System;
using System.Text;

namespace Broiler.Cli;

/// <summary>
/// Generates random but well-formed HTML/CSS documents targeting layout stress
/// parameters: floats, clears, widths/heights, box model properties, display
/// values, and nesting. Used by fuzz testing.
/// </summary>
/// <remarks>
/// <c>--fuzz-layout</c> is the only thing that uses this, so it lives beside it. It was
/// <c>Broiler.HTML.Core.IR.HtmlCssGenerator</c> until Broiler.HTML deleted it as dead code in
/// 4c5a9d58 (2026-09-15): the command line that called it was still in the Broiler repository,
/// where nothing in Broiler.HTML could see it. Restored unchanged apart from the namespace.
/// </remarks>
internal sealed class HtmlCssGenerator
{
    private readonly Random _rng;

    /// <summary>The random seed used for this generator instance.</summary>
    public int Seed { get; }

    /// <summary>
    /// Creates a new generator with an optional deterministic seed.
    /// </summary>
    public HtmlCssGenerator(int? seed = null)
    {
        Seed = seed ?? Environment.TickCount;
        _rng = new Random(Seed);
    }

    /// <summary>
    /// Generates a single random HTML document with inline CSS styles.
    /// The document has a root container <c>&lt;div&gt;</c> with a fixed
    /// width of 500 px, containing 1–6 randomly styled child trees up
    /// to 4 levels deep.
    /// </summary>
    public string Generate()
    {
        var sb = new StringBuilder();
        sb.Append("<html><body>");
        sb.Append("<div style='width:500px;'>");

        int childCount = _rng.Next(1, 7); // 1–6
        int maxDepth = _rng.Next(1, 5);   // 1–4
        for (int i = 0; i < childCount; i++)
        {
            GenerateElement(sb, depth: 1, maxDepth: maxDepth);
        }

        sb.Append("</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private void GenerateElement(StringBuilder sb, int depth, int maxDepth)
    {
        string tag = "div";
        sb.Append('<').Append(tag);
        sb.Append(" style='").Append(GenerateStyle()).Append('\'');
        sb.Append('>');

        if (depth < maxDepth && _rng.Next(100) < 70) // 70 % chance of children
        {
            int childCount = _rng.Next(1, 7);
            for (int i = 0; i < childCount; i++)
            {
                GenerateElement(sb, depth + 1, maxDepth);
            }
        }
        else if (_rng.Next(100) < 40) // 40 % chance of text leaf
        {
            sb.Append("text");
        }

        sb.Append("</").Append(tag).Append('>');
    }

    private string GenerateStyle()
    {
        var sb = new StringBuilder();

        // display
        if (_rng.Next(100) < 50)
        {
            sb.Append("display:").Append(Pick("block", "inline", "inline-block")).Append(';');
        }

        // float
        if (_rng.Next(100) < 30)
        {
            sb.Append("float:").Append(Pick("left", "right", "none")).Append(';');
        }

        // clear
        if (_rng.Next(100) < 20)
        {
            sb.Append("clear:").Append(Pick("left", "right", "both", "none")).Append(';');
        }

        // width
        if (_rng.Next(100) < 60)
        {
            sb.Append("width:").Append(RandomDimension()).Append(';');
        }

        // height
        if (_rng.Next(100) < 40)
        {
            sb.Append("height:").Append(RandomDimension()).Append(';');
        }

        // padding
        if (_rng.Next(100) < 30)
        {
            sb.Append("padding:").Append(RandomPixelValue()).Append("px;");
        }

        // margin
        if (_rng.Next(100) < 30)
        {
            sb.Append("margin:").Append(RandomPixelValue()).Append("px;");
        }

        // border
        if (_rng.Next(100) < 20)
        {
            sb.Append("border:").Append(RandomBorderWidth()).Append("px solid black;");
        }

        return sb.ToString();
    }

    private string RandomDimension()
    {
        int choice = _rng.Next(3);
        return choice switch
        {
            0 => $"{_rng.Next(10, 401)}px",  // 10–400 px
            1 => $"{_rng.Next(10, 101)}%",   // 10–100 %
            _ => "auto",
        };
    }

    private int RandomPixelValue() => _rng.Next(0, 31); // 0–30 px
    private int RandomBorderWidth() => _rng.Next(1, 6); // 1–5 px

    private string Pick(params string[] options) =>
        options[_rng.Next(options.Length)];
}
