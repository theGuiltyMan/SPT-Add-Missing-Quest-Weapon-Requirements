using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Shared;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Weapon;

/// <summary>
/// Expands the <c>weapon</c> array of a CounterCreator condition using the weapon
/// categorizer output. Delegates shared bucket step to <see cref="GroupExpander"/>;
/// keeps weapon-specific logic: type expansion with caliber filter, best-candidate
/// fallback, <see cref="ExpansionMode"/>, include/exclude from overrides,
/// canBeUsedAs aliases with type-name resolution, and side-bucket reattach.
/// </summary>
public sealed class WeaponArrayExpander : IConditionExpander
{
    private readonly TypeSelector _typeSelector;
    private readonly INameResolver _nameResolver;

    public WeaponArrayExpander(TypeSelector typeSelector, INameResolver nameResolver)
    {
        _typeSelector = typeSelector;
        _nameResolver = nameResolver;
    }

    public void Expand(
        ConditionNode condition,
        QuestOverrideEntry? overrideEntry,
        CategorizationResult categorization,
        ModConfig config,
        IModLogger logger)
    {
        if (condition.Weapon.Count == 0)
        {
            return;
        }

        var mode = overrideEntry?.ExpansionMode ?? ExpansionMode.Auto;

        // ── Step 1: bucket + log (shared) ────────────────────────────────────
        var buckets = GroupExpander.BucketAndLog(
            condition.Weapon, categorization, config.UnknownWeaponHandling,
            logger, condition.Id, "weapon", _nameResolver);

        // ── Step 2: type expansion on categorized bucket (weapon-specific) ──
        var weapons = new List<string>(buckets.Categorized);

        if (mode == ExpansionMode.Auto && buckets.Categorized.Count >= 2)
        {
            var selection = _typeSelector.Select(
                buckets.Categorized,
                categorization.WeaponToType,
                categorization.WeaponTypes,
                config.ParentTypes);

            if (selection.BestType is not null)
            {
                weapons = ExpandToType(selection.BestType, condition.WeaponCaliber, categorization);
            }
            else if (config.BestCandidateExpansion && selection.BestCandidate is not null)
            {
                logger.Info(
                    $"No common weapon type — expanding as '{selection.BestCandidate}' " +
                    $"(outlier: '{_nameResolver.FormatItem(selection.OutlierId!)}').");

                weapons = ExpandToType(selection.BestCandidate, condition.WeaponCaliber, categorization);

                // Remove the outlier — it was not covered by the best candidate type
                weapons.Remove(selection.OutlierId!);
            }
        }

        // ── Step 3: included weapons additions ───────────────────────────────
        // NoExpansion: whitelist additions suppressed; only canBeUsedAs applied.
        // WhitelistOnly: list cleared, then replaced by includedWeapons exclusively.
        if (mode == ExpansionMode.WhitelistOnly)
        {
            weapons.Clear();
        }

        var seen = weapons.ToHashSet();

        if (mode != ExpansionMode.NoExpansion && overrideEntry is not null)
        {
            foreach (var entry in overrideEntry.IncludedWeapons)
            {
                AddResolved(entry, weapons, seen, categorization);
            }
        }

        // ── Step 4: excluded weapons → excludeSet (weapon-specific) ──────────
        // Excludes are applied BEFORE aliases so the alias step can still
        // re-introduce an ID; GroupExpander.ApplyAliasesAndReattach filters
        // the alias/reattach output by excludeSet so the final list is clean.
        var excludeSet = new HashSet<string>();
        if (overrideEntry is not null && overrideEntry.ExcludedWeapons.Count > 0)
        {
            foreach (var entry in overrideEntry.ExcludedWeapons)
            {
                if (categorization.WeaponTypes.TryGetValue(entry, out var members))
                {
                    excludeSet.UnionWith(members);
                }
                else
                {
                    excludeSet.Add(entry);
                }
            }

            weapons.RemoveAll(id => excludeSet.Contains(id));
        }

        // ── Step 5: aliases (with type-name resolution) + reattach (shared) ──
        weapons = GroupExpander.ApplyAliasesAndReattach(
            weapons, buckets, categorization, excludeSet);

        // ── Write back in-place ───────────────────────────────────────────────
        condition.Weapon.Clear();

        foreach (var id in weapons)
        {
            condition.Weapon.Add(id);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<string> ExpandToType(
        string typeName,
        IReadOnlyList<string> caliberFilter,
        CategorizationResult categorization)
    {
        if (!categorization.WeaponTypes.TryGetValue(typeName, out var typeMembers))
        {
            return [];
        }

        if (caliberFilter.Count == 0)
        {
            return [..typeMembers];
        }

        // Normalise display strings (e.g. "5.56x45") to SPT ammoCaliber IDs
        // (e.g. "Caliber556x45NATO") before building the filter set.
        // Tokens already in CaliberXXX form, or otherwise unknown, pass through unchanged.
        var caliberSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in caliberFilter)
        {
            caliberSet.Add(CaliberNormalizer.ToSpt(raw));
        }

        var result = new List<string>();

        foreach (var id in typeMembers)
        {
            if (categorization.WeaponToCaliber.TryGetValue(id, out var caliber) &&
                caliberSet.Contains(caliber))
            {
                result.Add(id);
            }
            else if (!categorization.WeaponToCaliber.ContainsKey(id))
            {
                // No caliber info — include conservatively
                result.Add(id);
            }
        }

        return result;
    }

    private static void AddResolved(
        string entry,
        List<string> target,
        HashSet<string> seen,
        CategorizationResult categorization)
    {
        if (categorization.WeaponTypes.TryGetValue(entry, out var members))
        {
            foreach (var id in members)
            {
                if (seen.Add(id))
                {
                    target.Add(id);
                }
            }
        }
        else if (seen.Add(entry))
        {
            target.Add(entry);
        }
    }
}
