using System.Runtime.CompilerServices;
using Broiler.Graphics;

namespace Broiler.Layout.Text;

/// <summary>
/// Publishes <see cref="SystemFontIndex"/> to <c>Broiler.Graphics</c>, so the software rasterizer
/// and its text measurement resolve a family to the same file the layout engine does.
/// </summary>
/// <remarks>
/// <para>
/// Graphics cannot reference Layout — the dependency runs the other way — so the index has to be
/// handed over rather than reached for. It happens here, in a module initializer, rather than in
/// each composition root, because every host that can produce a render list carrying text has
/// already loaded this assembly to build the display list in the first place: <c>DisplayList</c>
/// and <c>DrawTextItem</c> live in <c>Broiler.Layout.IR</c>. Registering per-head would instead
/// mean every new head silently starting out with the single-face behaviour until someone noticed.
/// </para>
/// <para>
/// Registration is not the same as scanning: <see cref="SystemFontIndex"/> indexes lazily on its
/// first lookup, so this costs a delegate assignment at load and nothing else. A host that wants a
/// different font source can still call <see cref="BSystemFontFiles.Use"/> afterwards and win.
/// </para>
/// </remarks>
internal static class SystemFontIndexRegistration
{
    [ModuleInitializer]
    internal static void Register() => BSystemFontFiles.Use(SystemFontIndex.TryResolve);
}
