using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>
/// Resolves the <see cref="QuestOverrideEntry"/> applicable to one quest condition.
/// <para>
/// An entry targets the condition when its <c>Conditions</c> list names the
/// sub-condition id (<see cref="ConditionNode.Id"/>) or the outer CounterCreator id
/// (<see cref="ConditionNode.ParentConditionId"/>); users commonly author against the
/// outer id because quest editors and locale keys surface it. An entry with an empty
/// <c>Conditions</c> list is quest-wide and targets every condition.
/// </para>
/// <para>
/// Files are folded in load order (<see cref="QuestOverrideEntry.Layer"/>). Each file
/// contributes its entries that target the condition specifically, or, if it has none,
/// its quest-wide entries. Each contributed entry is applied by its behaviour:
/// IGNORE keeps an earlier result, REPLACE takes the entry, MERGE field-merges it
/// into the earlier result, DELETE clears the result.
/// </para>
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

        QuestOverrideEntry? resolved = null;

        foreach (var file in entries.GroupBy(e => e.Layer).OrderBy(g => g.Key))
        {
            var specific = file.Where(e => TargetsSpecifically(e, condition)).ToList();
            var contributed = specific.Count > 0
                ? specific
                : file.Where(e => e.Conditions.Count == 0);

            foreach (var entry in contributed)
            {
                resolved = Apply(resolved, entry);
            }
        }

        return resolved;
    }

    private static bool TargetsSpecifically(QuestOverrideEntry entry, ConditionNode condition)
    {
        if (entry.Conditions.Contains(condition.Id))
        {
            return true;
        }
        return !string.IsNullOrEmpty(condition.ParentConditionId)
            && entry.Conditions.Contains(condition.ParentConditionId);
    }

    private static QuestOverrideEntry? Apply(QuestOverrideEntry? current, QuestOverrideEntry incoming)
    {
        switch (incoming.Behaviour ?? OverrideBehaviour.IGNORE)
        {
            case OverrideBehaviour.REPLACE:
                return incoming;
            case OverrideBehaviour.MERGE:
                return current is null ? incoming : MergeHelper.MergeEntries(current, incoming);
            case OverrideBehaviour.DELETE:
                return null;
            default:
                return current ?? incoming;
        }
    }
}
