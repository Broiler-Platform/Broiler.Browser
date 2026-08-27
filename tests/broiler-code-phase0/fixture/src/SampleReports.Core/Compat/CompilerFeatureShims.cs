#if LEGACY_TARGET
using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// netstandard2.0 does not ship <c>IsExternalInit</c>, so <c>init</c> accessors
/// need this shim. Compiled only under the netstandard2.0 condition in
/// <c>SampleReports.Core.csproj</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
#endif
