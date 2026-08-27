using System.Reflection;
using System.Xml.Linq;

namespace Broiler.Layout.Tests;

/// <summary>
/// Freezes the <c>Broiler.Layout</c> dependency boundary for the layout extraction
/// (see <c>Broiler.Layout/docs/roadmap.md</c>). The component may
/// reference only <c>Broiler.CSS</c>, <c>Broiler.CSS.Dom</c>, <c>Broiler.Dom</c>,
/// the backend-agnostic <c>Broiler.Graphics</c> primitive layer (color/font
/// metrics — <c>BColor</c>, <c>ILayoutFont</c>; no concrete rasterizer), and the
/// BCL. It must not leak the renderer, bridge, JavaScript, or any concrete
/// graphics backend through its public surface.
/// </summary>
public sealed class LayoutArchitectureTests
{
    [Fact(Timeout = 600000)]
    public void Production_Project_References_Only_Css_Dom_And_Graphics_Primitives()
    {
        var project = XDocument.Load(FindProjectPath());
        var references = project
            .Descendants("ProjectReference")
            // Normalise Windows-style backslash separators so the file-name extraction works on
            // any host — Path.GetFileNameWithoutExtension does not treat '\' as a separator on Linux.
            .Select(static element => Path.GetFileNameWithoutExtension(
                ((string?)element.Attribute("Include"))?.Replace('\\', '/')))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        // Broiler.Graphics is the backend-agnostic primitive layer (BColor,
        // ILayoutFont). A concrete backend (e.g. Broiler.Graphics.Windows) must
        // NOT appear here — this allowlist is the structural gate that keeps them
        // out of the layout engine.
        Assert.Equal(["Broiler.CSS", "Broiler.CSS.Dom", "Broiler.Dom", "Broiler.Graphics"], references);
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact(Timeout = 600000)]
    public void Internal_Consumers_Are_Explicit_And_Minimal()
    {
        var project = XDocument.Load(FindProjectPath());
        var friends = project
            .Descendants("InternalsVisibleTo")
            .Select(static element => (string?)element.Attribute("Include"))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Broiler.Cli.Tests",
                "Broiler.DevConsole",
                "Broiler.DevConsole.Tests",
                "Broiler.HTML",
                "Broiler.HTML.Dom",
                // Headless live-geometry snapshot (Phase 5 engine-native live geometry) reads the
                // internal box tree via HeadlessLayoutView.
                "Broiler.HTML.Headless",
                // Rasterises a nested browsing context, so it reads EmbeddedCanvas to decide
                // whether that frame's canvas is opaque or transparent (CSS Color Adjust §2.4).
                "Broiler.HTML.Image",
                "Broiler.HTML.Orchestration",
                // The bridge writes the Phase 5 visual-viewport channel
                // (NativeAnchorPlacement.VisualViewportScale) around the shared geometry snapshot.
                "Broiler.HtmlBridge.Dom",
                "Broiler.Layout.Tests",
                // The stage profiler drives internal knobs the render path exposes to no one else
                // — CssStyleRecalc's thread budget for --style-scaling, and the same for the
                // raster and tile budgets. Added with item #12; this list was not updated with it,
                // which is why this gate was red before item #14 touched anything.
                "Broiler.Render.Stage.Benchmarks",
                // The WPT runner toggles NativeAnchorPlacement.Enabled around the final
                // render for the Phase 5 native anchor-placement cutover (P5.8d.2b).
                "Broiler.Wpt",
            ],
            friends);
    }

    [Fact(Timeout = 600000)]
    public void Public_Surface_Does_Not_Leak_Consumer_Types()
    {
        var assembly = typeof(ILayoutEnvironment).Assembly;
        var forbidden = assembly.GetExportedTypes()
            .SelectMany(GetMemberTypes)
            .Where(static type => type.Namespace is not null)
            .Where(static type =>
                type.Namespace!.StartsWith("Broiler.HtmlBridge", StringComparison.Ordinal) ||
                type.Namespace.StartsWith("Broiler.HTML", StringComparison.Ordinal) ||
                type.Namespace.StartsWith("Broiler.JavaScript", StringComparison.Ordinal) ||
                // Concrete graphics backends (e.g. Broiler.Graphics.Windows) must not
                // leak; the backend-agnostic core (namespace "Broiler.Graphics" exactly:
                // BColor, ILayoutFont) is an allowed primitive dependency.
                type.Namespace.StartsWith("Broiler.Graphics.", StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        Assert.Empty(forbidden);
    }

    [Fact(Timeout = 600000)]
    public void Public_Surface_Does_Not_Expose_Mutable_Collections()
    {
        var assembly = typeof(ILayoutEnvironment).Assembly;
        var mutable = assembly.GetExportedTypes()
            .SelectMany(GetMemberTypes)
            .Where(static type => type.IsGenericType)
            .Select(static type => type.GetGenericTypeDefinition())
            .Where(static definition =>
                definition == typeof(List<>) ||
                definition == typeof(Dictionary<,>) ||
                definition == typeof(HashSet<>))
            .Distinct()
            .ToArray();

        Assert.Empty(mutable);
    }

    private static IEnumerable<Type> GetMemberTypes(Type type)
    {
        yield return type;
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }
        foreach (var property in type.GetProperties())
            yield return property.PropertyType;
    }

    private static string FindProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "Broiler.Layout", "Broiler.Layout", "Broiler.Layout.csproj");
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"Broiler.Layout.csproj not found walking up from {AppContext.BaseDirectory}");
    }
}
