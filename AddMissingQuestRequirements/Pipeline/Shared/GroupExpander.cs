using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Shared;

/// <summary>
/// Shared bucketing and reattach logic for condition-group expansion.
/// <see cref="BucketAndLog"/> splits IDs by category/DB/unknown and emits the
/// appropriate Debug/Warning log lines.
/// <see cref="ApplyAliasesAndReattach"/> applies canBeUsedAs aliases on a
/// working list and reattaches preserved side-buckets per the handling mode.
/// Domain-specific logic (type selection, caliber filter, include/exclude,
/// expansion mode) stays with the caller.
/// </summary>
public static class GroupExpander
{
    /// <summary>
    /// Splits an ID list into three buckets based on categorization and database membership,
    /// emits per-ID log messages, and returns flags derived from the handling mode.
    /// </summary>
    /// <param name="ids">Raw IDs from the condition field.</param>
    /// <param name="categorization">Shared categorizer interface (weapons or attachments).</param>
    /// <param name="handling">Policy for unknown IDs.</param>
    /// <param name="logger">Logger for per-ID messages.</param>
    /// <param name="conditionId">Condition ID for logging context.</param>
    /// <param name="fieldName">Field name for logging context (e.g., "weapon", "weaponModsInclusive").</param>
    /// <param name="nameResolver">Resolver for "<c>name (id)</c>" formatting in log lines.</param>
    /// <returns>Buckets and flags for use by ApplyAliasesAndReattach.</returns>
    public static ExpansionBuckets BucketAndLog(
        IReadOnlyList<string> ids,
        IItemCategorization categorization,
        UnknownWeaponHandling handling,
        IModLogger logger,
        string conditionId,
        string fieldName,
        INameResolver nameResolver)
    {
        var categorized       = new List<string>();
        var uncategorizedInDb = new List<string>();
        var notInDb           = new List<string>();

        foreach (var id in ids)
        {
            if (categorization.ItemToType.ContainsKey(id))
            {
                categorized.Add(id);
            }
            else if (categorization.KnownItemIds.Contains(id))
            {
                uncategorizedInDb.Add(id);
            }
            else
            {
                notInDb.Add(id);
            }
        }

        var keepUncategorizedInDb = handling != UnknownWeaponHandling.Strip;
        var keepNotInDb           = handling == UnknownWeaponHandling.KeepAll;

        foreach (var id in uncategorizedInDb)
        {
            if (keepUncategorizedInDb)
            {
                logger.Debug($"{fieldName}: uncategorized ID '{nameResolver.FormatItem(id)}' kept (in DB, no type match).");
            }
            else
            {
                logger.Warning($"{fieldName}: uncategorized ID '{nameResolver.FormatItem(id)}' removed (UnknownWeaponHandling=Strip).");
            }
        }

        foreach (var id in notInDb)
        {
            if (keepNotInDb)
            {
                logger.Debug($"{fieldName}: unknown ID '{nameResolver.FormatItem(id)}' kept (UnknownWeaponHandling=KeepAll).");
            }
            else
            {
                logger.Warning($"{fieldName}: unknown ID '{nameResolver.FormatItem(id)}' — removed (not in item database).");
            }
        }

        return new ExpansionBuckets
        {
            Categorized           = categorized,
            UncategorizedInDb     = uncategorizedInDb,
            NotInDb               = notInDb,
            KeepUncategorizedInDb = keepUncategorizedInDb,
            KeepNotInDb           = keepNotInDb,
        };
    }

    /// <summary>
    /// Applies canBeUsedAs aliases to a working list and reattaches preserved side-buckets
    /// with optional exclude filtering.
    /// The working list is mutated in-place and returned.
    /// Aliases are computed on a snapshot of working before mutation, preventing recursive aliasing.
    /// </summary>
    /// <param name="working">Mutable list of IDs (usually the expanded set). Will be mutated.</param>
    /// <param name="buckets">Buckets and flags from BucketAndLog.</param>
    /// <param name="categorization">Shared categorizer interface for alias lookups.</param>
    /// <param name="excludeSet">Optional set of IDs to exclude from alias or reattach steps.</param>
    /// <returns>The mutated working list.</returns>
    public static List<string> ApplyAliasesAndReattach(
        List<string> working,
        ExpansionBuckets buckets,
        IItemCategorization categorization,
        HashSet<string>? excludeSet = null)
    {
        var seen = working.ToHashSet();

        // canBeUsedAs aliases (snapshot before mutation).
        // An alias value may be either a plain item ID or a type name — type names
        // resolve to every member of that type. This matches weapon semantics where
        // `{ "weapon_x": ["M4A1"] }` expands to every weapon tagged M4A1. Attachments
        // today do not author type-name aliases, so the TypeToItems lookup misses and
        // the alias is added as a bare ID — symmetric, backward-compatible behavior.
        foreach (var id in working.ToList())
        {
            if (!categorization.CanBeUsedAs.TryGetValue(id, out var aliases))
            {
                continue;
            }

            foreach (var alias in aliases)
            {
                if (categorization.TypeToItems.TryGetValue(alias, out var typeMembers))
                {
                    foreach (var memberId in typeMembers)
                    {
                        if (excludeSet is not null && excludeSet.Contains(memberId))
                        {
                            continue;
                        }
                        if (seen.Add(memberId))
                        {
                            working.Add(memberId);
                        }
                    }
                }
                else
                {
                    if (excludeSet is not null && excludeSet.Contains(alias))
                    {
                        continue;
                    }
                    if (seen.Add(alias))
                    {
                        working.Add(alias);
                    }
                }
            }
        }

        // Reattach preserved side-buckets
        if (buckets.KeepUncategorizedInDb)
        {
            foreach (var id in buckets.UncategorizedInDb)
            {
                if (excludeSet is not null && excludeSet.Contains(id))
                {
                    continue;
                }
                if (seen.Add(id))
                {
                    working.Add(id);
                }
            }
        }

        if (buckets.KeepNotInDb)
        {
            foreach (var id in buckets.NotInDb)
            {
                if (excludeSet is not null && excludeSet.Contains(id))
                {
                    continue;
                }
                if (seen.Add(id))
                {
                    working.Add(id);
                }
            }
        }

        return working;
    }
}
