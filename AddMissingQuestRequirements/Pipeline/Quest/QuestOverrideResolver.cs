using AddMissingQuestRequirements.Models;

namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>
/// Resolves the <see cref="QuestOverrideEntry"/> applicable to a given quest condition.
/// Condition-specific matches win over generic (quest-wide) entries. A specific match
/// accepts either the sub-condition id (<see cref="ConditionNode.Id"/>) or the outer
/// CounterCreator wrapper id (<see cref="ConditionNode.ParentConditionId"/>); users
/// commonly author overrides against the outer id because that is what quest editors
/// and the locale keys surface.
/// </summary>
public static class QuestOverrideResolver
{
    public static QuestOverrideEntry? Resolve(
        OverriddenSettings settings,
        string questId,
        ConditionNode condition)
    {
        if (!settings.QuestOverrides.TryGetValue(questId, out var entries))
        {
            return null;
        }

        foreach (var entry in entries)
        {
            if (entry.Conditions.Count == 0)
            {
                continue;
            }
            if (entry.Conditions.Contains(condition.Id)
                || (!string.IsNullOrEmpty(condition.ParentConditionId)
                    && entry.Conditions.Contains(condition.ParentConditionId)))
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
