using System.Text.Json;
using System.Text.Json.Serialization;

namespace AddMissingQuestRequirements.Models;

public sealed class TypeRule
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>
    /// Condition key-value pairs. Values are raw JsonElement to support
    /// both plain strings (e.g. "hasAncestor": "SniperRifle") and
    /// objects (e.g. "properties": { "BoltAction": true }).
    /// </summary>
    [JsonPropertyName("conditions")]
    public Dictionary<string, JsonElement> Conditions { get; init; } = [];

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("alsoAs")]
    public List<string> AlsoAs { get; init; } = [];

    /// <summary>
    /// Reserved. Currently unused — the rule engine evaluates every matching rule
    /// regardless of priority, and user rules always merge after built-in rules.
    /// Kept for forward compatibility; do not rely on it.
    /// </summary>
    [JsonPropertyName("priority")]
    public string? Priority { get; init; }

    /// <summary>
    /// Per-rule override behaviour applied during TypeRule list merging, keyed on <see cref="Type"/>.
    /// null = default (always append). See <see cref="MergeHelper.MergeTypeRules"/> for semantics.
    /// </summary>
    [JsonPropertyName("behaviour")]
    public OverrideBehaviour? Behaviour { get; init; }
}
