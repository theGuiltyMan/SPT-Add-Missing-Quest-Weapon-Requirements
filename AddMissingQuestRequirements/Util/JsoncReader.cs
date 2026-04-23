using System.Text.Json;
using System.Text.Json.Serialization;

namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Deserializes JSONC (JSON with // and /* */ comments and trailing commas)
/// using System.Text.Json with appropriate options.
/// </summary>
public static class JsoncReader
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Deserializes a JSONC string to <typeparamref name="T"/>.</summary>
    public static T? Deserialize<T>(string jsonc, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(jsonc, options ?? DefaultOptions);

    /// <summary>Deserializes a JSONC file to <typeparamref name="T"/>. Returns null if the file does not exist.</summary>
    public static T? DeserializeFile<T>(string path, JsonSerializerOptions? options = null)
    {
        if (!File.Exists(path)) return default;
        var content = File.ReadAllText(path);
        return Deserialize<T>(content, options);
    }
}
