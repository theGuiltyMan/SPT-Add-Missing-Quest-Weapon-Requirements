using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Shared;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Attachment;

/// <summary>
/// Rewrites <c>weaponModsInclusive</c> and <c>weaponModsExclusive</c> under
/// intra-group AND, cross-group OR semantics.
///
/// <para><b>Per-field overrides (since 2.1.0):</b>
/// <c>includedMods</c> + <c>includedModBundles</c> append only to
/// <c>weaponModsInclusive</c>. <c>excludedMods</c> + <c>excludedModBundles</c>
/// append only to <c>weaponModsExclusive</c>. Neither set drops groups from
/// either field.</para>
///
/// <para><b>Singleton consensus expansion (Auto):</b> when a field has ≥2
/// singletons sharing a single minimal covering type, expansion emits one
/// singleton per type member (plus <c>canBeUsedAs</c> aliases). Multi-item
/// groups are AND bundles and pass through verbatim.</para>
///
/// <para><b>Bundle fields:</b> each bundle is a list of <i>sets</i>
/// (type-name = members, bare id = singleton-set). Output = cartesian product,
/// one AND-bundle per combination. Truncated to
/// <see cref="ModConfig.ModBundleCartesianCap"/>.</para>
/// </summary>
public sealed class WeaponModsExpander : IConditionExpander
{
    private readonly AttachmentCategorizationResult _attachmentCategorization;
    private readonly TypeSelector _typeSelector;
    private readonly INameResolver _nameResolver;

    public WeaponModsExpander(AttachmentCategorizationResult attachmentCategorization, INameResolver nameResolver)
    {
        _attachmentCategorization = attachmentCategorization;
        _typeSelector             = new TypeSelector();
        _nameResolver             = nameResolver;
    }

    public void Expand(
        ConditionNode condition,
        QuestOverrideEntry? overrideEntry,
        CategorizationResult categorization,
        ModConfig config,
        IModLogger logger)
    {
        var hasOverrideWork =
            overrideEntry is not null
            && (overrideEntry.IncludedMods.Count > 0
                || overrideEntry.ExcludedMods.Count > 0
                || overrideEntry.IncludedModBundles.Count > 0
                || overrideEntry.ExcludedModBundles.Count > 0
                || overrideEntry.ModsExpansionMode != ExpansionMode.Auto);

        if (condition.WeaponModsInclusive.Count == 0
            && condition.WeaponModsExclusive.Count == 0
            && !hasOverrideWork)
        {
            return;
        }

        var newInclusive = RewriteField(
            condition.WeaponModsInclusive,
            condition.Id,
            "weaponModsInclusive",
            overrideEntry?.ModsExpansionMode ?? ExpansionMode.Auto,
            overrideEntry?.IncludedMods ?? [],
            overrideEntry?.IncludedModBundles ?? [],
            config,
            logger);

        var newExclusive = RewriteField(
            condition.WeaponModsExclusive,
            condition.Id,
            "weaponModsExclusive",
            overrideEntry?.ModsExpansionMode ?? ExpansionMode.Auto,
            overrideEntry?.ExcludedMods ?? [],
            overrideEntry?.ExcludedModBundles ?? [],
            config,
            logger);

        condition.WeaponModsInclusive.Clear();
        condition.WeaponModsInclusive.AddRange(newInclusive);

        condition.WeaponModsExclusive.Clear();
        condition.WeaponModsExclusive.AddRange(newExclusive);
    }

    // ── Core rewrite ─────────────────────────────────────────────────────────

