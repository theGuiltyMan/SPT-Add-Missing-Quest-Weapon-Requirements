using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>Passes when the item's locale name contains the given substring (case-insensitive).</summary>
public sealed class NameContainsCondition(string substring) : IRuleCondition
{
    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        var name = db.GetLocaleName(item.Id);
        if (name is null)
        {
            return false;
        }

        return name.Contains(substring, StringComparison.OrdinalIgnoreCase);
    }
}
