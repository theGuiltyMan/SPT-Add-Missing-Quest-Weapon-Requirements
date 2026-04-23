using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>Passes when ANY inner condition passes.</summary>
public sealed class OrCondition(IReadOnlyList<IRuleCondition> conditions) : IRuleCondition
{
    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        foreach (var condition in conditions)
        {
            if (condition.Evaluate(item, db, ancestry))
            {
                return true;
            }
        }

        return false;
    }
}
