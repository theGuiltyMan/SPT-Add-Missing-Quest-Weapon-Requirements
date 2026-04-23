using AddMissingQuestRequirements.Pipeline.Shared;

namespace AddMissingQuestRequirements.Pipeline.Weapon;

/// <summary>
/// Output of <see cref="WeaponCategorizer.Categorize"/>.
/// All three maps are immutable snapshots.
/// </summary>
public sealed class CategorizationResult : IItemCategorization
{
    /// <summary>Weapon type name → set of weapon IDs belonging to that type.</summary>
    public required IReadOnlyDictionary<string, IReadOnlySet<string>> WeaponTypes { get; init; }

    /// <summary>Weapon ID → set of types that weapon belongs to.</summary>
    public required IReadOnlyDictionary<string, IReadOnlySet<string>> WeaponToType { get; init; }

    /// <summary>Weapon ID → set of weapon IDs that can substitute for it.</summary>
    public required IReadOnlyDictionary<string, IReadOnlySet<string>> CanBeUsedAs { get; init; }

    /// <summary>Weapon ID → caliber string (from _props.ammoCaliber). Only weapons that have
    /// a caliber defined are present; missing key means caliber is unknown.</summary>
    public required IReadOnlyDictionary<string, string> WeaponToCaliber { get; init; }

    /// <summary>All item IDs present in the database, regardless of whether they matched any
    /// type rule. Used by <see cref="WeaponArrayExpander"/> to distinguish garbage IDs (not
    /// in DB at all) from merely-uncategorized weapons (in DB, but no rule matched).</summary>
    public required IReadOnlySet<string> KnownItemIds { get; init; }

    // Explicit interface implementation for IItemCategorization
    IReadOnlyDictionary<string, IReadOnlySet<string>> IItemCategorization.ItemToType => WeaponToType;
    IReadOnlyDictionary<string, IReadOnlySet<string>> IItemCategorization.TypeToItems => WeaponTypes;
    IReadOnlyDictionary<string, IReadOnlySet<string>> IItemCategorization.CanBeUsedAs => CanBeUsedAs;
    IReadOnlySet<string> IItemCategorization.KnownItemIds => KnownItemIds;
}
