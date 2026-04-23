namespace AddMissingQuestRequirements.Pipeline.Shared;

/// <summary>
/// Shared shape of weapon and attachment categorization results.
/// Implemented by <see cref="Weapon.CategorizationResult"/> and
/// <see cref="Attachment.AttachmentCategorizationResult"/> via explicit interface
/// implementation — the public property names on each concrete type are
/// preserved (<c>WeaponToType</c>, <c>AttachmentToType</c>, etc.).
/// Used by the shared group expander to read the four maps without knowing
/// which domain it is operating on.
/// Note: the interface uses neutral names — <c>TypeToItems</c> on the
/// interface is the same map as <c>WeaponTypes</c>/<c>AttachmentTypes</c> on
/// the concrete type.
/// </summary>
public interface IItemCategorization
{
    /// <summary>Item ID → set of types the item belongs to.</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemToType { get; }

    /// <summary>Type name → set of item IDs in that type.</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> TypeToItems { get; }

    /// <summary>Item ID → set of item IDs that can substitute for it.</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> CanBeUsedAs { get; }

    /// <summary>Every item ID present in the source database.</summary>
    IReadOnlySet<string> KnownItemIds { get; }
}
