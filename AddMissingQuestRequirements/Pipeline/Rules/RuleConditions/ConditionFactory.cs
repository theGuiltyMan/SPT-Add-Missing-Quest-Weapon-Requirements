using System.Text.Json;
using AddMissingQuestRequirements.Pipeline.Database;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>
/// Builds <see cref="IRuleCondition"/> instances from rule condition key-value pairs.
/// Supports meta-conditions: "and", "or", "not" (recursive), and leaf conditions.
/// </summary>
public static class ConditionFactory
{
    public static IRuleCondition Create(string key, JsonElement value)
    {
        return key switch
        {
            "hasAncestor"        => new HasAncestorCondition(value.GetString()!),
            "properties"         => new PropertiesCondition(value),
            "caliber"            => new CaliberCondition(value.GetString()!),
            "nameContains"       => new NameContainsCondition(value.GetString()!),
            "nameMatches"        => new NameMatchesCondition(value.GetString()!),
            "pathMatches"        => new PathMatchesCondition(value.GetString()!),
            "descriptionMatches" => new DescriptionMatchesCondition(value.GetString()!),
            "and"                => CreateAnd(value),
            "or"                 => CreateOr(value),
            "not"                => CreateNot(value),
            _                    => throw new ArgumentException($"Unknown condition key: '{key}'")
        };
    }

    // ── Meta-condition builders ──────────────────────────────────────────────

    private static AndCondition CreateAnd(JsonElement arrayValue)
    {
        var conditions = BuildConditionList(arrayValue);
        return new AndCondition(conditions);
    }

    private static OrCondition CreateOr(JsonElement arrayValue)
    {
        var conditions = BuildConditionList(arrayValue);
        return new OrCondition(conditions);
    }

    private static NotCondition CreateNot(JsonElement objectValue)
    {
        var inner = BuildConditionsFromObject(objectValue);
        return new NotCondition(inner);
    }

    /// <summary>
    /// Given a JsonElement that is a JSON array of condition objects,
    /// parses each element as a condition object and returns the list.
    /// </summary>
    private static List<IRuleCondition> BuildConditionList(JsonElement arrayValue)
    {
        var result = new List<IRuleCondition>();

        foreach (var element in arrayValue.EnumerateArray())
        {
            result.Add(BuildConditionsFromObject(element));
        }

        return result;
    }

    /// <summary>
    /// Given a JsonElement that is a JSON object with one or more condition keys,
    /// parses each property via <see cref="Create"/> and returns:
    /// - a single condition when there is exactly one property
    /// - an <see cref="AndCondition"/> wrapping all conditions when there are multiple properties
    /// Throws <see cref="ArgumentException"/> if the object has no properties.
    /// </summary>
    private static IRuleCondition BuildConditionsFromObject(JsonElement objectValue)
    {
        var conditions = new List<IRuleCondition>();

        foreach (var prop in objectValue.EnumerateObject())
        {
            conditions.Add(Create(prop.Name, prop.Value));
        }

        if (conditions.Count == 0)
        {
            throw new ArgumentException("Condition object must have at least one property.");
        }

        if (conditions.Count == 1)
        {
            return conditions[0];
        }

        return new AndCondition(conditions);
    }
}
