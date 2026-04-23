using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>Passes when any ancestor has the given _name.</summary>
public sealed class HasAncestorCondition(string ancestorName) : IRuleCondition
{
    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        return ancestry.HasAncestorWithName(item.Id, ancestorName, db);
    }
}
