using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Config;

namespace AddMissingQuestRequirements.Models;

public sealed class WeaponOverridesFile : IVersionedConfig
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("overrideBehaviour")]
    public OverrideBehaviour OverrideBehaviour { get; init; } = OverrideBehaviour.IGNORE;

    /// <summary>
    /// Manual type overrides: weapon ID → comma-separated type names.
    /// When set, auto-detection is skipped for that item.
    /// </summary>
    [JsonPropertyName("manualTypeOverrides")]
    public Dictionary<string, string> ManualTypeOverrides { get; init; } = [];

    /// <summary>
    /// Alias map: weapon ID → list of weapon IDs it can be used as.
    /// Each entry supports the bare-string or { value, behaviour } Overridable form.
    /// </summary>
    [JsonPropertyName("canBeUsedAs")]
    public Dictionary<string, List<Overridable<string>>> CanBeUsedAs { get; init; } = [];

    /// <summary>Words stripped from weapon locale names before short-name alias matching.</summary>
    [JsonPropertyName("aliasNameStripWords")]
    public List<string> AliasNameStripWords { get; init; } = [];

    /// <summary>
    /// Weapon short names that are excluded from automatic short-name alias matching,
    /// even if they would otherwise match after stripping <see cref="AliasNameStripWords"/>.
    /// </summary>
    [JsonPropertyName("aliasNameExcludeWeapons")]
    public List<string> AliasNameExcludeWeapons { get; init; } = [];

    /// <summary>
    /// Type rules generated from migrated CustomCategories (TS v0 format)
    /// or written directly by the user in v2 format.
    /// </summary>
    [JsonPropertyName("customTypeRules")]
    public List<TypeRule> CustomTypeRules { get; init; } = [];
}
