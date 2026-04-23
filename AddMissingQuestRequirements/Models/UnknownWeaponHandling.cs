namespace AddMissingQuestRequirements.Models;

/// <summary>
/// Controls how weapon and attachment IDs that cannot be categorized are treated.
/// Applied by <see cref="Pipeline.Weapon.WeaponArrayExpander"/> and
/// <see cref="Pipeline.Attachment.WeaponModsExpander"/>.
/// </summary>
public enum UnknownWeaponHandling
{
    /// <summary>Remove any ID not produced by the rule chain, including IDs absent from the item database.</summary>
    Strip    = 0,

    /// <summary>Keep IDs that exist in the item database but lack a rule match; remove IDs absent from the database.</summary>
    KeepInDb = 1,

    /// <summary>Keep every ID the user wrote, regardless of categorization or database membership. Conservative default.</summary>
    KeepAll  = 2,
}
