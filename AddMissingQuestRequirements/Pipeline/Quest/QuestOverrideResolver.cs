using AddMissingQuestRequirements.Models;

namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>
/// Resolves the <see cref="QuestOverrideEntry"/> applicable to a given quest condition.
/// Condition-specific matches win over generic (quest-wide) entries.
/// </summary>
public static class QuestOverrideResolver
{
    public static QuestOverrideEntry? Resolve(OverriddenSettings settings, string questId, string conditionId)
    {
        if (!settings.QuestOverrides.TryGetValue(questId, out var entries))
        {
            return null;
        }

        foreach (var entry in entries)
        {
            if (entry.Conditions.Contains(conditionId))
            {
                return entry;
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Conditions.Count == 0)
            {
                return entry;
            }
        }

        return null;
    }
}
