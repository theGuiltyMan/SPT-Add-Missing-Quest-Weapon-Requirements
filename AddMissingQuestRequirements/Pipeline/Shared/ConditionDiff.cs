namespace AddMissingQuestRequirements.Pipeline.Shared;

/// <summary>
/// Shared change-detection helpers used by both the SPT runtime summary and
/// the inspector Noop check. "Changed" means the condition's semantic state
/// after patching differs from its pre-patch snapshot — reordering, duplicate
/// folding, and empty AND-bundle cleanup do NOT count as changes.
/// </summary>
public static class ConditionDiff
{
    /// <summary>
    /// True iff the set of IDs differs (count or membership). Order within
    /// each list is irrelevant — quest evaluation treats the weapon array as
    /// a flat set.
    /// </summary>
    public static bool WeaponsChanged(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        if (before.Count != after.Count)
        {
            return true;
        }

        var beforeSet = new HashSet<string>(before);
        foreach (var id in after)
        {
            if (!beforeSet.Contains(id))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True iff the set of non-empty AND-bundles differs. Each bundle is
    /// compared as a sorted-ID key; bundles compare across both sides as a
    /// sorted list of those keys. Empty bundles are dropped because an empty
    /// AND matches nothing and has no effect on quest evaluation.
    /// </summary>
    public static bool GroupsChanged(
        IReadOnlyList<IReadOnlyList<string>> before,
        IReadOnlyList<IReadOnlyList<string>> after)
    {
        var beforeKeys = NonEmptyKeys(before);
        var afterKeys = NonEmptyKeys(after);
        return !beforeKeys.SequenceEqual(afterKeys);
    }

    private static List<string> NonEmptyKeys(IReadOnlyList<IReadOnlyList<string>> groups)
    {
        var keys = new List<string>();
        foreach (var group in groups)
        {
            if (group.Count == 0)
            {
                continue;
            }
            keys.Add(string.Join(",", group.OrderBy(s => s, StringComparer.Ordinal)));
        }
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }
}
