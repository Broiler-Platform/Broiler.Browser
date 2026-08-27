#nullable disable
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using Broiler.CSS;
using Broiler.Graphics;


namespace Broiler.Layout.Engine;

/// <summary>
/// Resolves CSS animation keyframe values for static rendering.
/// For a static renderer (single-frame snapshot), this computes the animated
/// property values at the effective start time (t=0 + delay offset).
/// </summary>
internal static class CssAnimationResolver
{
    private const double WptSnapshotFrameRateHz = 60.0;
    private static readonly ConditionalWeakTable<CssStyleSheet, IReadOnlyDictionary<string, AnimationRule>> RuleCache = [];

    private sealed record AnimationStop(double Offset, Dictionary<string, string> Properties);
    private sealed record AnimationRule(IReadOnlyList<AnimationStop> Stops);

    /// <summary>
    /// If the box has an <c>animation-name</c> that maps to a known
    /// <c>@keyframes</c> rule in the CSS data, compute the animated property
    /// values and apply them to the box.
    /// </summary>
    public static void ResolveAnimations(CssBox box, CssStyleSheet styleSheet)
    {
        string animName = box.AnimationName;

        if (string.IsNullOrEmpty(animName) || animName.Equals("none", StringComparison.OrdinalIgnoreCase))
            return;

        var rules = RuleCache.GetValue(styleSheet, BuildAnimationRules);

        if (!rules.TryGetValue(animName, out var rule) || rule.Stops.Count == 0)
            return;

        // Compute animation progress for static rendering.
        double durationSeconds = ParseTimeValue(box.AnimationDuration);
        double delaySeconds = ParseTimeValue(box.AnimationDelay);
        string timingFunction = box.AnimationTimingFunction;

        // Sample the animation one 60 Hz frame (~16.67 ms) after it becomes
        // active so static renders line up more closely with the first
        // post-load Chromium screenshot taken by the non-JS WPT runner.
        const double snapshotLeadSeconds = 1.0 / WptSnapshotFrameRateHz;
        double elapsedSeconds = snapshotLeadSeconds - delaySeconds;

        if (elapsedSeconds < 0)
            elapsedSeconds = 0;

        // Compute the raw progress (0.0 to 1.0)
        double progress;

        if (durationSeconds <= 0)
        {
            // Zero or negative duration means the animation completes instantly.
            progress = 1.0;
        }
        else
        {
            progress = elapsedSeconds / durationSeconds;
            progress = Math.Clamp(progress, 0.0, 1.0);
        }

        // Apply the timing function
        double easedProgress = ApplyTimingFunction(progress, timingFunction);

        // Interpolate and apply animated properties
        ApplyInterpolatedProperties(box, rule, easedProgress);
    }

