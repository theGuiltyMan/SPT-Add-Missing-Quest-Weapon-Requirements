using System.Text.Json.Nodes;

namespace AddMissingQuestRequirements.Config;

public sealed record MigrationResult(JsonObject Json, IReadOnlyList<string> Warnings);

/// <summary>
/// Applies an ordered chain of migration functions to a raw JSON object,
/// advancing it from its current version to <c>currentVersion</c>.
/// </summary>
public static class ConfigMigrator
{
    /// <param name="json">The raw deserialized JSON object (will be mutated in place).</param>
    /// <param name="currentVersion">The version this mod expects (i.e. the highest known version).</param>
    /// <param name="migrations">
    /// Ordered migration functions where <c>migrations[i]</c> upgrades from version i to version i+1.
    /// </param>
    public static MigrationResult Migrate(
        JsonObject json,
        int currentVersion,
        Func<JsonObject, JsonObject>[] migrations)
    {
        var warnings = new List<string>();

        var fileVersion = json["version"]?.GetValue<int>() ?? 0;

        if (fileVersion > currentVersion)
        {
            warnings.Add(
                $"Config version {fileVersion} is newer than the mod's expected version {currentVersion}. " +
                "Some settings may be ignored.");
            return new MigrationResult(json, warnings);
        }

        // Apply migrations from fileVersion to currentVersion.
        // If no function is defined for a step the file is already compatible — skip it.
        for (var from = fileVersion; from < currentVersion; from++)
        {
            if (from < migrations.Length)
                json = migrations[from](json);
        }

        // Stamp the final version
        json["version"] = currentVersion;

        return new MigrationResult(json, warnings);
    }
}
