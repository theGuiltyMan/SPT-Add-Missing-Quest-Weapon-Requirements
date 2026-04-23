using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>Passes when ALL inner conditions pass.</summary>
public sealed class AndCondition(IReadOnlyList<IRuleCondition> conditions) : IRuleCondition
{
    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        foreach (var condition in conditions)
        {
            if (!condition.Evaluate(item, db, ancestry))
            {
                return false;
            }
        }

        return true;
    }
}
