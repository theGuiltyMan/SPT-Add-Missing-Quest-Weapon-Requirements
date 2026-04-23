using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;

namespace AddMissingQuestRequirements.Pipeline.Weapon;

/// <summary>
/// Builds the default weapon rule set from a list of weapon-like ancestor node
/// names. For each ancestor A, the factory probes the item database once and
/// emits either a {directChildOf:A} rule (when A has sub-Nodes between itself
/// and leaf Items) or a literal type=A rule (when A's descendant Items are
/// direct children). Keeps the catch-all current behaviour for Weapon while
/// auto-covering Knife, ThrowWeap, and any future ancestor the user adds.
/// </summary>
public static class DefaultWeaponRuleFactory
{
    public static IReadOnlyList<TypeRule> Build(
        IItemDatabase db,
        IReadOnlyList<string> ancestors)
    {
        if (ancestors.Count == 0)
        {
            return [];
        }

        var hasSubtree = DetectSubtreeAncestors(db, ancestors);

        var rules = new List<TypeRule>(ancestors.Count);
        foreach (var anc in ancestors)
        {
            var type = hasSubtree.Contains(anc)
                ? $"{{directChildOf:{anc}}}"
                : anc;

            rules.Add(new TypeRule
            {
                Conditions = new Dictionary<string, JsonElement>
                {
                    ["hasAncestor"] = JsonSerializer.SerializeToElement(anc),
                },
                Type = type,
            });
        }

        return rules;
    }

    /// <summary>
    /// An ancestor A "has a subtree" iff at least one Item in the DB has A
    /// somewhere in its parent chain AND the Item's direct parent is a Node
    /// whose Name is not A. Equivalently: there is at least one Node between
    /// A and the leaf.
    /// </summary>
    private static HashSet<string> DetectSubtreeAncestors(
        IItemDatabase db,
        IReadOnlyList<string> ancestors)
    {
        var lookup = new HashSet<string>(ancestors);
        var nameById = db.Items.Values.ToDictionary(n => n.Id, n => n.Name);
        var result = new HashSet<string>();

        foreach (var item in db.Items.Values)
        {
            if (item.NodeType != "Item")
            {
                continue;
            }

            var directParentId = item.ParentId;
            if (directParentId is null || !nameById.TryGetValue(directParentId, out var directParentName))
            {
                continue;
            }

            // Walk up from the direct parent. Any ancestor we pass that's
            // in the lookup set AND isn't the direct parent itself has a subtree.
            var cursor = directParentId;
            while (cursor is not null && db.Items.TryGetValue(cursor, out var node))
            {
                if (lookup.Contains(node.Name) && node.Name != directParentName)
                {
                    result.Add(node.Name);
                }
                cursor = node.ParentId;
            }
        }

        return result;
    }
}
