using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>Passes when the inner condition fails.</summary>
public sealed class NotCondition(IRuleCondition inner) : IRuleCondition
{
    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        return !inner.Evaluate(item, db, ancestry);
    }
}
