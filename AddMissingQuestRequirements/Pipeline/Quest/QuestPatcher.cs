using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>
/// Iterates all quests and their CounterCreator conditions, dispatching each condition to every
/// registered <see cref="IConditionExpander"/>. Expanders modify conditions in-place.
/// Adding support for a new condition field requires only a new <see cref="IConditionExpander"/>
/// implementation — this class's core loop does not change.
/// </summary>
public sealed class QuestPatcher
{
    private static readonly HashSet<string> _supportedConditionTypes = ["CounterCreator"];

    private readonly IReadOnlyList<IConditionExpander> _expanders;
    private readonly INameResolver _nameResolver;

    public QuestPatcher(IEnumerable<IConditionExpander> expanders, INameResolver nameResolver)
    {
        _expanders = [..expanders];
        _nameResolver = nameResolver;
    }

    public void Patch(
        IQuestDatabase db,
        OverriddenSettings settings,
        CategorizationResult categorization,
        IModLogger logger)
    {
        foreach (var (questId, quest) in db.Quests)
        {
            if (settings.ExcludedQuests.Contains(questId))
            {
                continue;
            }

            var questLogger = new PrefixingModLogger(
                logger,
                $"[{_nameResolver.GetQuestName(questId)} ({questId})] ");

            foreach (var condition in quest.Conditions)
            {
                if (!_supportedConditionTypes.Contains(condition.ConditionType))
                {
                    continue;
                }

                var overrideEntry = QuestOverrideResolver.Resolve(settings, questId, condition.Id);

                foreach (var expander in _expanders)
                {
                    expander.Expand(condition, overrideEntry, categorization, settings.Config, questLogger);
                }
            }
        }
    }

}