    private static IReadOnlyDictionary<string, AnimationRule> BuildAnimationRules(CssStyleSheet styleSheet)
    {
        var result = new Dictionary<string, AnimationRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var atRule in styleSheet.Rules.OfType<CssAtRule>())
        {
            if (atRule.Name is not ("keyframes" or "-webkit-keyframes") || string.IsNullOrWhiteSpace(atRule.Prelude))
                continue;

            var stops = new List<AnimationStop>();

            foreach (var styleRule in atRule.Rules.OfType<CssStyleRule>())
            {
                var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var declaration in styleRule.Declarations.Declarations)
                    properties[declaration.Name] = declaration.Value.Text;

                foreach (var selector in styleRule.Selectors.Selectors)
                {
                    if (TryParseKeyframeOffset(selector.Text, out var offset))
                        stops.Add(new AnimationStop(offset, new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase)));
                }
            }

            result[Unquote(atRule.Prelude.Trim())] = new AnimationRule([.. stops.OrderBy(static stop => stop.Offset)]);
        }

        return result;
    }

    private static bool TryParseKeyframeOffset(string value, out double offset)
    {
        value = value.Trim();
        if (value.Equals("from", StringComparison.OrdinalIgnoreCase))
        {
            offset = 0;
            return true;
        }
        if (value.Equals("to", StringComparison.OrdinalIgnoreCase))
        {
            offset = 1;
            return true;
        }
        if (value.EndsWith('%') &&
            double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            offset = Math.Clamp(percent / 100d, 0, 1);
            return true;
        }

        offset = 0;
        return false;
    }

    private static string Unquote(string value) => 
        value.Length >= 2 && 
        ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary>
    /// Parses a CSS time value (e.g. "500ms", "1.5s", "-500000s") into seconds.
    /// </summary>
    private static double ParseTimeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        value = value.Trim().ToLowerInvariant();

        if (value.EndsWith("ms"))
        {
            if (double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
                return ms / 1000.0;
        }
        else if (value.EndsWith('s'))
        {
            if (double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
                return s;
        }

        return 0;
    }

    /// <summary>
    /// Applies a CSS timing function to a raw progress value.
    /// Supports: ease, linear, ease-in, ease-out, ease-in-out, cubic-bezier(...)
    /// </summary>
    private static double ApplyTimingFunction(double progress, string timingFunction)
    {
        if (string.IsNullOrEmpty(timingFunction))
            return CubicBezier(progress, 0.25, 0.1, 0.25, 1.0); // ease

        var lower = timingFunction.Trim().ToLowerInvariant();

        return lower switch
        {
            "linear" => progress,
            "ease" => CubicBezier(progress, 0.25, 0.1, 0.25, 1.0),
            "ease-in" => CubicBezier(progress, 0.42, 0.0, 1.0, 1.0),
            "ease-out" => CubicBezier(progress, 0.0, 0.0, 0.58, 1.0),
            "ease-in-out" => CubicBezier(progress, 0.42, 0.0, 0.58, 1.0),
            _ when lower.StartsWith("cubic-bezier(") => ParseAndApplyCubicBezier(progress, lower),
            _ => progress // fallback to linear for unsupported functions
        };
    }

    /// <summary>
    /// Parses "cubic-bezier(x1,y1,x2,y2)" and evaluates the bezier curve.
    /// </summary>
    private static double ParseAndApplyCubicBezier(double progress, string value)
    {
        // Extract the parameters from "cubic-bezier(x1, y1, x2, y2)"
        int start = value.IndexOf('(');
        int end = value.LastIndexOf(')');
        
        if (start < 0 || end <= start)
            return progress;

        var paramsStr = value.Substring(start + 1, end - start - 1);
        var parts = paramsStr.Split(',', StringSplitOptions.TrimEntries);
        
        if (parts.Length != 4)
            return progress;

        if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x1) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y1) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x2) &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y2))
        {
            return CubicBezier(progress, x1, y1, x2, y2);
        }

        return progress;
    }

    /// <summary>
    /// Evaluates a cubic bezier curve for the given input progress.
    /// Uses Newton-Raphson iteration to solve for the parametric t
    /// that yields the desired x value, then returns y(t).
    /// </summary>
    private static double CubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        // Solve for t where bezierX(t) = x using Newton-Raphson
        double t = x; // initial guess

        for (int i = 0; i < 20; i++)
        {
            double xt = BezierComponent(t, x1, x2);
            double dx = xt - x;

            if (Math.Abs(dx) < 1e-7)
                break;

            double dxdt = BezierDerivative(t, x1, x2);

            if (Math.Abs(dxdt) < 1e-7)
                break;

            t -= dx / dxdt;
            t = Math.Clamp(t, 0, 1);
        }

        return BezierComponent(t, y1, y2);
    }

    /// <summary>
    /// Computes the value of a cubic bezier component at parametric t.
    /// B(t) = 3(1-t)²t·p1 + 3(1-t)t²·p2 + t³
    /// </summary>
    private static double BezierComponent(double t, double p1, double p2)
    {
        double omt = 1.0 - t;
        return 3.0 * omt * omt * t * p1
             + 3.0 * omt * t * t * p2
             + t * t * t;
    }

    /// <summary>
    /// Derivative of the cubic bezier component with respect to t.
    /// B'(t) = 3(1-t)²·p1 + 6(1-t)t·(p2-p1) + 3t²·(1-p2)
    /// </summary>
    private static double BezierDerivative(double t, double p1, double p2)
    {
        double omt = 1.0 - t;
        return 3.0 * omt * omt * p1
             + 6.0 * omt * t * (p2 - p1)
             + 3.0 * t * t * (1.0 - p2);
    }

    /// <summary>
    /// Applies interpolated property values from keyframe stops to the box.
    /// </summary>
    private static void ApplyInterpolatedProperties(CssBox box, AnimationRule rule, double progress)
    {
        // Collect all animatable property names across all stops
        var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stop in rule.Stops)
        {
            foreach (var key in stop.Properties.Keys)
                propertyNames.Add(key);
        }

        foreach (var propName in propertyNames)
        {
            // Find the two surrounding keyframe stops for this property
            AnimationStop before = null;
            AnimationStop after = null;

            for (int i = 0; i < rule.Stops.Count; i++)
            {
                var stop = rule.Stops[i];
                
                if (!stop.Properties.ContainsKey(propName))
                    continue;

                if (stop.Offset <= progress)
                    before = stop;

                if (stop.Offset >= progress && after == null)
                    after = stop;
            }

            // If we only have one boundary, use it directly
            if (before == null && after == null)
                continue;

            if (before == null)
                before = after;
            else if (after == null)
                after = before;

            string fromValue = before.Properties[propName];
            string toValue = after.Properties[propName];

            string resolved;
            if (before == after || Math.Abs(before.Offset - after.Offset) < 1e-7)
            {
                resolved = fromValue;
            }
            else
            {
                // Compute the local progress between the two stops
                double localProgress = (progress - before.Offset) / (after.Offset - before.Offset);
                localProgress = Math.Clamp(localProgress, 0.0, 1.0);

                resolved = InterpolateValue(propName, fromValue, toValue, localProgress);
            }

            CssUtils.SetPropertyValue(box, propName, resolved);
        }
    }

    /// <summary>
    /// Interpolates between two CSS property values at a given progress.
    /// Currently supports color interpolation for background-color and color properties.
    /// Falls back to discrete switching at 50% for unsupported types.
    /// </summary>
    private static string InterpolateValue(string propName, string fromValue, string toValue, double progress)
    {
        // Normalise property name for comparison
        var lower = propName.ToLowerInvariant();

        // Color properties: interpolate in RGBA space
        if (lower is "background-color" or "color" or "border-top-color" or "border-right-color"
            or "border-bottom-color" or "border-left-color" or "background"
            or "outline-color" or "text-decoration-color")
        {
            return InterpolateColor(fromValue, toValue, progress);
        }

        // Discrete: use "from" for < 50%, "to" for >= 50%
        return progress < 0.5 ? fromValue : toValue;
    }

    /// <summary>
    /// Interpolates between two CSS color values and returns an rgb() string.
    /// </summary>
    private static string InterpolateColor(string fromValue, string toValue, double progress)
    {
        if (!TryParseColor(fromValue, out var from) || !TryParseColor(toValue, out var to))
            return progress < 0.5 ? fromValue : toValue;

        int r = (int)Math.Round(from.R + (to.R - from.R) * progress);
        int g = (int)Math.Round(from.G + (to.G - from.G) * progress);
        int b = (int)Math.Round(from.B + (to.B - from.B) * progress);
        int a = (int)Math.Round(from.A + (to.A - from.A) * progress);

        r = Math.Clamp(r, 0, 255);
        g = Math.Clamp(g, 0, 255);
        b = Math.Clamp(b, 0, 255);
        a = Math.Clamp(a, 0, 255);

        if (a == 255)
            return $"rgb({r}, {g}, {b})";
        else
            return $"rgba({r}, {g}, {b}, {(a / 255.0).ToString("G4", CultureInfo.InvariantCulture)})";
    }

    /// <summary>
    /// Attempts to parse a CSS color value into a <see cref="Color"/>.
    /// Handles rgb(), rgba(), hex, and named colors.
    /// </summary>
    private static bool TryParseColor(string value, out BColor color)
    {
        color = BColor.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim().ToLowerInvariant();

        if (value == "transparent")
        {
            color = BColor.FromArgb(0, 0, 0, 0);
            return true;
        }

        // Try rgb(r, g, b) / rgba(r, g, b, a)
        if (value.StartsWith("rgb"))
        {
            int paren = value.IndexOf('(');
            int end = value.IndexOf(')');

            if (paren >= 0 && end > paren)
            {
                var inner = value.Substring(paren + 1, end - paren - 1);
                // Support both comma and space separators
                var parts = inner.Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                
                if (parts.Length >= 3)
                {
                    if (int.TryParse(parts[0], out int r) &&
                        int.TryParse(parts[1], out int g) &&
                        int.TryParse(parts[2], out int b))
                    {
                        int a = 255;
                        
                        if (parts.Length >= 4)
                        {
                            if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha))
                                a = (int)Math.Round(alpha <= 1.0 ? alpha * 255 : alpha);
                        }
                        
                        color = BColor.FromArgb(a, r, g, b);
                        return true;
                    }
                }
            }
        }

        // Try hex: #RGB, #RRGGBB, #RRGGBBAA
        if (value.StartsWith('#'))
        {
            string hex = value[1..];
            
            if (hex.Length == 3)
            {
                int r = Convert.ToInt32(new string(hex[0], 2), 16);
                int g = Convert.ToInt32(new string(hex[1], 2), 16);
                int b = Convert.ToInt32(new string(hex[2], 2), 16);
                
                color = BColor.FromArgb(255, r, g, b);
                return true;
            }
            
            if (hex.Length == 6)
            {
                int r = Convert.ToInt32(hex[..2], 16);
                int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                
                color = BColor.FromArgb(255, r, g, b);
                return true;
            }
            
            if (hex.Length == 8)
            {
                int r = Convert.ToInt32(hex[..2], 16);
                int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                int a = Convert.ToInt32(hex.Substring(6, 2), 16);
                color = BColor.FromArgb(a, r, g, b);
                return true;
            }
        }

        // System colors (e.g. Field, FieldText) — CSS-native, no rendering deps.
        if (CssSystemColors.TryResolve(value, out CssColor sys))
        {
            color = BColor.FromArgb(sys.Alpha, sys.Red, sys.Green, sys.Blue);
            return true;
        }

        try
        {
            var named = BColor.FromName(value);

            if (!named.IsEmpty)
            {
                color = named;
                return true;
            }
        }
        catch
        {
            // Not a known color
        }

        return false;
    }
}
