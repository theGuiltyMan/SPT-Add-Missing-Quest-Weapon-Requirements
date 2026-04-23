namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>
/// A single sub-condition inside a quest's CounterCreator condition.
/// Fields are mutable so <see cref="QuestPatcher"/> can expand them in-place.
/// </summary>
public sealed class ConditionNode
{
    public string Id { get; init; } = string.Empty;
    public string ConditionType { get; init; } = string.Empty;

    /// <summary>
    /// Parent CounterCreator condition ID. Display-only metadata — not read by any pipeline
    /// stage. SPT stores the user-facing condition description under this bare-ID locale key,
    /// not under the sub-condition <see cref="Id"/>, so the inspector needs it to resolve
    /// descriptions for the HTML report. Empty when unknown or when populated by tests that
    /// do not care about descriptions.
    /// </summary>
    public string ParentConditionId { get; init; } = string.Empty;

    /// <summary>Weapon item IDs the kill must be made with. Expanded by WeaponArrayExpander.</summary>
    public List<string> Weapon { get; init; } = [];

    /// <summary>Caliber filter — constrains which weapons count; not an expand target.</summary>
    public List<string> WeaponCaliber { get; init; } = [];

    /// <summary>
    /// Attachment AND-bundles, OR'd across the list. A weapon satisfies the
    /// condition iff at least one group is fully matched (every item present).
    /// </summary>
    public List<List<string>> WeaponModsInclusive { get; init; } = [];

    /// <summary>
    /// Attachment rejection AND-bundles, OR'd across the list. A weapon is
    /// rejected iff at least one group is fully matched. Same shape as
    /// <see cref="WeaponModsInclusive"/>.
    /// </summary>
    public List<List<string>> WeaponModsExclusive { get; init; } = [];

    /// <summary>Number of kills required (from parent CounterCreator condition's value field).</summary>
    public int? KillCount { get; init; }

    /// <summary>Enemy type filter — savageRole values (e.g. "Savage", "pmcBot").</summary>
    public List<string> EnemyTypes { get; init; } = [];

    /// <summary>Location filter on the sub-condition (distinct from the quest's own location).</summary>
    public List<string> ConditionLocation { get; init; } = [];

    /// <summary>Distance constraint as a display string (e.g. ">= 100"), null if absent.</summary>
    public string? Distance { get; init; }
}
