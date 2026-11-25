using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Models;

namespace AddMissingQuestRequirements.Config;

/// <summary>
/// Version and migration chain for <c>config.jsonc</c>. The SPT loader and the
/// Inspector both load through <see cref="Load"/>, so they can't disagree on either.
/// </summary>
public static class ModConfigFile
{
    public const string FileName = "config.jsonc";

    public const int CurrentVersion = 4;

    private static readonly Func<JsonObject, JsonObject>[] Chain =
    [
        Migrations.v0_to_v1,
        Migrations.v1_to_v2_Config,
        Migrations.v2_to_v3_Config,
        Migrations.v3_to_v4_Config,
    ];

    /// <summary>Parses and migrates <paramref name="path"/>. Throws on malformed JSONC.</summary>
    public static ModConfig Load(string path)
    {
        return ConfigLoader.LoadFromFile<ModConfig>(path, CurrentVersion, Chain).Config;
    }
}
