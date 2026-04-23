using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Config;

namespace AddMissingQuestRequirements.Models;

/// <summary>
/// Config model for <c>MissingQuestWeapons/AttachmentOverrides.jsonc</c>.
/// Parallel to <see cref="WeaponOverridesFile"/> but for attachment items.
/// </summary>
public sealed class AttachmentOverridesFile : IVersionedConfig
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("overrideBehaviour")]
    public OverrideBehaviour OverrideBehaviour { get; init; } = OverrideBehaviour.IGNORE;

    /// <summary>
    /// Manual attachment type overrides: attachment ID → comma-separated type names.
    /// When set, auto-detection is skipped for that item.
    /// </summary>
    [JsonPropertyName("manualAttachmentTypeOverrides")]
    public Dictionary<string, string> ManualAttachmentTypeOverrides { get; init; } = [];

    /// <summary>
    /// Alias map: attachment ID → list of attachment IDs it can be used as.
    /// Each entry supports the bare-string or { value, behaviour } Overridable form.
    /// </summary>
    [JsonPropertyName("canBeUsedAs")]
    public Dictionary<string, List<Overridable<string>>> CanBeUsedAs { get; init; } = [];

    /// <summary>Words stripped from attachment locale names before short-name alias matching.</summary>
    [JsonPropertyName("aliasNameStripWords")]
    public List<string> AliasNameStripWords { get; init; } = [];

    /// <summary>
    /// User-authored type rules for attachment categorization. Merged into
    /// <see cref="OverriddenSettings.AttachmentTypeRules"/> by
    /// <see cref="Pipeline.Override.OverrideReader"/> in load order. Applied to
    /// attachment items by <see cref="Pipeline.Attachment.AttachmentCategorizer"/>
    /// alongside the built-in rule chain.
    /// </summary>
    [JsonPropertyName("customTypeRules")]
    public List<TypeRule> CustomTypeRules { get; init; } = [];
}
