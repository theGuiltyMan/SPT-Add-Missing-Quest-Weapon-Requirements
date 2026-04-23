using AddMissingQuestRequirements.Pipeline.Shared;

namespace AddMissingQuestRequirements.Pipeline.Attachment;

/// <summary>
/// Output of <see cref="AttachmentCategorizer.Categorize"/>.
/// All three maps are immutable snapshots keyed by attachment item ID.
/// </summary>
public sealed class AttachmentCategorizationResult : IItemCategorization
{
    /// <summary>Attachment type name → set of attachment IDs belonging to that type.</summary>
    public required IReadOnlyDictionary<string, IReadOnlySet<string>> AttachmentTypes { get; init; }

    /// <summary>Attachment ID → set of types that attachment belongs to.</summary>
    public required IReadOnlyDictionary<string, IReadOnlySet<string>> AttachmentToType { get; init; }

    /// <summary>Attachment ID → set of attachment IDs that can substitute for it.</summary>
    public required IReadOnlyDictionary<string, IReadOnlySet<string>> CanBeUsedAs { get; init; }

    /// <summary>
    /// Every item ID present in the source database. Used by <see cref="WeaponModsExpander"/>
    /// to distinguish uncategorized-but-in-DB attachments from wholly unknown IDs.
    /// </summary>
    public required IReadOnlySet<string> KnownItemIds { get; init; }

    // Explicit interface implementation for IItemCategorization
    IReadOnlyDictionary<string, IReadOnlySet<string>> IItemCategorization.ItemToType => AttachmentToType;
    IReadOnlyDictionary<string, IReadOnlySet<string>> IItemCategorization.TypeToItems => AttachmentTypes;
    IReadOnlyDictionary<string, IReadOnlySet<string>> IItemCategorization.CanBeUsedAs => CanBeUsedAs;
    IReadOnlySet<string> IItemCategorization.KnownItemIds => KnownItemIds;
}
