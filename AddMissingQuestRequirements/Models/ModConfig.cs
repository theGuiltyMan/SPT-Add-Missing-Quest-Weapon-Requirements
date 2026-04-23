using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Config;

namespace AddMissingQuestRequirements.Models;

public sealed class ModConfig : IVersionedConfig
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    /// <summary>
    /// Master switch. When false the loader skips the entire pipeline and logs a
    /// single "disabled" line to the SPT console. Useful for troubleshooting
    /// quest issues without uninstalling the mod.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>Maps a child weapon type to its parent type (e.g. "BoltActionSniperRifle" → "SniperRifle").</summary>
    [JsonPropertyName("parentTypes")]
    public Dictionary<string, string> ParentTypes { get; init; } = [];

    [JsonPropertyName("excludedItems")]
    public List<string> ExcludedItems { get; init; } = [];

    [JsonPropertyName("excludedWeaponTypes")]
    public List<string> ExcludedWeaponTypes { get; init; } = [];

    /// <summary>
    /// When true, a weapon appears in both its direct type and all ancestor types via <see cref="ParentTypes"/>.
    /// </summary>
    [JsonPropertyName("includeParentCategories")]
    public bool IncludeParentCategories { get; init; } = true;

    [JsonPropertyName("debug")]
    public bool Debug { get; init; }

    [JsonPropertyName("bestCandidateExpansion")]
    public bool BestCandidateExpansion { get; init; }

    /// <summary>
    /// How to treat weapon and attachment IDs that are not categorized by the rule chain.
    /// <see cref="UnknownWeaponHandling.KeepAll"/> (default) preserves every ID the user
    /// wrote, so runtime-loaded mod items and forward-authored configs survive the pass.
    /// See <see cref="UnknownWeaponHandling"/> for the full table.
    /// </summary>
    [JsonPropertyName("unknownWeaponHandling")]
    public UnknownWeaponHandling UnknownWeaponHandling { get; init; } = UnknownWeaponHandling.KeepAll;

    [JsonPropertyName("validateOverrideIds")]
    public bool ValidateOverrideIds { get; init; }

    /// <summary>
    /// Ancestor `_name` values treated as "weapon-like" by the categorizer pre-filter.
    /// Any leaf item whose parent chain passes through one of these nodes is a candidate
    /// for type detection. Defaults cover firearms, melee, throwables, and underbarrel
    /// grenade launchers (which live under the Mod → GearMod → Launcher subtree rather
    /// than Weapon).
    /// </summary>
    [JsonPropertyName("weaponLikeAncestors")]
    public List<string> WeaponLikeAncestors { get; init; } = ["Weapon", "Knife", "ThrowWeap", "Launcher"];
}
