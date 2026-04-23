using AddMissingQuestRequirements.Pipeline.Database;

namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Walks and caches the _parent chain of items in the SPT item database.
/// </summary>
public sealed class AncestryResolver
{
    private readonly Dictionary<string, IReadOnlyList<ItemNode>> _cache = [];

    /// <summary>
    /// Returns ancestors from immediate parent up to (and including) the root,
    /// stopping if a parent ID is not found or a cycle is detected.
    /// </summary>
    public IReadOnlyList<ItemNode> GetAncestors(string itemId, IItemDatabase db)
    {
        if (_cache.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        var ancestors = new List<ItemNode>();
        var visited = new HashSet<string> { itemId };
        var current = itemId;

        while (true)
        {
            if (!db.Items.TryGetValue(current, out var node))
            {
                break;
            }

            if (node.ParentId is null || !db.Items.ContainsKey(node.ParentId))
            {
                break;
            }

            if (!visited.Add(node.ParentId))
            {
                break; // cycle detected
            }

            var parent = db.Items[node.ParentId];
            ancestors.Add(parent);

            // If the parent's chain is already resolved, splice it in and stop early
            if (_cache.TryGetValue(node.ParentId, out var cachedParentChain))
            {
                ancestors.AddRange(cachedParentChain);
                break;
            }

            current = node.ParentId;
        }

        var result = ancestors.AsReadOnly();
        _cache[itemId] = result;
        return result;
    }

    /// <summary>Returns true if any ancestor has the given _name.</summary>
    public bool HasAncestorWithName(string itemId, string ancestorName, IItemDatabase db)
    {
        return GetAncestors(itemId, db).Any(a => a.Name == ancestorName);
    }

    /// <summary>
    /// Returns the slash-separated path from root to item, e.g. "Item/Weapon/AssaultRifle/AKS74U".
    /// </summary>
    public string GetAncestorPath(string itemId, IItemDatabase db)
    {
        if (!db.Items.TryGetValue(itemId, out var item))
        {
            return itemId;
        }

        var ancestors = GetAncestors(itemId, db);
        var parts = ancestors.Reverse().Select(a => a.Name).Append(item.Name);
        return string.Join("/", parts);
    }

    /// <summary>
    /// Returns the _name of the ancestor that is a direct child of the node named
    /// <paramref name="targetAncestorName"/> in <paramref name="itemId"/>'s chain, or null if
    /// <paramref name="targetAncestorName"/> is not in the chain.
    /// </summary>
    public string? GetDirectChildOf(string targetAncestorName, string itemId, IItemDatabase db)
    {
        var ancestors = GetAncestors(itemId, db);

        for (var i = 0; i < ancestors.Count - 1; i++)
        {
            if (ancestors[i + 1].Name == targetAncestorName)
            {
                return ancestors[i].Name;
            }
        }

        // Also check if the item itself is a direct child of the target
        if (ancestors.Count > 0 && ancestors[0].Name == targetAncestorName)
        {
            return db.Items.TryGetValue(itemId, out var self) ? self.Name : null;
        }

        return null;
    }
}
