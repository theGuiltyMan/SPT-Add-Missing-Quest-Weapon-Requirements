using System.Text.RegularExpressions;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules;

/// <summary>
/// The result of a single rule match: the resolved type name, the rule's alsoAs list,
/// and whether the matching rule was a "core" rule (caliber / hasAncestor / properties only).
/// </summary>
public sealed record RuleMatch(string Type, IReadOnlyList<string> AlsoAs, bool IsCore);

/// <summary>
/// Evaluates an ordered list of <see cref="TypeRule"/>s against an item.
/// Returns the first match, or null if no rule matches.
/// Resolves <c>{directChildOf:X}</c> templates in the type field at evaluation time.
/// </summary>
public sealed class RuleEngine
{
    private static readonly Regex DirectChildOfPattern =
        new(@"^\{directChildOf:(.+)\}$", RegexOptions.Compiled);

    /// <summary>Rules pre-compiled to <see cref="IRuleCondition"/> instances once at construction.</summary>
    private readonly IReadOnlyList<CompiledRule> _compiled;
    private readonly IItemDatabase _db;
    private readonly AncestryResolver _ancestry = new();

    /// <summary>The shared ancestry resolver used during rule evaluation — callers may reuse it to avoid duplicate traversal.</summary>
    internal AncestryResolver Ancestry => _ancestry;

    private readonly record struct CompiledRule(
        IReadOnlyList<IRuleCondition> Conditions,
        TypeRule Rule,
        bool IsCore);

    public RuleEngine(IEnumerable<TypeRule> rules, IItemDatabase db)
    {
        _db = db;
        _compiled = [..rules.Select(r => new CompiledRule(
            [..r.Conditions.Select(kvp => ConditionFactory.Create(kvp.Key, kvp.Value))],
            r,
            RuleCoreDetector.IsCore(r.Conditions)
        ))];
    }

    /// <summary>
    /// Returns all matching rules' types (with templates resolved) and alsoAs lists.
    /// Every rule whose conditions pass contributes to the result.
    /// Returns an empty list if no rule matches.
    /// </summary>
    public IReadOnlyList<RuleMatch> EvaluateAll(ItemNode item)
    {
        var matches = new List<RuleMatch>();

        foreach (var compiled in _compiled)
        {
            if (!AllConditionsPass(compiled.Conditions, item))
            {
                continue;
            }

            var resolvedType = ResolveType(compiled.Rule.Type, item);
            if (resolvedType is null)
            {
                continue; // template couldn't resolve (e.g. ancestor not found)
            }

            matches.Add(new RuleMatch(resolvedType, compiled.Rule.AlsoAs, compiled.IsCore));
        }

        return matches;
    }

    private bool AllConditionsPass(IReadOnlyList<IRuleCondition> conditions, ItemNode item)
    {
        foreach (var condition in conditions)
        {
            if (!condition.Evaluate(item, _db, _ancestry))
            {
                return false;
            }
        }

        return true;
    }

    private string? ResolveType(string typeTemplate, ItemNode item)
    {
        var match = DirectChildOfPattern.Match(typeTemplate);
        if (!match.Success)
        {
            return typeTemplate;
        }

        var targetAncestor = match.Groups[1].Value;
        return _ancestry.GetDirectChildOf(targetAncestor, item.Id, _db);
    }
}
