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
    /// Attachment IDs or type names appended as new <b>singleton</b> groups to
    /// <c>weaponModsInclusive</c> only. Each type-name entry expands to one singleton
    /// per type member. Applied in <see cref="ExpansionMode.Auto"/> and
    /// <see cref="ExpansionMode.NoExpansion"/>; under <see cref="ExpansionMode.WhitelistOnly"/>
    /// the inclusive field is rebuilt from these alone.
    ///
    /// <para>For multi-attachment AND bundles (e.g. <c>barrel + scope</c>) use
    /// <see cref="IncludedModBundles"/> instead.</para>
    /// </summary>
    [JsonPropertyName("includedMods")]
    public List<string> IncludedMods { get; init; } = [];

    /// <summary>
    /// Attachment IDs or type names appended as new <b>singleton</b> groups to
    /// <c>weaponModsExclusive</c> only. Each type-name entry expands to one singleton
    /// per type member. Forbids weapons carrying any listed attachment.
    ///
    /// <para><b>Behavior changed in config v3 (mod 2.1.0):</b> previously this list
    /// dropped groups from both fields. Now it only appends to the exclusive field.</para>
    /// </summary>
    [JsonPropertyName("excludedMods")]
    public List<string> ExcludedMods { get; init; } = [];

    /// <summary>
    /// Cartesian AND-bundles appended to <c>weaponModsInclusive</c>. Each outer entry
    /// is a list of <i>sets</i>: a type-name expands to its member set, a bare id is
    /// a singleton-set. The output is the cartesian product of the sets, emitted as
    /// one group per combination (each group an AND-bundle of one id per set).
    ///
    /// <para>Example: <c>[["m60_barrels", "aimpoint_scopes"]]</c> with 2 barrels and
    /// 5 scopes emits 10 bundles of shape <c>[barrel, scope]</c>.</para>
    ///
    /// <para>Per-entry product is capped by <see cref="ModConfig.ModBundleCartesianCap"/>
    /// (default 500). Exceeding entries are truncated and logged.</para>
    /// </summary>
    [JsonPropertyName("includedModBundles")]
    public List<List<string>> IncludedModBundles { get; init; } = [];

    /// <summary>
    /// Cartesian AND-bundles appended to <c>weaponModsExclusive</c>. Same shape and
    /// expansion rules as <see cref="IncludedModBundles"/>.
    /// </summary>
    [JsonPropertyName("excludedModBundles")]
    public List<List<string>> ExcludedModBundles { get; init; } = [];
}
