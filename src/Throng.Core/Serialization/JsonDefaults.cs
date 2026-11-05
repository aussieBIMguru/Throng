using System.Text.Json;
using System.Text.Json.Serialization;

namespace Throng.Core.Serialization;

/// <summary>
/// The single set of JSON options used for every Throng file and template copy, so all JSON looks the same:
/// camelCase, indented, enums as strings, nulls omitted.
/// </summary>
public static class JsonDefaults
{
    /// <summary>Shared serializer options. Treat as read-only.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
