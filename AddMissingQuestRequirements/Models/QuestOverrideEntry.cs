using System.Text.Json.Serialization;

namespace AddMissingQuestRequirements.Models;

public sealed class QuestOverrideEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("behaviour")]
    public OverrideBehaviour? Behaviour { get; init; }

    /// <summary>
    /// Controls how the weapon array is expanded for this quest's conditions.
    /// Defaults to <see cref="ExpansionMode.Auto"/> (full pipeline).
    /// </summary>
    [JsonPropertyName("expansionMode")]
    public ExpansionMode ExpansionMode { get; init; } = ExpansionMode.Auto;

    /// <summary>Specific condition IDs this override applies to. Empty = applies to all conditions.</summary>
    [JsonPropertyName("conditions")]
    public List<string> Conditions { get; init; } = [];

    /// <summary>Weapon IDs (or type names) to unconditionally add to the condition.</summary>
    [JsonPropertyName("includedWeapons")]
    public List<string> IncludedWeapons { get; init; } = [];

    /// <summary>Weapon IDs (or type names) to remove after all additions. Runs last.</summary>
    [JsonPropertyName("excludedWeapons")]
    public List<string> ExcludedWeapons { get; init; } = [];

    /// <summary>
    /// Controls how attachment groups in the matched condition are rewritten.
    /// Groups use intra-group AND, cross-group OR. See <see cref="ExpansionMode"/>
    /// for mode-specific behaviour. Defaults to <see cref="ExpansionMode.Auto"/>.
    /// </summary>
    [JsonPropertyName("modsExpansionMode")]
    public ExpansionMode ModsExpansionMode { get; init; } = ExpansionMode.Auto;

    /// <summary>
    /// Attachment IDs or type names appended as new singleton groups to the matched
    /// condition's mod field (inclusive or exclusive, whichever is being processed).
    /// Each type-name entry expands to one singleton group per member. Applied in
    /// <see cref="ExpansionMode.Auto"/> and <see cref="ExpansionMode.NoExpansion"/>;
    /// under <see cref="ExpansionMode.WhitelistOnly"/> the field is rebuilt from
    /// these alone (original groups discarded).
    /// </summary>
    [JsonPropertyName("includedMods")]
    public List<string> IncludedMods { get; init; } = [];

    /// <summary>
    /// Attachment IDs or type names that cause output groups to be dropped. Applied
    /// last, after all expansion and inclusion steps.
    /// <list type="bullet">
    /// <item><description>A bare attachment ID drops any output group that contains
    /// that ID (aggressive — removes mixed AND-bundles that reference it).</description></item>
    /// <item><description>A type name drops only output groups whose members are
    /// <b>entirely</b> within that type. Mixed bundles that span multiple types
    /// are preserved.</description></item>
    /// </list>
    /// </summary>
    [JsonPropertyName("excludedMods")]
    public List<string> ExcludedMods { get; init; } = [];
}
