using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Rules;

namespace AddMissingQuestRequirements.Pipeline.Shared;

/// <summary>
/// Domain-agnostic categorization pipeline shared by weapon and attachment
/// categorizers. Performs rule-engine sweep, canBeUsedAs seeding, short-name
/// alias grouping, and transitive closure. Does NOT populate
/// <see cref="IItemCategorization.KnownItemIds"/> — callers do that — and does
/// NOT pre-filter items or extract domain-specific metadata (caliber etc).
/// </summary>
public static class CategorizerCore
{
    public static (
        Dictionary<string, HashSet<string>> ItemToType,
        Dictionary<string, HashSet<string>> TypeToItems,
        Dictionary<string, HashSet<string>> CanBeUsedAs
    ) Categorize(
        IItemDatabase db,
        IEnumerable<ItemNode> preFilteredItems,
        RuleEngine engine,
        CategorizerInput input)
    {
        var (itemToType, typeToItems) = CategorizationHelper.BuildTypeMaps(
            preFilteredItems,
            engine,
            input.ManualOverrides,
            input.GetTypes,
            input.ExpandManualType);

        var canBeUsedAs = new Dictionary<string, HashSet<string>>();

        // Seed with manually configured aliases (bidirectional for known IDs only)
        foreach (var (id, aliases) in input.CanBeUsedAsSeeds)
        {
            CategorizationHelper.AddAlias(canBeUsedAs, id, aliases);
            foreach (var alias in aliases)
            {
                // Only reverse-link aliases that refer to a categorized item;
                // unknown IDs (from late-loading mods, typos) must not become graph keys.
                if (itemToType.ContainsKey(alias))
                {
                    CategorizationHelper.AddAlias(canBeUsedAs, alias, [id]);
                }
            }
        }

        // Short-name alias matching: group by normalized name, cross-link each group — O(N)
        var categorized = itemToType.Keys.ToList();
        var normalized = CategorizationHelper.BuildNormalizedNames(db, categorized, input.AliasStripWords);

        // Build exclude sets: entries may be item IDs or normalized short-names.
        // Normalize each entry through the same strip-word pass used for grouping so that
        // a user who writes "FDE" (a strip word) still gets a meaningful name-match.
        var excludeIds = input.AliasExcludeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludeNames = input.AliasExcludeIds
            .Select(e => CategorizationHelper.RemoveIgnoredWords(e, input.AliasStripWords))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Group item IDs by their normalized locale name, skipping excluded entries
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in normalized)
        {
            if (excludeIds.Contains(id) || excludeNames.Contains(name))
            {
                continue;
            }

            if (!byName.TryGetValue(name, out var group))
            {
                group = [];
                byName[name] = group;
            }

            group.Add(id);
        }

        // Cross-link every pair within each name group
        foreach (var group in byName.Values)
        {
            if (group.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < group.Count; i++)
            {
                for (var j = i + 1; j < group.Count; j++)
                {
                    CategorizationHelper.AddAlias(canBeUsedAs, group[i], [group[j]]);
                    CategorizationHelper.AddAlias(canBeUsedAs, group[j], [group[i]]);
                }
            }
        }

        // Transitive group expansion: all members of a connected component are cross-linked
        CategorizationHelper.ExpandTransitiveGroups(canBeUsedAs, itemToType.Keys.ToHashSet());

        return (itemToType, typeToItems, canBeUsedAs);
    }
}
