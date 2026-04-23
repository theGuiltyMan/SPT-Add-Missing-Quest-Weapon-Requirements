using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Attachment;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Attachment;

/// <summary>
/// Tests for WeaponModsExpander covering the FIELD-LEVEL CONSENSUS expansion rule.
///
/// Semantics of weaponModsInclusive / weaponModsExclusive (unchanged):
///   - Intra-group = AND: every item in a single group must be carried simultaneously.
///   - Cross-group = OR:  the condition is satisfied iff at least one group matches.
///
/// Field-level expansion rule (the point of this test suite):
///   - Lone singleton (field has exactly one singleton and no multi-item peers that
///     are being expanded) → emit verbatim. No type expansion.
///   - ≥2 singletons AND all singleton ids share a common covering attachment type T
///     → emit one singleton group per member of T (+ canBeUsedAs aliases per member).
///   - ≥2 singletons with no common covering type → emit singletons verbatim.
///   - Multi-item groups always emitted verbatim; never counted toward singleton consensus.
///   - Emission order: multi-item groups first, then (field-level expansion batch OR
///     verbatim singletons), then IncludedMods.
///
/// Unchanged from prior revision: WhitelistOnly / NoExpansion behaviour, IncludedMods
/// appending (bare-id + type-name), ExcludedMods bare-id vs type-name split, dedup,
/// exclusive-field symmetry, unknown-handling filter, alias application on the
/// expansion path only.
/// </summary>
public class WeaponModsExpanderTests
{
    // ── Shared fixture ─────────────────────────────────────────────────────────

    /// <summary>
    /// Attachment catalogue used by most tests:
    ///   Stock      → stock_a, stock_b, stock_c  (3 members)
    ///   Scope      → scope_a, scope_b           (2 members)
    ///   Suppressor → supp_a, supp_b             (2 members)
    /// </summary>
    private static AttachmentCategorizationResult MakeCat(
        Dictionary<string, IReadOnlySet<string>>? attachmentTypes = null,
        Dictionary<string, IReadOnlySet<string>>? attachmentToType = null,
        Dictionary<string, IReadOnlySet<string>>? canBeUsedAs = null,
        IReadOnlySet<string>? knownItemIds = null)
    {
        var types = attachmentTypes ?? new Dictionary<string, IReadOnlySet<string>>
        {
            ["Stock"]      = new HashSet<string> { "stock_a", "stock_b", "stock_c" },
            ["Scope"]      = new HashSet<string> { "scope_a", "scope_b" },
            ["Suppressor"] = new HashSet<string> { "supp_a", "supp_b" },
        };
        var toType = attachmentToType ?? new Dictionary<string, IReadOnlySet<string>>
        {
            ["stock_a"] = new HashSet<string> { "Stock" },
            ["stock_b"] = new HashSet<string> { "Stock" },
            ["stock_c"] = new HashSet<string> { "Stock" },
            ["scope_a"] = new HashSet<string> { "Scope" },
            ["scope_b"] = new HashSet<string> { "Scope" },
            ["supp_a"]  = new HashSet<string> { "Suppressor" },
            ["supp_b"]  = new HashSet<string> { "Suppressor" },
        };

        // Default known-item set = every categorized attachment ID.
        var known = knownItemIds ?? (IReadOnlySet<string>)toType.Keys.ToHashSet();

        return new AttachmentCategorizationResult
        {
            AttachmentTypes  = types,
            AttachmentToType = toType,
            CanBeUsedAs      = canBeUsedAs ?? new Dictionary<string, IReadOnlySet<string>>(),
            KnownItemIds     = known,
        };
    }

    private static WeaponModsExpander MakeExpander(AttachmentCategorizationResult? cat = null)
    {
        return new WeaponModsExpander(cat ?? MakeCat(), NullNameResolver.Instance);
    }

