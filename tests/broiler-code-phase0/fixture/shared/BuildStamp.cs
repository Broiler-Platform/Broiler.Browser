namespace SampleReports.Core.Reporting;

/// <summary>
/// Lives outside every project cone and is pulled in with a linked
/// <c>&lt;Compile Include=".." Link=".." /&gt;</c> item. A workspace model that
/// assumes "source files are the files under the project directory" loses this
/// file, and a diagnostic reported against it has a path the editor cannot map
/// back to a document unless linked items are modelled explicitly.
/// </summary>
internal static class BuildStamp
{
    public static string Describe()
    {
#if LEGACY_TARGET
        return "sample-reports (netstandard2.0)";
#else
        return "sample-reports (net10.0)";
#endif
    }
}
