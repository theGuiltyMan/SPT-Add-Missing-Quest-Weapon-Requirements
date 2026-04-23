using System.Text.Json;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;

/// <summary>
/// Passes when all key-value pairs in the config object are present and equal in _props.
/// Example config: { "BoltAction": true }
/// </summary>
public sealed class PropertiesCondition(JsonElement config) : IRuleCondition
{
    public bool Evaluate(ItemNode item, IItemDatabase db, AncestryResolver ancestry)
    {
        foreach (var required in config.EnumerateObject())
        {
            if (!item.Props.TryGetValue(required.Name, out var actual))
            {
                return false;
            }

            if (actual.ValueKind != required.Value.ValueKind)
            {
                return false;
            }

            if (!ValuesEqual(actual, required.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValuesEqual(JsonElement actual, JsonElement required)
    {
        return actual.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => actual.GetBoolean() == required.GetBoolean(),
            JsonValueKind.String                      => actual.GetString() == required.GetString(),
            JsonValueKind.Number                      => actual.GetDouble() == required.GetDouble(),
            _                                         => actual.GetRawText() == required.GetRawText()
        };
    }
}