    /// <summary>
    /// Rewrites a single field. <paramref name="appendEntries"/> are appended as
    /// singletons (post-expansion of type names). <paramref name="appendBundles"/>
    /// are appended as cartesian AND-bundles, capped by config.
    /// </summary>
    private List<List<string>> RewriteField(
        List<List<string>> original,
        string conditionId,
        string fieldName,
        ExpansionMode mode,
        IReadOnlyList<string> appendEntries,
        IReadOnlyList<List<string>> appendBundles,
        ModConfig config,
        IModLogger logger)
    {
        var output = new List<List<string>>();

        if (mode == ExpansionMode.WhitelistOnly)
        {
            // Originals discarded; field rebuilt from append-only sources.
            foreach (var id in ResolveEntries(appendEntries))
            {
                output.Add([id]);
            }
        }
        else if (mode == ExpansionMode.NoExpansion)
        {
            foreach (var group in original)
            {
                var kept = FilterByUnknownHandling(group, config, logger, conditionId, fieldName);
                if (kept.Count == 0)
                {
                    continue;
                }

                output.Add(kept);
            }

            foreach (var id in ResolveEntries(appendEntries))
            {
                output.Add([id]);
            }
        }
        else
        {
            var singletons = new List<List<string>>();
            var multis     = new List<List<string>>();

            foreach (var group in original)
            {
                var kept = FilterByUnknownHandling(group, config, logger, conditionId, fieldName);
                if (kept.Count == 0)
                {
                    continue;
                }

                if (kept.Count == 1)
                {
                    singletons.Add(kept);
                }
                else
                {
                    multis.Add(kept);
                }
            }

            var willExpand = false;
            IReadOnlySet<string>? typeMembers = null;

            if (singletons.Count >= 2)
            {
                string? sharedType = null;
                var matched = true;

                foreach (var group in singletons)
                {
                    var selection = _typeSelector.Select(
                        [group[0]],
                        _attachmentCategorization.AttachmentToType,
                        _attachmentCategorization.AttachmentTypes,
                        new Dictionary<string, string>());

                    if (selection.BestType is null)
                    {
                        matched = false;
                        break;
                    }

                    sharedType ??= selection.BestType;
                    if (!string.Equals(sharedType, selection.BestType, StringComparison.Ordinal))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched
                    && sharedType is not null
                    && _attachmentCategorization.AttachmentTypes.TryGetValue(sharedType, out var members))
                {
                    willExpand = true;
                    typeMembers = members;
                }
            }

            foreach (var g in multis)
            {
                output.Add(g);
            }

            if (willExpand && typeMembers is not null)
            {
                AppendFieldExpansion(output, typeMembers);
            }
            else
            {
                foreach (var s in singletons)
                {
                    output.Add(s);
                }
            }

            foreach (var id in ResolveEntries(appendEntries))
            {
                output.Add([id]);
            }
        }

        // Cartesian bundles appended in every mode (including WhitelistOnly:
        // they extend the rebuilt list, they don't get discarded).
        AppendCartesianBundles(output, appendBundles, config, logger, conditionId, fieldName);

        return DedupeGroupsInOrder(output);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves entries to ids: type-name entries expand to all members of that
    /// attachment type; bare ids are returned as-is.
    /// </summary>
    private IEnumerable<string> ResolveEntries(IEnumerable<string> entries)
    {
        foreach (var entry in entries)
        {
            if (_attachmentCategorization.AttachmentTypes.TryGetValue(entry, out var members))
            {
                foreach (var id in members)
                {
                    yield return id;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    /// Resolves a bundle's inner entries to per-position sets:
    /// type-name → member set; bare id → singleton-set.
    /// </summary>
    private List<IReadOnlyList<string>> ResolveBundleSets(IReadOnlyList<string> bundle)
    {
        var sets = new List<IReadOnlyList<string>>(bundle.Count);
        foreach (var entry in bundle)
        {
            if (_attachmentCategorization.AttachmentTypes.TryGetValue(entry, out var members))
            {
                sets.Add(members.ToList());
            }
            else
            {
                sets.Add([entry]);
            }
        }

        return sets;
    }

    /// <summary>
    /// Emits the cartesian product of each bundle's resolved sets as AND-bundles.
    /// Truncates per entry to <see cref="ModConfig.ModBundleCartesianCap"/> and
    /// warns on truncation.
    /// </summary>
    private void AppendCartesianBundles(
        List<List<string>> output,
        IReadOnlyList<List<string>> bundles,
        ModConfig config,
        IModLogger logger,
        string conditionId,
        string fieldName)
    {
        if (bundles.Count == 0)
        {
            return;
        }

        var cap = Math.Max(1, config.ModBundleCartesianCap);

        for (var bIx = 0; bIx < bundles.Count; bIx++)
        {
            var bundle = bundles[bIx];
            if (bundle.Count == 0)
            {
                continue;
            }

            var sets = ResolveBundleSets(bundle);
            if (sets.Any(s => s.Count == 0))
            {
                logger.Warning(
                    $"[mods-expander] condition '{conditionId}' field '{fieldName}': " +
                    $"bundle #{bIx} contains an empty set; bundle dropped.");
                continue;
            }

            long fullProduct = 1;
            foreach (var s in sets)
            {
                fullProduct *= s.Count;
                if (fullProduct > int.MaxValue)
                {
                    fullProduct = int.MaxValue;
                    break;
                }
            }

            var truncated = fullProduct > cap;
            var emit = truncated ? cap : (int)fullProduct;

            var idx = new int[sets.Count];
            for (var k = 0; k < emit; k++)
            {
                var combo = new List<string>(sets.Count);
                for (var p = 0; p < sets.Count; p++)
                {
                    combo.Add(sets[p][idx[p]]);
                }

                output.Add(combo);

                for (var p = sets.Count - 1; p >= 0; p--)
                {
                    idx[p]++;
                    if (idx[p] < sets[p].Count)
                    {
                        break;
                    }

                    idx[p] = 0;
                }
            }

            if (truncated)
            {
                logger.Warning(
                    $"[mods-expander] condition '{conditionId}' field '{fieldName}': " +
                    $"bundle #{bIx} cartesian product {fullProduct} exceeds cap {cap}; " +
                    $"output truncated to {cap} groups.");
            }
        }
    }

    private List<string> FilterByUnknownHandling(
        IReadOnlyList<string> group,
        ModConfig config,
        IModLogger logger,
        string conditionId,
        string fieldName)
    {
        var buckets = GroupExpander.BucketAndLog(
            group,
            _attachmentCategorization,
            config.UnknownWeaponHandling,
            logger,
            conditionId,
            fieldName,
            _nameResolver);

        var categorizedSet       = buckets.Categorized.ToHashSet();
        var uncategorizedInDbSet = buckets.UncategorizedInDb.ToHashSet();
        var notInDbSet           = buckets.NotInDb.ToHashSet();

        var kept = new List<string>(group.Count);
        var seen = new HashSet<string>();

        foreach (var id in group)
        {
            var keep =
                categorizedSet.Contains(id)
                || (buckets.KeepUncategorizedInDb && uncategorizedInDbSet.Contains(id))
                || (buckets.KeepNotInDb && notInDbSet.Contains(id));

            if (keep && seen.Add(id))
            {
                kept.Add(id);
            }
        }

        return kept;
    }

    private void AppendFieldExpansion(List<List<string>> output, IReadOnlySet<string> typeMembers)
    {
        var emittedIds = new List<string>(typeMembers.Count);

        foreach (var member in typeMembers)
        {
            output.Add([member]);
            emittedIds.Add(member);
        }

        foreach (var emitted in emittedIds)
        {
            if (!_attachmentCategorization.CanBeUsedAs.TryGetValue(emitted, out var aliases))
            {
                continue;
            }

            foreach (var alias in aliases)
            {
                if (_attachmentCategorization.AttachmentTypes.TryGetValue(alias, out var members))
                {
                    foreach (var member in members)
                    {
                        output.Add([member]);
                    }
                }
                else
                {
                    output.Add([alias]);
                }
            }
        }
    }

    private static List<List<string>> DedupeGroupsInOrder(List<List<string>> groups)
    {
        var seenKeys = new HashSet<string>();
        var result   = new List<List<string>>(groups.Count);

        foreach (var group in groups)
        {
            var key = string.Join("\0", group.Distinct().OrderBy(x => x, StringComparer.Ordinal));
            if (seenKeys.Add(key))
            {
                result.Add(group);
            }
        }

        return result;
    }
}
