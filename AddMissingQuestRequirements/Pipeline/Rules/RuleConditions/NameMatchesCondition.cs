using System.Text.RegularExpressions;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>Passes when the item's locale name matches the given regex (case-insensitive).</summary>
public sealed class NameMatchesCondition(string pattern) : IRuleCondition
{
    private readonly Regex _regex = new(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexGuard.Timeout);

    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        var name = db.GetLocaleName(item.Id);
        if (name is null)
        {
            return false;
        }

        return RegexGuard.IsMatchSafe(_regex, name);
    }
}
