using System.Text.RegularExpressions;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>
/// Passes when the item's full ancestor path (e.g. "Item/Weapon/AssaultRifle/AKS74U")
/// matches the given regex.
/// </summary>
public sealed class PathMatchesCondition(string pattern) : IRuleCondition
{
    private readonly Regex _regex = new(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexGuard.Timeout);

    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        var path = ancestry.GetAncestorPath(item.Id, db);
        return RegexGuard.IsMatchSafe(_regex, path);
    }
}
