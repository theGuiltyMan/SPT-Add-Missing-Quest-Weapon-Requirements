using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Rules;

namespace AddMissingQuestRequirements.Pipeline.Shared;

/// <summary>
/// Shared static utilities for item categorization.
/// Used by both <see cref="Weapon.WeaponCategorizer"/> and <see cref="Attachment.AttachmentCategorizer"/>.
/// </summary>
internal static class CategorizationHelper
{
    internal static void AddAlias(
        Dictionary<string, HashSet<string>> map,
        string id,
        IEnumerable<string> aliases)
    {
        if (!map.TryGetValue(id, out var set))
        {
            set = [];
            map[id] = set;
        }

        foreach (var alias in aliases)
        {
            set.Add(alias);
        }
    }

    internal static Dictionary<string, string> BuildNormalizedNames(
        IItemDatabase db,
        IEnumerable<string> ids,
        IReadOnlyList<string> stripWords)
    {
        var result = new Dictionary<string, string>();

        foreach (var id in ids)
        {
            var name = db.GetLocaleName(id);
            if (name is null)
            {
                continue;
            }

            result[id] = RemoveIgnoredWords(name, stripWords);
        }

        return result;
    }

    internal static void ExpandTransitiveGroups(
        Dictionary<string, HashSet<string>> map,
        HashSet<string> knownIds)
    {
        var visited = new HashSet<string>();

        foreach (var startId in map.Keys.ToList())
        {
            if (visited.Contains(startId))
            {
                continue;
            }

            var group = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(startId);

            // BFS to collect the connected component
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!group.Add(current))
                {
                    continue;
                }

                visited.Add(current);

                if (!map.TryGetValue(current, out var neighbors))
                {
                    continue;
                }

                foreach (var neighbor in neighbors)
                {
                    if (!group.Contains(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Cross-link every known member with every other known member in the group.
            // Unknown IDs stay as alias targets but never become keys in the graph.
            var knownGroup = group.Where(knownIds.Contains).ToList();
            foreach (var id in knownGroup)
            {
                if (!map.TryGetValue(id, out var links))
                {
                    links = [];
                    map[id] = links;
                }

                foreach (var other in knownGroup)
                {
                    if (other != id)
                    {
                        links.Add(other);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Core item-classification loop: for each item, applies the manual override or the rule engine,
    /// then populates both the item-to-type and type-to-items maps.
    /// <para>
    /// <paramref name="items"/> should already be pre-filtered by the caller (e.g. leaf-node check,
    /// ancestry filter). <paramref name="getTypes"/> controls which types a rule match contributes
    /// (e.g. always include alsoAs for attachments; conditionally for weapons).
    /// </para>
    /// </summary>
    internal static (Dictionary<string, HashSet<string>> itemToType, Dictionary<string, HashSet<string>> typeToItems) BuildTypeMaps(
        IEnumerable<ItemNode> items,
        RuleEngine engine,
        Dictionary<string, string> manualOverrides,
        Func<RuleMatch, IEnumerable<string>> getTypes,
        Func<string, IEnumerable<string>>? expandManualType = null)
    {
        // Default: manual override types are added as-is. Weapons pass an expander
        // that walks parentTypes so e.g. "GrenadeLauncher" also pulls in "explosive".
        expandManualType ??= t => [t];

        var itemToType  = new Dictionary<string, HashSet<string>>();
        var typeToItems = new Dictionary<string, HashSet<string>>();

        foreach (var item in items)
        {
            var typeSet = new HashSet<string>();
            var hasManualOverride = manualOverrides.TryGetValue(item.Id, out var overrideStr);

            // Seed the type set from the manual override when one is present.
            if (hasManualOverride)
            {
                foreach (var t in overrideStr!.Split(
                    ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    foreach (var expanded in expandManualType(t))
                    {
                        typeSet.Add(expanded);
                    }
                }
            }

            // Apply rule matches: when a manual override is present, only core rules
            // (caliber / hasAncestor / properties) are merged in — non-core rules
            // (nameContains, nameMatches, pathMatches, descriptionMatches) are suppressed
            // because the user may have overridden them deliberately.
            // When no manual override is present, all matching rules fire as before.
            var matches = engine.EvaluateAll(item);
            foreach (var match in matches)
            {
                if (hasManualOverride && !match.IsCore)
                {
                    continue;
                }

                foreach (var t in getTypes(match))
                {
                    typeSet.Add(t);
                }
            }

            if (typeSet.Count == 0)
            {
                continue;
            }

            itemToType[item.Id] = typeSet;

            foreach (var t in typeSet)
            {
                if (!typeToItems.TryGetValue(t, out var ids))
                {
                    ids = [];
                    typeToItems[t] = ids;
                }

                ids.Add(item.Id);
            }
        }

        return (itemToType, typeToItems);
    }

    internal static IReadOnlyDictionary<string, IReadOnlySet<string>> AsReadOnly(
        Dictionary<string, HashSet<string>> source)
    {
        return source.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlySet<string>)kvp.Value);
    }

    internal static string RemoveIgnoredWords(string name, IReadOnlyList<string> stripWords)
    {
        if (stripWords.Count == 0)
        {
            return name;
        }

        var words    = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = words.Where(w =>
            !stripWords.Any(sw => string.Equals(w, sw, StringComparison.OrdinalIgnoreCase)));

        return string.Join(' ', filtered);
    }
}