    private static ConditionNode MakeCondition(
        string id = "c1",
        List<List<string>>? inclusive = null,
        List<List<string>>? exclusive = null,
        List<string>? weapons = null)
    {
        return new ConditionNode
        {
            Id                  = id,
            ConditionType       = "CounterCreator",
            Weapon              = weapons ?? [],
            WeaponModsInclusive = inclusive ?? [],
            WeaponModsExclusive = exclusive ?? [],
        };
    }

    private static void RunExpand(
        WeaponModsExpander expander,
        ConditionNode condition,
        IModLogger? logger = null,
        ModConfig? config = null)
    {
        expander.Expand(
            condition,
            overrideEntry: null,
            categorization: new CategorizationResult
            {
                WeaponTypes     = new Dictionary<string, IReadOnlySet<string>>(),
                WeaponToType    = new Dictionary<string, IReadOnlySet<string>>(),
                CanBeUsedAs     = new Dictionary<string, IReadOnlySet<string>>(),
                WeaponToCaliber = new Dictionary<string, string>(),
                KnownItemIds    = new HashSet<string>(),
            },
            config: config ?? new ModConfig(),
            logger: logger ?? NullModLogger.Instance);
    }

    private static void RunWithOverride(
        WeaponModsExpander expander,
        ConditionNode condition,
        QuestOverrideEntry overrideEntry,
        IModLogger? logger = null,
        ModConfig? config = null)
    {
        expander.Expand(
            condition,
            overrideEntry: overrideEntry,
            categorization: new CategorizationResult
            {
                WeaponTypes     = new Dictionary<string, IReadOnlySet<string>>(),
                WeaponToType    = new Dictionary<string, IReadOnlySet<string>>(),
                CanBeUsedAs     = new Dictionary<string, IReadOnlySet<string>>(),
                WeaponToCaliber = new Dictionary<string, string>(),
                KnownItemIds    = new HashSet<string>(),
            },
            config: config ?? new ModConfig(),
            logger: logger ?? NullModLogger.Instance);
    }

    /// <summary>
    /// Order-preserving group assertion with sort-within-group to ignore HashSet
    /// iteration order inside a single group. Asserts on the outer order because
    /// first-occurrence de-duplication and emission order are part of the contract.
    /// </summary>
    private static void AssertGroups(List<List<string>> actual, params string[][] expected)
    {
        var actualSorted = actual.Select(g => g.OrderBy(x => x).ToList()).ToList();
        var expectedSorted = expected.Select(g => g.OrderBy(x => x).ToList()).ToList();
        actualSorted.Should().BeEquivalentTo(expectedSorted, o => o.WithStrictOrdering());
    }

    /// <summary>
    /// Unordered group assertion — used in tests where the relative order of the
    /// emitted type members / aliases isn't contractually fixed (type expansion
    /// iterates a HashSet of members).
    /// </summary>
    private static void AssertGroupsUnordered(List<List<string>> actual, params string[][] expected)
    {
        var actualSorted = actual.Select(g => g.OrderBy(x => x).ToList()).ToList();
        var expectedSorted = expected.Select(g => g.OrderBy(x => x).ToList()).ToList();
        actualSorted.Should().BeEquivalentTo(expectedSorted);
    }

    // ── Row 1: Lone singleton under Auto stays verbatim (NEW SEMANTICS) ───────

