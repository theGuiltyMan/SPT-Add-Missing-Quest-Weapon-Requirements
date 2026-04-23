using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>
/// Processes one expandable field of a <see cref="ConditionNode"/> in-place.
/// One implementation per field (weapon array, weaponMods*, etc.).
/// Register implementations with <see cref="QuestPatcher"/> to add support for new fields
/// without changing the patcher's core loop.
/// </summary>
public interface IConditionExpander
{
    void Expand(
        ConditionNode condition,
        QuestOverrideEntry? overrideEntry,
        CategorizationResult categorization,
        ModConfig config,
        IModLogger logger);
}
