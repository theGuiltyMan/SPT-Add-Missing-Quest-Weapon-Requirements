using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>Evaluates one condition clause from a TypeRule's conditions block.</summary>
public interface IRuleCondition
{
    bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry);
}
