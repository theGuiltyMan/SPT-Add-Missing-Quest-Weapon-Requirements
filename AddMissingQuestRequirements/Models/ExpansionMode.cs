namespace AddMissingQuestRequirements.Models;

/// <summary>
/// Controls how a condition's weapon array (<c>WeaponArrayExpander</c>) or mod
/// groups (<c>WeaponModsExpander</c>) are rewritten.
/// </summary>
public enum ExpansionMode
{
    /// <summary>
    /// Default.
    /// Weapons: full pipeline (type expansion → whitelist additions → canBeUsedAs → blacklist).
    /// Mods: singleton groups expand via type + aliases into new singleton groups;
    /// multi-item groups are kept verbatim (AND-bundles are not broadened).
    /// </summary>
    Auto,


    /// <summary>
    /// Weapons: type expansion is suppressed. The weapon list is cleared and replaced
    /// exclusively with weapons from <c>includedWeapons</c>. canBeUsedAs still applies
    /// to those entries.
    /// Mods: the entire mod field is discarded and rebuilt as one singleton group
    /// per entry in <c>includedMods</c> (type names expand to their members).
    /// </summary>
    WhitelistOnly,

    /// <summary>
    /// Weapons: type expansion and whitelist additions are suppressed. Only
    /// canBeUsedAs aliases are applied to the original weapon list.
    /// Mods: every original group is kept verbatim (no type or alias expansion);
    /// <c>includedMods</c> is still appended as new singleton groups.
    /// </summary>
    NoExpansion,
}