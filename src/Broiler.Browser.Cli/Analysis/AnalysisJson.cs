using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broiler.Cli.Analysis;

/// <summary>How every JSON file of an analysis is written.</summary>
internal static class AnalysisJson
{
    /// <summary>
    /// Indented, camel-cased, nulls kept, and without the HTML-safe escaping the default encoder
    /// applies. The files are read by people and by tools, never embedded in a page, so a URL or a
    /// selector reads as <c>a &gt; b</c> and <c>?a=1&amp;b=2</c> rather than as <c>></c> and
    /// <c>&</c>.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>Writes <paramref name="value"/> to <paramref name="path"/>, creating its directory.</summary>
    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }
}
