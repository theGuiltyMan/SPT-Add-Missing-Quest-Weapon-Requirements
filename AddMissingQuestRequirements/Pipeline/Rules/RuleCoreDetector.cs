using System.Text.Json;
using AddMissingQuestRequirements.Models;

namespace AddMissingQuestRequirements.Pipeline.Rules;

/// <summary>
/// Determines whether a <see cref="TypeRule"/> is a "core" rule — i.e., every leaf condition
/// key in its condition tree is one of <c>caliber</c>, <c>hasAncestor</c>, or <c>properties</c>.
/// <para>
/// Core rules describe intrinsic, structural facts about the item (its caliber, its parent
/// chain, its stored props). Name- and path-based rules are heuristic and may have been
/// deliberately overridden by a manual type override, so they are excluded from the core set.
/// </para>
/// <para>
/// Composite keys (<c>and</c>, <c>or</c>, <c>not</c>) are transparent — the detector recurses
/// through them to reach leaf keys. A composite whose children are all core is itself core.
/// An empty <c>Conditions</c> dict is NOT core (unconditional rules should not auto-merge).
/// </para>
/// </summary>
public static class RuleCoreDetector
{
    private static readonly HashSet<string> CoreLeafKeys = new(StringComparer.Ordinal)
    {
        "caliber",
        "hasAncestor",
        "properties",
    };

    private static readonly HashSet<string> CompositeKeys = new(StringComparer.Ordinal)
    {
        "and",
        "or",
        "not",
    };

    /// <summary>
    /// Returns <see langword="true"/> iff every leaf condition key reachable from
    /// <paramref name="conditions"/> is in <see cref="CoreLeafKeys"/>.
    /// Returns <see langword="false"/> when <paramref name="conditions"/> is empty (conservative).
    /// </summary>
    public static bool IsCore(Dictionary<string, JsonElement> conditions)
    {
        if (conditions.Count == 0)
        {
            return false;
        }

        foreach (var (key, value) in conditions)
        {
            if (!IsKeyCore(key, value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Recursively checks whether a single condition key + value subtree is entirely core.
    /// </summary>
    private static bool IsKeyCore(string key, JsonElement value)
    {
        if (CoreLeafKeys.Contains(key))
        {
            return true;
        }

        if (CompositeKeys.Contains(key))
        {
            return IsCompositeCore(key, value);
        }

        // Unknown key — treat as non-core (conservative).
        return false;
    }

    /// <summary>
    /// Checks composite conditions: <c>and</c> and <c>or</c> take a JSON array of condition
    /// objects; <c>not</c> takes a single condition object. Recurse into each child object's
    /// key-value pairs.
    /// </summary>
    private static bool IsCompositeCore(string compositeKey, JsonElement value)
    {
        if (compositeKey is "and" or "or")
        {
            // Value is a JSON array; each element is a condition object.
            if (value.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var element in value.EnumerateArray())
            {
                if (!IsConditionObjectCore(element))
                {
                    return false;
                }
            }

            return true;
        }

        if (compositeKey is "not")
        {
            // Value is a single condition object.
            if (value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return IsConditionObjectCore(value);
        }

        return false;
    }

    /// <summary>
    /// Checks a condition object (a JSON object whose properties are condition key-value pairs)
    /// by recursively evaluating each property.
    /// </summary>
    private static bool IsConditionObjectCore(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (!IsKeyCore(prop.Name, prop.Value))
            {
                return false;
            }
        }

        return true;
    }
}