    [Fact]
    public void lone_singleton_under_auto_stays_verbatim_no_type_expansion()
    {
        // Only one singleton group present → field-level consensus requires ≥2
        // singletons to expand. Must remain exactly as authored.
        var condition = MakeCondition(inclusive: [["scope_a"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsInclusive, ["scope_a"]);
    }

    [Fact]
    public void lone_singleton_of_large_type_under_auto_stays_verbatim()
    {
        // Even when the attachment type (Stock) has many members, a lone singleton
        // is NOT expanded — the authored "just stock_a" intent is preserved.
        var condition = MakeCondition(inclusive: [["stock_a"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsInclusive, ["stock_a"]);
    }

    // ── Row 2: ≥2 singletons, common covering type → expand to type members ───

    [Fact]
    public void two_singletons_same_type_same_members_emit_type_membership_verbatim()
    {
        // [["scope_a"], ["scope_b"]] with Scope = {scope_a, scope_b} → the type
        // covers exactly these two ids, so the emitted output is the same two
        // singletons (no additional members to add).
        var condition = MakeCondition(inclusive: [["scope_a"], ["scope_b"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["scope_a"],
            ["scope_b"]);
    }

    [Fact]
    public void two_singletons_same_type_with_extra_members_expands_to_all_members()
    {
        // Two singletons referencing Stock members; Stock has a third member. The
        // shared covering type triggers field-level expansion → emit one singleton
        // per type member (3 groups).
        var condition = MakeCondition(inclusive: [["stock_a"], ["stock_b"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["stock_a"],
            ["stock_b"],
            ["stock_c"]);
    }

    [Fact]
    public void three_singletons_same_type_expand_to_type_members()
    {
        var condition = MakeCondition(inclusive: [["stock_a"], ["stock_b"], ["stock_c"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["stock_a"],
            ["stock_b"],
            ["stock_c"]);
    }

    // ── Row 3: ≥2 singletons, no common covering type → verbatim ──────────────

    [Fact]
    public void two_singletons_mixed_types_stay_verbatim_no_expansion()
    {
        // [["scope_a"], ["supp_a"]] — no single attachment type covers both ids.
        // Field-level rule: emit verbatim, never broaden the mixed list.
        // This is the main safety case — prevents scope+suppressor quests from
        // exploding into ~100 brake ids.
        var condition = MakeCondition(inclusive: [["scope_a"], ["supp_a"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsInclusive,
            ["scope_a"],
            ["supp_a"]);
    }

    [Fact]
    public void many_singletons_mostly_one_type_with_one_outlier_stay_verbatim()
    {
        // Simulates the Tarkov Shooter Pt7 contamination shape: many Scope ids
        // with one Suppressor outlier. No single type covers all → verbatim.
        var condition = MakeCondition(inclusive:
        [
            ["scope_a"],
            ["scope_b"],
            ["supp_a"], // outlier, different type
        ]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsInclusive,
            ["scope_a"],
            ["scope_b"],
            ["supp_a"]);
    }

    // ── Row 4: ≥2 singletons where unknown-strip reduces to 1 → lone-rule ────

    [Fact]
    public void two_singletons_reduced_to_one_by_strip_then_lone_rule_applies()
    {
        // One known + one unknown, unknown stripped by KeepInDb → 1 singleton
        // remaining → lone-singleton rule → verbatim, no expansion.
        var logger    = new CapturingModLogger();
        var condition = MakeCondition(inclusive: [["stock_a"], ["totally_unknown"]]);
        var config    = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };

        RunExpand(MakeExpander(), condition, logger, config);

        // After strip: only [stock_a] remains; as a lone singleton it stays as-is.
        AssertGroups(condition.WeaponModsInclusive, ["stock_a"]);
        logger.Warnings.Should().ContainMatch("*totally_unknown*");
    }

    // ── Row 5: Multi-item groups are always verbatim, never counted ──────────

    [Fact]
    public void multi_item_group_alone_under_auto_stays_verbatim()
    {
        var condition = MakeCondition(inclusive: [["scope_a", "supp_a"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsInclusive, ["scope_a", "supp_a"]);
    }

    [Fact]
    public void multi_item_group_does_not_apply_aliases()
    {
        // Aliases on members of a multi-item AND-bundle must NOT be applied —
        // doing so would broaden the bundle and change its meaning.
        var cat = MakeCat(
            canBeUsedAs: new Dictionary<string, IReadOnlySet<string>>
            {
                ["scope_a"] = new HashSet<string> { "scope_alias" },
            });

        var condition = MakeCondition(inclusive: [["scope_a", "supp_a"]]);
        RunExpand(MakeExpander(cat), condition);

        AssertGroups(condition.WeaponModsInclusive, ["scope_a", "supp_a"]);
    }

    [Fact]
    public void multi_item_plus_single_singleton_multi_verbatim_and_singleton_verbatim()
    {
        // [[scope_a, supp_a], [scope_b]] → multi-item verbatim; only 1 singleton
        // post-partition → lone-singleton rule, emitted verbatim.
        // Emission order: multi-item first, then singletons.
        var condition = MakeCondition(inclusive:
        [
            ["scope_a", "supp_a"],
            ["scope_b"],
        ]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsInclusive,
            ["scope_a", "supp_a"],
            ["scope_b"]);
    }

    [Fact]
    public void multi_item_plus_two_same_type_singletons_multi_verbatim_plus_expansion()
    {
        // [[scope_a, supp_a], [stock_a], [stock_b]] → multi-item verbatim,
        // then two singletons share Stock covering type → expand to 3 members.
        // Asserts emission order: multi-item BEFORE the type expansion batch.
        var condition = MakeCondition(inclusive:
        [
            ["scope_a", "supp_a"],
            ["stock_a"],
            ["stock_b"],
        ]);
        RunExpand(MakeExpander(), condition);

        // Multi-item first (position 0), then type expansion (3 Stock singletons).
        condition.WeaponModsInclusive.Should().HaveCount(4);
        condition.WeaponModsInclusive[0].OrderBy(x => x).Should()
            .BeEquivalentTo(new[] { "scope_a", "supp_a" }, o => o.WithStrictOrdering());

        var tail = condition.WeaponModsInclusive.Skip(1).ToList();
        AssertGroupsUnordered(tail,
            ["stock_a"],
            ["stock_b"],
            ["stock_c"]);
    }

    [Fact]
    public void multiple_multi_item_groups_preserve_relative_order_under_auto()
    {
        // All multi-item → none rewritten, ordering preserved exactly.
        var condition = MakeCondition(inclusive:
        [
            ["scope_a", "supp_a"],
            ["stock_a", "stock_b"],
            ["scope_b", "supp_b"],
        ]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsInclusive,
            ["scope_a", "supp_a"],
            ["stock_a", "stock_b"],
            ["scope_b", "supp_b"]);
    }

    // ── NoExpansion: every group verbatim (lone, multi, or ≥2 singletons) ────

    [Fact]
    public void no_expansion_singleton_stays_unchanged()
    {
        var condition = MakeCondition(inclusive: [["scope_a"]]);
        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            ModsExpansionMode = ExpansionMode.NoExpansion,
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroups(condition.WeaponModsInclusive, ["scope_a"]);
    }

    [Fact]
    public void no_expansion_two_same_type_singletons_stay_verbatim_no_consensus()
    {
        // Under NoExpansion, even ≥2 same-type singletons must NOT be expanded
        // to the full type. Verbatim.
        var condition = MakeCondition(inclusive: [["stock_a"], ["stock_b"]]);
        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            ModsExpansionMode = ExpansionMode.NoExpansion,
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroups(condition.WeaponModsInclusive,
            ["stock_a"],
            ["stock_b"]);
    }

    [Fact]
    public void no_expansion_multi_item_group_stays_unchanged()
    {
        var condition = MakeCondition(inclusive: [["scope_a", "supp_a"]]);
        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            ModsExpansionMode = ExpansionMode.NoExpansion,
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroups(condition.WeaponModsInclusive, ["scope_a", "supp_a"]);
    }

    // ── WhitelistOnly: discard originals, rebuild from IncludedMods ──────────

    [Fact]
    public void whitelist_only_replaces_original_groups_with_included_mods_as_singletons()
    {
        // WhitelistOnly + IncludedMods ["stock_a", "Stock"] → output is
        // [[stock_a], [stock_b], [stock_c]] after dedup. Type name "Stock"
        // expands; bare "stock_a" is already covered → dedup drops duplicate.
        var condition = MakeCondition(inclusive: [["scope_a", "supp_a"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            ModsExpansionMode = ExpansionMode.WhitelistOnly,
            IncludedMods = ["stock_a", "Stock"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["stock_a"],
            ["stock_b"],
            ["stock_c"]);
    }

    [Fact]
    public void whitelist_only_with_bare_ids_only_produces_one_singleton_each()
    {
        var condition = MakeCondition(inclusive: [["scope_a", "supp_a"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            ModsExpansionMode = ExpansionMode.WhitelistOnly,
            IncludedMods = ["scope_a"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroups(condition.WeaponModsInclusive, ["scope_a"]);
    }

    // ── NoExpansion still appends IncludedMods as new singleton groups ───────

    [Fact]
    public void no_expansion_singleton_stays_and_included_mods_appended_as_singletons()
    {
        // NoExpansion singleton [["scope_a"]] stays [["scope_a"]]; IncludedMods
        // ["supp_a"] appended as a NEW singleton group.
        var condition = MakeCondition(inclusive: [["scope_a"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            ModsExpansionMode = ExpansionMode.NoExpansion,
            IncludedMods = ["supp_a"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroups(condition.WeaponModsInclusive,
            ["scope_a"],
            ["supp_a"]);
    }

    // ── IncludedMods appended as singleton groups; type-name entries expand ─

    [Fact]
    public void included_mods_type_name_entry_expands_to_singleton_per_member_under_auto()
    {
        // Auto + multi-item original (verbatim) + IncludedMods with type name
        // → additional singleton groups, one per type member. Emission order:
        // originals first, IncludedMods tail.
        var condition = MakeCondition(inclusive: [["scope_a", "supp_a"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            IncludedMods = ["Stock"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        condition.WeaponModsInclusive.Should().HaveCount(4);
        condition.WeaponModsInclusive[0].OrderBy(x => x).Should()
            .BeEquivalentTo(new[] { "scope_a", "supp_a" }, o => o.WithStrictOrdering());

        var tail = condition.WeaponModsInclusive.Skip(1).ToList();
        AssertGroupsUnordered(tail,
            ["stock_a"],
            ["stock_b"],
            ["stock_c"]);
    }

    [Fact]
    public void included_mods_bare_id_appends_single_new_singleton_group()
    {
        var condition = MakeCondition(inclusive: [["scope_a", "supp_a"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            IncludedMods = ["stock_a"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroups(condition.WeaponModsInclusive,
            ["scope_a", "supp_a"],
            ["stock_a"]);
    }

    // ── ExcludedMods drops any output group matching an exclude rule ────────

    [Fact]
    public void excluded_mods_drops_multi_item_group_when_any_member_excluded()
    {
        // Multi-item [["scope_a", "supp_a"]] + ExcludedMods ["scope_a"] → [].
        var condition = MakeCondition(inclusive: [["scope_a", "supp_a"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            ExcludedMods = ["scope_a"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroups(condition.WeaponModsInclusive /* empty */);
    }

    [Fact]
    public void excluded_mods_type_name_drops_every_group_whose_members_all_in_type()
    {
        // Groups: [stock_a] (all Stock → dropped), [scope_a] (not Stock → kept),
        //         [stock_b, supp_a] (not all Stock → kept).
        var condition = MakeCondition(inclusive: [["stock_a"], ["scope_a"], ["stock_b", "supp_a"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            // NoExpansion so the singletons stay verbatim, avoiding entangling
            // ExcludedMods behaviour with the field-level consensus rule.
            ModsExpansionMode = ExpansionMode.NoExpansion,
            ExcludedMods = ["Stock"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroups(condition.WeaponModsInclusive,
            ["scope_a"],
            ["stock_b", "supp_a"]);
    }

    [Fact]
    public void excluded_mods_drops_expanded_singletons_that_land_on_excluded_id()
    {
        // Field-level expansion: [[stock_a], [stock_b]] share Stock (3 members)
        // → expands to [stock_a], [stock_b], [stock_c]. ExcludedMods ["stock_b"]
        // drops the [stock_b] group from the expanded output.
        var condition = MakeCondition(inclusive: [["stock_a"], ["stock_b"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            ExcludedMods = ["stock_b"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["stock_a"],
            ["stock_c"]);
    }

    // ── Aliases are applied on the expansion path, one per emitted type member ─

    [Fact]
    public void field_level_expansion_applies_aliases_per_emitted_type_member()
    {
        // Two singletons share Stock (3 members); stock_a has an alias "stock_alias".
        // Expansion emits [stock_a], [stock_b], [stock_c] plus the alias singleton
        // for stock_a. Aliases for other members (none configured) contribute nothing.
        var cat = MakeCat(
            canBeUsedAs: new Dictionary<string, IReadOnlySet<string>>
            {
                ["stock_a"] = new HashSet<string> { "stock_alias" },
            });

        var condition = MakeCondition(inclusive: [["stock_a"], ["stock_b"]]);
        RunExpand(MakeExpander(cat), condition);

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["stock_a"],
            ["stock_b"],
            ["stock_c"],
            ["stock_alias"]);
    }

    [Fact]
    public void field_level_expansion_alias_type_name_expands_to_members()
    {
        // Two singletons share Scope; scope_a's alias is the TYPE NAME "Suppressor",
        // which on the expansion path expands to all Suppressor members as
        // additional singleton groups.
        var cat = MakeCat(
            canBeUsedAs: new Dictionary<string, IReadOnlySet<string>>
            {
                ["scope_a"] = new HashSet<string> { "Suppressor" },
            });

        var condition = MakeCondition(inclusive: [["scope_a"], ["scope_b"]]);
        RunExpand(MakeExpander(cat), condition);

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["scope_a"],
            ["scope_b"],
            ["supp_a"],
            ["supp_b"]);
    }

    [Fact]
    public void lone_singleton_verbatim_does_not_emit_aliases()
    {
        // Lone singleton → verbatim → no expansion → no alias emission.
        // Aliases are only applied on the field-level expansion path.
        var cat = MakeCat(
            canBeUsedAs: new Dictionary<string, IReadOnlySet<string>>
            {
                ["scope_a"] = new HashSet<string> { "scope_alias" },
            });

        var condition = MakeCondition(inclusive: [["scope_a"]]);
        RunExpand(MakeExpander(cat), condition);

        AssertGroups(condition.WeaponModsInclusive, ["scope_a"]);
    }

    // ── Unknown-id handling: propagates through filter before partitioning ─

    [Fact]
    public void singleton_unknown_id_under_keep_all_preserves_group()
    {
        var logger    = new CapturingModLogger();
        var condition = MakeCondition(inclusive: [["totally_unknown"]]);
        RunExpand(MakeExpander(), condition, logger); // default KeepAll

        // Lone singleton + unknown kept → verbatim.
        AssertGroups(condition.WeaponModsInclusive, ["totally_unknown"]);
    }

    [Fact]
    public void singleton_unknown_id_under_keep_in_db_is_stripped_and_group_dropped()
    {
        var logger    = new CapturingModLogger();
        var condition = MakeCondition(inclusive: [["totally_unknown"]]);
        var config    = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };
        RunExpand(MakeExpander(), condition, logger, config);

        // totally_unknown is stripped → group becomes empty → dropped from output.
        AssertGroups(condition.WeaponModsInclusive /* empty */);
        logger.Warnings.Should().ContainMatch("*totally_unknown*");
    }

    [Fact]
    public void singleton_uncategorized_in_db_under_strip_drops_group()
    {
        var known = new HashSet<string> { "stock_a", "stock_b", "stock_c", "scope_a", "scope_b",
                                          "supp_a", "supp_b", "stock_d" };
        var cat   = MakeCat(knownItemIds: known);

        var logger    = new CapturingModLogger();
        var condition = MakeCondition(inclusive: [["stock_d"]]);
        var config    = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.Strip };

        RunExpand(MakeExpander(cat), condition, logger, config);

        // stock_d is in DB but uncategorized; Strip removes it → group drops.
        AssertGroups(condition.WeaponModsInclusive /* empty */);
        logger.Warnings.Should().ContainMatch("*stock_d*");
    }

    [Fact]
    public void multi_item_group_with_unknown_under_keep_all_preserves_group_intact()
    {
        var logger    = new CapturingModLogger();
        var condition = MakeCondition(inclusive: [["stock_a", "totally_unknown"]]);
        RunExpand(MakeExpander(), condition, logger); // default KeepAll

        // Multi-item under Auto is NOT expanded; KeepAll preserves unknown id.
        AssertGroups(condition.WeaponModsInclusive, ["stock_a", "totally_unknown"]);
    }

    [Fact]
    public void multi_item_group_with_unknown_under_keep_in_db_strips_unknown_but_keeps_rest()
    {
        var logger    = new CapturingModLogger();
        var condition = MakeCondition(inclusive: [["stock_a", "totally_unknown"]]);
        var config    = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };
        RunExpand(MakeExpander(), condition, logger, config);

        // Group is NOT an AND bundle of just unknowns — valid ids remain, unknowns dropped.
        AssertGroups(condition.WeaponModsInclusive, ["stock_a"]);
        logger.Warnings.Should().ContainMatch("*totally_unknown*");
    }

    [Fact]
    public void multi_item_group_with_all_unknown_under_keep_in_db_is_dropped()
    {
        var logger    = new CapturingModLogger();
        var condition = MakeCondition(inclusive: [["unknown_x", "unknown_y"]]);
        var config    = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };
        RunExpand(MakeExpander(), condition, logger, config);

        // Empty-after-strip groups are dropped from output.
        AssertGroups(condition.WeaponModsInclusive /* empty */);
    }

    // ── Exclusive field processed symmetrically ───────────────────────────────

    [Fact]
    public void exclusive_two_singletons_same_type_expand_to_type_members()
    {
        // Exclusive field must follow the same field-level consensus rule.
        var condition = MakeCondition(exclusive: [["stock_a"], ["stock_b"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroupsUnordered(condition.WeaponModsExclusive,
            ["stock_a"],
            ["stock_b"],
            ["stock_c"]);
    }

    [Fact]
    public void exclusive_lone_singleton_stays_verbatim()
    {
        var condition = MakeCondition(exclusive: [["scope_a"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsExclusive, ["scope_a"]);
    }

    [Fact]
    public void exclusive_multi_item_under_auto_stays_unchanged()
    {
        var condition = MakeCondition(exclusive: [["scope_a", "supp_a"]]);
        RunExpand(MakeExpander(), condition);

        AssertGroups(condition.WeaponModsExclusive, ["scope_a", "supp_a"]);
    }

    [Fact]
    public void inclusive_and_exclusive_processed_independently_with_new_semantics()
    {
        // Inclusive: ≥2 same-type singletons → expand. Exclusive: lone singleton → verbatim.
        var condition = MakeCondition(
            inclusive: [["scope_a"], ["scope_b"]],
            exclusive: [["supp_a"]]);

        RunExpand(MakeExpander(), condition);

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["scope_a"],
            ["scope_b"]);
        AssertGroups(condition.WeaponModsExclusive, ["supp_a"]);
    }

    // ── Dedup across expansion batch and IncludedMods ────────────────────────

    [Fact]
    public void dedup_uses_first_occurrence_order_across_expansion_and_included_mods()
    {
        // Two singletons share Stock → expand to 3 Stock singletons. IncludedMods
        // ["stock_b"] is already covered → dedup drops it; ["supp_a"] is new → added.
        var condition = MakeCondition(inclusive: [["stock_a"], ["stock_b"]]);

        var overrideEntry = new QuestOverrideEntry
        {
            Id = "q1",
            IncludedMods = ["stock_b", "supp_a"],
        };

        RunWithOverride(MakeExpander(), condition, overrideEntry);

        // 3 Stock + 1 supp_a = 4 groups total; supp_a must come last.
        condition.WeaponModsInclusive.Should().HaveCount(4);
        condition.WeaponModsInclusive[^1].Should().BeEquivalentTo(new[] { "supp_a" });

        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["stock_a"],
            ["stock_b"],
            ["stock_c"],
            ["supp_a"]);
    }

    // ── Misc: weapon array is not touched ─────────────────────────────────────

    [Fact]
    public void expander_does_not_touch_weapon_array()
    {
        var condition = MakeCondition(
            weapons:   ["weapon_id_1", "weapon_id_2"],
            inclusive: [["scope_a"]]);

        RunExpand(MakeExpander(), condition);

        condition.Weapon.Should().BeEquivalentTo(["weapon_id_1", "weapon_id_2"]);
        // Lone singleton → verbatim.
        AssertGroups(condition.WeaponModsInclusive, ["scope_a"]);
    }

    // ── Mods-only condition: no weapon array present ──────────────────────────

    [Fact]
    public void mods_only_condition_with_no_weapon_array_is_handled()
    {
        var condition = MakeCondition(
            weapons: [],
            inclusive: [["scope_a"], ["scope_b"]]);

        var act = () => RunExpand(MakeExpander(), condition);

        act.Should().NotThrow();
        // Two scopes → share Scope type (2 members, full coverage) → verbatim emission.
        AssertGroupsUnordered(condition.WeaponModsInclusive,
            ["scope_a"],
            ["scope_b"]);
    }

    // ── IncludedMods / ExcludedMods only target weaponModsInclusive ───────────

    [Fact]
    public void IncludedMods_only_appends_to_inclusive_never_to_exclusive()
    {
        // User authors `includedMods: [scope_a]` to broaden what satisfies the
        // quest. Historically the same list got appended to weaponModsExclusive
        // too, silently inverting the author's intent (weapon would be rejected
        // if it carried scope_a). Inclusive must be appended; exclusive must
        // stay untouched.
        var condition = MakeCondition(
            inclusive: [],
            exclusive: []);

        RunWithOverride(MakeExpander(), condition, new QuestOverrideEntry
        {
            Id                = "c1",
            ModsExpansionMode = ExpansionMode.Auto,
            IncludedMods      = ["scope_a"]
        });

        AssertGroupsUnordered(condition.WeaponModsInclusive, ["scope_a"]);
        condition.WeaponModsExclusive.Should().BeEmpty(
            "includedMods must not flow into weaponModsExclusive — that would invert the author's intent");
    }

    [Fact]
    public void ExcludedMods_only_filters_inclusive_never_exclusive()
    {
        // ExcludedMods on the override entry removes groups from the inclusive
        // field. The exclusive field must pass through untouched regardless of
        // the same ID appearing in its groups.
        var condition = MakeCondition(
            inclusive: [["scope_a"], ["scope_b"]],
            exclusive: [["scope_a"]]);

        RunWithOverride(MakeExpander(), condition, new QuestOverrideEntry
        {
            Id                = "c1",
            ModsExpansionMode = ExpansionMode.NoExpansion,
            ExcludedMods      = ["scope_a"]
        });

        AssertGroupsUnordered(condition.WeaponModsInclusive, ["scope_b"]);
        AssertGroupsUnordered(condition.WeaponModsExclusive, ["scope_a"]);
    }
}
