using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Shared;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Attachment;

/// <summary>
/// Rewrites <c>weaponModsInclusive</c> and <c>weaponModsExclusive</c> under the
/// confirmed intra-group AND, cross-group OR semantics.
///
/// <para>Singleton groups under <see cref="ExpansionMode.Auto"/> are expanded
/// OUTWARDS into N new singleton groups (one per attachment-type member, plus
/// aliases), broadening the cross-group OR. Multi-item groups (AND bundles,
/// e.g. <c>Test Drive</c>) are kept verbatim so the conjunction is preserved.</para>
///
/// <para>Override modes operate at the group level — <see cref="ExpansionMode.WhitelistOnly"/>
/// discards the original field and rebuilds it from <c>includedMods</c>;
/// <see cref="ExpansionMode.NoExpansion"/> keeps every original group verbatim
/// and still appends <c>includedMods</c> as new singleton groups. <c>excludedMods</c>
/// drops any output group containing an excluded id.</para>
/// </summary>
public sealed class WeaponModsExpander : IConditionExpander
{
    private readonly AttachmentCategorizationResult _attachmentCategorization;
    private readonly TypeSelector _typeSelector;
    private readonly INameResolver _nameResolver;

    public WeaponModsExpander(AttachmentCategorizationResult attachmentCategorization, INameResolver nameResolver)
    {
        _attachmentCategorization = attachmentCategorization;
        _typeSelector = new TypeSelector();
        _nameResolver = nameResolver;
    }

    public void Expand(
        ConditionNode condition,
        QuestOverrideEntry? overrideEntry,
        CategorizationResult categorization,
        ModConfig config,
        IModLogger logger)
    {
        if (condition.WeaponModsInclusive.Count == 0 && condition.WeaponModsExclusive.Count == 0
            && (overrideEntry is null
                || (overrideEntry.IncludedMods.Count == 0
                    && overrideEntry.ExcludedMods.Count == 0
                    && overrideEntry.ModsExpansionMode == ExpansionMode.Auto)))
        {
            return;
        }

        var newInclusive = RewriteField(
            condition.WeaponModsInclusive,
            condition.Id,
            "weaponModsInclusive",
            overrideEntry,
            config,
            logger);

        // User-authored includedMods / excludedMods / ModsExpansionMode apply
        // only to weaponModsInclusive. Letting them affect weaponModsExclusive
        // inverts the author's intent: "broaden what satisfies this quest"
        // would also mean "reject weapons that carry these mods". The exclusive
        // field still runs through the expander (for baseline auto-expansion
        // of singleton groups that share a type) but with no override.
        var newExclusive = RewriteField(
            condition.WeaponModsExclusive,
            condition.Id,
            "weaponModsExclusive",
            overrideEntry: null,
            config,
            logger);

        // The fields are init-only properties with a List<List<string>> backing. Mutate in
        // place to avoid touching the ConditionNode type.
        condition.WeaponModsInclusive.Clear();
        condition.WeaponModsInclusive.AddRange(newInclusive);

        condition.WeaponModsExclusive.Clear();
        condition.WeaponModsExclusive.AddRange(newExclusive);
    }

    // ── Core rewrite ─────────────────────────────────────────────────────────

