namespace AddMissingQuestRequirements.Pipeline.Weapon;

/// <summary>
/// Finds the most specific weapon type that covers a given set of weapon IDs.
/// Also computes the best-candidate type (covers all-but-one) when no full match exists.
/// </summary>
public sealed class TypeSelector  // sealed: no inheritance intended
{
    public record SelectionResult(string? BestType, string? BestCandidate, string? OutlierId);

    /// <param name="weaponIds">Weapon IDs in the condition.</param>
    /// <param name="weaponToType">Weapon ID → types it belongs to.</param>
    /// <param name="weaponTypes">Type name → set of weapon IDs in that type.</param>
    /// <param name="parentTypes">Child type → parent type; used to prefer child over parent.</param>
    public SelectionResult Select(
        IReadOnlyList<string> weaponIds,
        IReadOnlyDictionary<string, IReadOnlySet<string>> weaponToType,
        IReadOnlyDictionary<string, IReadOnlySet<string>> weaponTypes,
        IReadOnlyDictionary<string, string> parentTypes)
    {
        if (weaponIds.Count == 0)
        {
            return new SelectionResult(null, null, null);
        }

        var coveringTypes = FindCoveringTypes(weaponIds, weaponTypes);
        var bestType = SelectMostSpecific(coveringTypes, parentTypes, weaponTypes);

        if (bestType is not null)
        {
            return new SelectionResult(bestType, null, null);
        }

        // No single type covers all weapons — look for best-candidate (covers all-but-one)
        if (weaponIds.Count <= 1)
        {
            return new SelectionResult(null, null, null);
        }

        for (var i = 0; i < weaponIds.Count; i++)
        {
            var outlier  = weaponIds[i];
            var remaining = weaponIds.Where((_, idx) => idx != i).ToList();
            var candidates = FindCoveringTypes(remaining, weaponTypes);
            var candidate  = SelectMostSpecific(candidates, parentTypes, weaponTypes);

            if (candidate is not null)
            {
                return new SelectionResult(null, candidate, outlier);
            }
        }

        return new SelectionResult(null, null, null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<string> FindCoveringTypes(
        IReadOnlyList<string> weaponIds,
        IReadOnlyDictionary<string, IReadOnlySet<string>> weaponTypes)
    {
        return weaponTypes
            .Where(kvp => weaponIds.All(id => kvp.Value.Contains(id)))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    private static string? SelectMostSpecific(
        IReadOnlyList<string> types,
        IReadOnlyDictionary<string, string> parentTypes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> weaponTypes)
    {
        if (types.Count == 0)
        {
            return null;
        }

        if (types.Count == 1)
        {
            return types[0];
        }

        // Identify which covering types appear as parentTypes parents of other covering types.
        // Those are "less specific" — prefer the child types instead.
        var parentSet = new HashSet<string>(
            types.Select(t => parentTypes.GetValueOrDefault(t)!).Where(p => p is not null));

        // Prefer the smallest type (fewest members = most specific). Alphabetical as secondary
        // key for determinism when sizes are equal.
        var preferred = types
            .Where(t => !parentSet.Contains(t))
            .OrderBy(t => weaponTypes.TryGetValue(t, out var s) ? s.Count : int.MaxValue)
            .ThenBy(t => t)
            .ToList();

        return preferred.Count > 0
            ? preferred[0]
            : types
                .OrderBy(t => weaponTypes.TryGetValue(t, out var s) ? s.Count : int.MaxValue)
                .ThenBy(t => t)
                .First();
    }
}
