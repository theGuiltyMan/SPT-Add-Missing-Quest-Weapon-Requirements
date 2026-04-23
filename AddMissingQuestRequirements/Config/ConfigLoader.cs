using System.Text.Json;
using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Config;

public sealed record LoadResult<T>(T Config, IReadOnlyList<string> Warnings);

/// <summary>
/// Chains JsoncReader → ConfigMigrator → JsonSerializer to load a typed config file.
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Loads config from a JSONC string, migrates it to <paramref name="currentVersion"/>,
    /// and deserializes to <typeparamref name="T"/>.
    /// </summary>
    public static LoadResult<T> LoadFromString<T>(
        string jsonc,
        int currentVersion,
        Func<JsonObject, JsonObject>[] migrations)
        where T : IVersionedConfig, new()
    {
        var node = JsonNode.Parse(jsonc, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var obj = node?.AsObject() ?? new JsonObject();
        var migrated = ConfigMigrator.Migrate(obj, currentVersion, migrations);
        var config = migrated.Json.Deserialize<T>(JsoncReader.DefaultOptions) ?? new T();
        return new LoadResult<T>(config, migrated.Warnings);
    }

    /// <summary>
    /// Loads config from a JSONC file. Returns a default instance if the file does not exist.
    /// </summary>
    public static LoadResult<T> LoadFromFile<T>(
        string path,
        int currentVersion,
        Func<JsonObject, JsonObject>[] migrations)
        where T : IVersionedConfig, new()
    {
        if (!File.Exists(path))
            return new LoadResult<T>(new T(), []);

        var content = File.ReadAllText(path);
        return LoadFromString<T>(content, currentVersion, migrations);
    }
}