    private List<List<string>> RewriteField(
        List<List<string>> original,
        string conditionId,
        string fieldName,
        QuestOverrideEntry? overrideEntry,
        ModConfig config,
        IModLogger logger)
    {
        var mode     = overrideEntry?.ModsExpansionMode ?? ExpansionMode.Auto;
        var includes = overrideEntry?.IncludedMods ?? [];
        var excludes = overrideEntry?.ExcludedMods ?? [];

        var output = new List<List<string>>();

        if (mode == ExpansionMode.WhitelistOnly)
        {
            // Discard original groups entirely; rebuild from IncludedMods alone.
            foreach (var id in ResolveEntries(includes))
            {
                output.Add([id]);
            }
        }
        else if (mode == ExpansionMode.NoExpansion)
        {
            // Preserve input order verbatim (after unknown-handling). No reordering,
            // no type consensus. IncludedMods still appended.
            foreach (var group in original)
            {
                var kept = FilterByUnknownHandling(group, config, logger, conditionId, fieldName);
                if (kept.Count == 0)
                {
                    continue;
                }

                output.Add(kept);
            }

            foreach (var id in ResolveEntries(includes))
            {
                output.Add([id]);
            }
        }
        else
        {
            // Auto mode: partition groups (post unknown-handling filter) into singletons
            // and multis. Singleton partition is keyed on post-filter size: a multi-item
            // AND bundle stripped to one member via unknown-handling no longer represents
            // an AND, so the survivor is treated as an atom for consensus purposes.
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

            // Field-level type-consensus decision. We use the STRICT rule: every singleton
            // must resolve to the same minimal covering type. A shared broader ancestor
            // (e.g. Muzzle over Silencer+MuzzleBrake) does NOT count — that catch-all
            // would produce over-broad expansions for contaminated quest data (silencer
            // quest with a stray brake singleton should stay verbatim, not balloon to all
            // Muzzle devices).
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

            // Emission order: multis verbatim → expansion batch OR singletons verbatim → includes.
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

            foreach (var id in ResolveEntries(includes))
            {
                output.Add([id]);
            }
        }

        // ExcludedMods applied last. Two drop rules:
        //   - Bare-id entry   → drop any group that contains that id.
        //   - Type-name entry → drop any group whose members are ALL inside the type.
        // Rationale: a bare id names a specific attachment the authored bundle must not
        // rely on; a type name names a category and only fully-excluded bundles are
        // dropped so mixed-category AND bundles (e.g. [stock_b, supp_a] vs exclude Stock)
        // are preserved.
        if (excludes.Count > 0)
        {
            var bareIdExcludes = new HashSet<string>();
            var typeExcludes = new List<IReadOnlySet<string>>();

            foreach (var entry in excludes)
            {
                if (_attachmentCategorization.AttachmentTypes.TryGetValue(entry, out var members))
                {
                    typeExcludes.Add(members);
                }
                else
                {
                    bareIdExcludes.Add(entry);
                }
            }

            output.RemoveAll(group =>
            {
                if (bareIdExcludes.Count > 0 && group.Any(id => bareIdExcludes.Contains(id)))
                {
                    return true;
                }

                foreach (var typeMembers in typeExcludes)
                {
                    if (group.All(id => typeMembers.Contains(id)))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

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
    /// Applies unknown-id handling via <see cref="GroupExpander.BucketAndLog"/> and
    /// returns the kept ids preserving input order (categorized ∪ uncategorizedInDb
    /// when kept ∪ notInDb when kept).
    /// </summary>
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

    /// <summary>
    /// Field-level expansion batch: given the chosen type's <paramref name="typeMembers"/>,
    /// emit one singleton group per member, and for each emitted id emit one singleton per
    /// <c>canBeUsedAs</c> alias (type-name aliases expand to their members).
    /// Only called when singleton count ≥ 2 AND a common covering type was found —
    /// lone singletons never reach this path and therefore never emit aliases.
    /// </summary>
    private void AppendFieldExpansion(List<List<string>> output, IReadOnlySet<string> typeMembers)
    {
        var emittedIds = new List<string>(typeMembers.Count);

        foreach (var member in typeMembers)
        {
            output.Add([member]);
            emittedIds.Add(member);
        }

        // Aliases: each alias value may be a bare id OR a type name. Type names expand
        // to their members (each as its own singleton group); bare ids emit one group.
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

    /// <summary>
    /// Drops duplicate groups from <paramref name="groups"/> based on each group's
    /// sorted-set representation. First occurrence wins; ordering otherwise preserved.
    /// </summary>
    private static List<List<string>> DedupeGroupsInOrder(List<List<string>> groups)
    {
        var seenKeys = new HashSet<string>();
        var result = new List<List<string>>(groups.Count);

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
