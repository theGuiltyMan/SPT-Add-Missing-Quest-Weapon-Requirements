using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>Passes when _props.ammoCaliber equals the configured value.</summary>
public sealed class CaliberCondition(string caliber) : IRuleCondition
{
    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        if (!item.Props.TryGetValue("ammoCaliber", out var prop))
        {
            return false;
        }

        return prop.GetString() == caliber;
    }
}
