namespace AddMissingQuestRequirements.Models;

/// <summary>
/// Runtime container produced by OverrideReader and consumed by WeaponCategorizer and QuestPatcher.
/// Holds the fully-merged configuration from all installed mods.
/// </summary>
public sealed class OverriddenSettings
{
    /// <summary>Global mod settings.</summary>
    public ModConfig Config { get; init; } = new();

    /// <summary>Quest IDs that should be skipped entirely.</summary>
    public HashSet<string> ExcludedQuests { get; init; } = [];

    /// <summary>Per-quest (and per-condition) override entries, keyed by quest ID.</summary>
    public Dictionary<string, List<QuestOverrideEntry>> QuestOverrides { get; init; } = [];

    /// <summary>Manual weapon type overrides: weapon ID → comma-separated type names.</summary>
    public Dictionary<string, string> ManualTypeOverrides { get; init; } = [];

    /// <summary>
    /// Alias map: weapon ID → set of weapon IDs it can be used as.
    /// Already resolved — no Overridable wrappers; DELETE entries have been applied.
    /// </summary>
    public Dictionary<string, HashSet<string>> CanBeUsedAs { get; init; } = [];

    /// <summary>Words stripped from weapon locale names before short-name alias matching.</summary>
    public List<string> AliasNameStripWords { get; init; } = [];

    /// <summary>
    /// Weapon short names or IDs excluded from automatic short-name alias matching.
    /// An excluded weapon is skipped in both source and target positions of the
    /// name-based cross-link loop, so it is never auto-linked to other weapons
    /// sharing its normalized short name. Explicit <see cref="CanBeUsedAs"/>
    /// entries are NOT filtered — manual aliases always apply.
    /// Entries are compared case-insensitively and short-name entries are passed
    /// through the same <see cref="AliasNameStripWords"/> normalization used for
    /// grouping.
    /// </summary>
    public List<string> AliasNameExcludeWeapons { get; init; } = [];

    /// <summary>Manual attachment type overrides: attachment ID → comma-separated type names.</summary>
    public Dictionary<string, string> ManualAttachmentTypeOverrides { get; init; } = [];

    /// <summary>
    /// Alias map for attachments: attachment ID → set of attachment IDs it can be used as.
    /// Already resolved — no Overridable wrappers; DELETE entries have been applied.
    /// </summary>
    public Dictionary<string, HashSet<string>> AttachmentCanBeUsedAs { get; init; } = [];

    /// <summary>Words stripped from attachment locale names before short-name alias matching.</summary>
    public List<string> AttachmentAliasNameStripWords { get; init; } = [];

    /// <summary>Merged ordered rule list for type detection.</summary>
    public List<TypeRule> TypeRules { get; init; } = [];

    /// <summary>
    /// Merged ordered rule list for attachment type detection. Populated from
    /// every mod's <c>AttachmentOverrides.jsonc</c> <c>customTypeRules</c> field.
    /// Fully isolated from <see cref="TypeRules"/> — the two lists never intermix.
    /// </summary>
    public List<TypeRule> AttachmentTypeRules { get; init; } = [];
}
