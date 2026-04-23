using System.Diagnostics;
using AddMissingQuestRequirements.Inspector;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Attachment;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Shared;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Integration;

/// <summary>
/// Smoke tests that run the full pipeline against real SPT game data.
/// Opt in with <c>dotnet test --filter "Category=Integration"</c>.
/// <para>
/// Each test short-circuits when <see cref="SliceLoader.IsAvailable"/> is false
/// — i.e. no env vars set and no in-repo <c>SptDbExporter/export/</c> slice —
/// so a clean clone can run the full suite without bringing up SPT.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RealDataSmokeTests
{
    // Item database is read-only after loading — safe to share across all tests.
    // Quest database is mutated in-place by QuestPatcher, so tests that patch must
    // load their own fresh copy via SliceLoader.LoadQuestDatabase().
    private static readonly Lazy<InMemoryItemDatabase?> _itemDb =
        new(() => SliceLoader.IsAvailable ? SliceLoader.LoadItemDatabase() : null);

    private static bool TryGetDb(out InMemoryItemDatabase db)
    {
        db = _itemDb.Value!;
        return db is not null;
    }

    private static IReadOnlyList<TypeRule> DefaultWeaponRulesFor(ModConfig config, IItemDatabase db)
        => DefaultWeaponRuleFactory.Build(db, config.WeaponLikeAncestors);

    // Shared default attachment rules live in the core project.
    private static TypeRule[] DefaultAttachmentRules =>
        AddMissingQuestRequirements.Pipeline.Attachment.DefaultAttachmentRules.Rules;

    // ── Test 1: Full categorization ──────────────────────────────────────────

    [Fact]
    public void FullCategorization_OnRealItemsJson_NoCrashAndPlausibleWeaponCount()
    {
        if (!TryGetDb(out var db)) return;
        var settings = new OverriddenSettings();
        var config = new ModConfig();

        var categorizer = new WeaponCategorizer(DefaultWeaponRulesFor(config, db));
        CategorizationResult result = null!;

        var act = () => { result = categorizer.Categorize(db, settings, config); };
        act.Should().NotThrow("categorization must not crash on real game data");

        result.WeaponToType.Count.Should().BeGreaterThan(100,
            "real game data contains well over 100 distinct weapon items");

        foreach (var (weaponId, types) in result.WeaponToType)
        {
            types.Should().NotBeEmpty($"weapon {weaponId} must have at least one type");
            foreach (var t in types)
            {
                t.Should().NotBeNullOrWhiteSpace($"type for weapon {weaponId} must be a non-empty string");
            }
        }
    }

    // ── Test 2: Full patch run (weapon array expander) ───────────────────────

    [Fact]
    public void FullPatchRun_OnRealQuestsJson_NoCrashAtLeastOneExpansionAndAllIdsValid()
    {
        if (!TryGetDb(out var itemDb)) return;
        // Load a fresh quest database — QuestPatcher mutates conditions in-place
        var questDb = SliceLoader.LoadQuestDatabase();
        // Use KeepInDb so the assertion that all patched IDs exist in the item DB holds.
        var config = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };
        var settings = new OverriddenSettings { Config = config };

        // Categorize
        var categorizer = new WeaponCategorizer(DefaultWeaponRulesFor(config, itemDb));
        var categorization = categorizer.Categorize(itemDb, settings, config);

        // Capture pre-patch weapon counts keyed by object reference (IDs are not unique across quests)
        var allConditions = questDb.Quests.Values
            .SelectMany(q => q.Conditions)
            .Where(c => c.Weapon.Count > 0)
            .ToList();

        var prePatchCounts = new Dictionary<ConditionNode, int>(ReferenceEqualityComparer.Instance);
        foreach (var c in allConditions)
        {
            prePatchCounts[c] = c.Weapon.Count;
        }

        // Patch
        var logger = NullModLogger.Instance;
        var expander = new WeaponArrayExpander(new TypeSelector(), NullNameResolver.Instance);
        var patcher = new QuestPatcher([expander], NullNameResolver.Instance);

        var act = () => patcher.Patch(questDb, settings, categorization, logger);
        act.Should().NotThrow("patching must not crash on real game data");

        // At least one condition should have grown (i.e. expansion occurred)
        var postPatchExpanded = prePatchCounts.Keys
            .Where(c => c.Weapon.Count > prePatchCounts[c])
            .ToList();

        postPatchExpanded.Should().NotBeEmpty("at least one weapon condition should have been expanded on real data");

        // All weapon IDs in patched conditions must exist in the item database
        var unknownIds = questDb.Quests.Values
            .SelectMany(q => q.Conditions)
            .SelectMany(c => c.Weapon)
            .Where(id => !itemDb.Items.ContainsKey(id))
            .Distinct()
            .ToList();

        unknownIds.Should().BeEmpty(
            $"all weapon IDs in patched conditions must exist in the item database, but found unknown: [{string.Join(", ", unknownIds.Take(10))}]");
    }

    // ── Test 3: Unknown weapon IDs are logged as warnings but don't crash ────

    [Fact]
    public void UnknownWeaponIds_AreLoggedAsWarnings_NoCrash()
    {
        if (!TryGetDb(out var itemDb)) return;
        // Use KeepInDb so the unknown ID is stripped with a warning (the behavior under test).
        var config = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };
        var settings = new OverriddenSettings { Config = config };

        var categorizer = new WeaponCategorizer(DefaultWeaponRulesFor(config, itemDb));
        var categorization = categorizer.Categorize(itemDb, settings, config);

        // Synthesize a quest with two valid weapons + one completely unknown ID
        var validWeaponId = categorization.WeaponToType.Keys.First();
        var anotherValidId = categorization.WeaponToType.Keys.Skip(1).First();
        const string unknownId = "deadbeefdeadbeefdeadbeef";

        var synthQuest = new QuestNode
        {
            Id = "smoke_test_unknown_ids",
            Conditions =
            [
                new ConditionNode
                {
                    Id = "smoke_condition_1",
                    ConditionType = "CounterCreator",
                    Weapon = [validWeaponId, anotherValidId, unknownId]
                }
            ]
        };

        var synthDb = new InMemoryQuestDatabase([synthQuest]);
        var logger = new CapturingModLogger();
        var expander = new WeaponArrayExpander(new TypeSelector(), NullNameResolver.Instance);
        var patcher = new QuestPatcher([expander], NullNameResolver.Instance);

        var act = () => patcher.Patch(synthDb, settings, categorization, logger);
        act.Should().NotThrow("patcher must not crash when weapon arrays contain unknown IDs");

        logger.Warnings.Should().Contain(w => w.Contains(unknownId),
            "unknown weapon IDs must be reported as warnings");
    }

    // ── Test 4: Performance ──────────────────────────────────────────────────

    [Fact]
    public void Performance_FullCategorizeAndPatch_CompletesInUnderFiveSeconds()
    {
        if (!TryGetDb(out var itemDb)) return;
        // Load a fresh quest database — QuestPatcher mutates conditions in-place
        var questDb = SliceLoader.LoadQuestDatabase();
        var settings = new OverriddenSettings();
        var config = new ModConfig();

        var sw = Stopwatch.StartNew();

        var categorizer = new WeaponCategorizer(DefaultWeaponRulesFor(config, itemDb));
        var categorization = categorizer.Categorize(itemDb, settings, config);

        var expander = new WeaponArrayExpander(new TypeSelector(), NullNameResolver.Instance);
        var patcher = new QuestPatcher([expander], NullNameResolver.Instance);
        patcher.Patch(questDb, settings, categorization, NullModLogger.Instance);

        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            $"full categorize + patch must complete in under 5 seconds (took {sw.Elapsed.TotalSeconds:F2}s)");
    }

    // ── Test 5: Default attachment rules cover every known quest attachment ─

    [Fact]
    public void DefaultAttachmentRules_ClassifyEveryAttachmentReferencedByQuests()
    {
        if (!TryGetDb(out var itemDb)) return;
        var questDb = SliceLoader.LoadQuestDatabase();
        var settings = new OverriddenSettings();

        var attachmentCategorizer = new AttachmentCategorizer(DefaultAttachmentRules);
        var attachmentCategorization = attachmentCategorizer.Categorize(itemDb, settings);

        var referenced = questDb.Quests.Values
            .SelectMany(q => q.Conditions)
            .SelectMany(c => c.WeaponModsInclusive.Concat(c.WeaponModsExclusive))
            .SelectMany(g => g)
            .Where(id => itemDb.Items.ContainsKey(id))
            .Distinct()
            .ToList();

        referenced.Should().NotBeEmpty("base-game quests reference at least one attachment");

        var uncategorized = referenced
            .Where(id => !attachmentCategorization.AttachmentToType.ContainsKey(id))
            .ToList();

        uncategorized.Should().BeEmpty(
            $"every quest-referenced attachment must be categorized, missing: [{string.Join(", ", uncategorized.Take(10))}]");
    }

    // ── Test 6: Attachment expansion on real quests grows mod groups ────────

    [Fact]
    public void WeaponModsExpander_OnRealQuests_ExpandsEveryMultiMemberGroup()
    {
        if (!TryGetDb(out var itemDb)) return;
        var questDb = SliceLoader.LoadQuestDatabase();
        var settings = new OverriddenSettings();
        var config = new ModConfig();

        var weaponCategorizer = new WeaponCategorizer(DefaultWeaponRulesFor(config, itemDb));
        var categorization = weaponCategorizer.Categorize(itemDb, settings, config);

        var attachmentCategorizer = new AttachmentCategorizer(DefaultAttachmentRules);
        var attachmentCategorization = attachmentCategorizer.Categorize(itemDb, settings);

        // Snapshot: per condition, capture each input group's membership (as a sorted set)
        // and the input field's group count. We need both to verify (a) multi-item AND-bundles
        // survive untouched and (b) the outer list never shrinks under singleton expansion.
        var preInclGroups = new Dictionary<ConditionNode, List<HashSet<string>>>(ReferenceEqualityComparer.Instance);
        var preExclGroups = new Dictionary<ConditionNode, List<HashSet<string>>>(ReferenceEqualityComparer.Instance);
        foreach (var condition in questDb.Quests.Values.SelectMany(q => q.Conditions))
        {
            if (condition.WeaponModsInclusive.Count > 0)
            {
                preInclGroups[condition] = condition.WeaponModsInclusive
                    .Select(g => new HashSet<string>(g, StringComparer.Ordinal))
                    .ToList();
            }
            if (condition.WeaponModsExclusive.Count > 0)
            {
                preExclGroups[condition] = condition.WeaponModsExclusive
                    .Select(g => new HashSet<string>(g, StringComparer.Ordinal))
                    .ToList();
            }
        }

        var modsExpander = new WeaponModsExpander(attachmentCategorization, NullNameResolver.Instance);
        var weaponExpander = new WeaponArrayExpander(new TypeSelector(), NullNameResolver.Instance);
        var patcher = new QuestPatcher([weaponExpander, modsExpander], NullNameResolver.Instance);

        var logger = new CapturingModLogger();
        patcher.Patch(questDb, settings, categorization, logger);

        // 1. Multi-item groups are AND-bundles — they must survive verbatim after patching.
        //    For every input group with >= 2 ids, find a post-patch group in the same field
        //    with identical membership (order-independent).
        var violations = new List<string>();

        foreach (var (cond, preGroups) in preInclGroups)
        {
            var postGroups = cond.WeaponModsInclusive
                .Select(g => new HashSet<string>(g, StringComparer.Ordinal))
                .ToList();
            foreach (var pre in preGroups.Where(g => g.Count >= 2))
            {
                var found = postGroups.Any(post => post.SetEquals(pre));
                if (!found)
                {
                    violations.Add(
                        $"inclusive multi-item group [{string.Join(", ", pre.OrderBy(x => x))}] " +
                        $"missing from condition {cond.Id} after patch");
                }
            }
        }
        foreach (var (cond, preGroups) in preExclGroups)
        {
            var postGroups = cond.WeaponModsExclusive
                .Select(g => new HashSet<string>(g, StringComparer.Ordinal))
                .ToList();
            foreach (var pre in preGroups.Where(g => g.Count >= 2))
            {
                var found = postGroups.Any(post => post.SetEquals(pre));
                if (!found)
                {
                    violations.Add(
                        $"exclusive multi-item group [{string.Join(", ", pre.OrderBy(x => x))}] " +
                        $"missing from condition {cond.Id} after patch");
                }
            }
        }

        violations.Should().BeEmpty(
            "multi-item mod groups are AND-bundles and must survive patching unchanged");

        // 2. Singleton-id preservation. Under field-level consensus the output may be
        //    smaller than the input (BSG sometimes repeats an id across singleton groups;
        //    dedup collapses those), so an outer-count lower bound is not a valid invariant.
        //    The correct invariant is per-id: every unique id that appeared in an input
        //    singleton must still appear somewhere in the post-patch field — either because
        //    the singleton was kept verbatim, or because the field expanded to the type that
        //    contains it.
        var missingIds = new List<string>();
        foreach (var (cond, preGroups) in preInclGroups)
        {
            var postIds = cond.WeaponModsInclusive
                .SelectMany(g => g)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var preSingletonId in preGroups.Where(g => g.Count == 1).SelectMany(g => g))
            {
                if (!postIds.Contains(preSingletonId))
                {
                    missingIds.Add($"inclusive id {preSingletonId} lost from condition {cond.Id}");
                }
            }
        }
        foreach (var (cond, preGroups) in preExclGroups)
        {
            var postIds = cond.WeaponModsExclusive
                .SelectMany(g => g)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var preSingletonId in preGroups.Where(g => g.Count == 1).SelectMany(g => g))
            {
                if (!postIds.Contains(preSingletonId))
                {
                    missingIds.Add($"exclusive id {preSingletonId} lost from condition {cond.Id}");
                }
            }
        }

        missingIds.Should().BeEmpty(
            "every authored singleton id must survive field-level consensus (either verbatim or via type expansion)");

        // 3. Test Drive - Part 1 sanity check. Quest 5c0bd94186f7747a727f09b2 condition
        //    5c124b5186f77468cf6f7347 encodes "M1A with Hybrid 46 suppressor + Schmidt &
        //    Bender scope" as a single 2-item group. The old catch-all expansion blew this
        //    group up to ~650 items (unsatisfiable AND-bundle) — catch regressions here.
        var testDriveQuest = questDb.Quests["5c0bd94186f7747a727f09b2"];
        var testDriveCond = testDriveQuest.Conditions
            .Single(c => c.Id == "5c124b5186f77468cf6f7347");

        testDriveCond.WeaponModsInclusive.Should().HaveCount(1,
            "Test Drive Part 1 has a single AND-bundle group (suppressor + scope)");
        testDriveCond.WeaponModsInclusive[0].Count.Should().Be(2,
            "the Test Drive AND-bundle is exactly the Hybrid 46 suppressor + Schmidt & Bender scope");
        testDriveCond.WeaponModsInclusive
            .Any(g => g.Count > 10)
            .Should().BeFalse(
                "no Test Drive group should balloon past 10 items — that would mean catch-all type expansion regressed");

        // 4. No unknown attachment warnings — every referenced attachment is categorized.
        logger.Warnings.Should().NotContain(w => w.Contains("unknown attachment ID"),
            "default rules cover every quest-referenced attachment");
    }

    // ── Test 7: weaponModsInclusive-only quest (no weapon array) ────────────

    [Fact]
    public void WeaponModsOnlyQuest_WeaponExpanderIsNoop_NoCrash()
    {
        if (!TryGetDb(out var itemDb)) return;
        var settings = new OverriddenSettings();
        var config = new ModConfig();

        var categorizer = new WeaponCategorizer(DefaultWeaponRulesFor(config, itemDb));
        var categorization = categorizer.Categorize(itemDb, settings, config);

        // Synthesize a quest with weaponModsInclusive but NO weapon array
        // Pick a real attachment ID from the DB if possible; fall back to a dummy
        var attachmentId = itemDb.Items.Values
            .FirstOrDefault(i => i.NodeType == "Item" && i.ParentId != null &&
                                 itemDb.Items.TryGetValue(i.ParentId, out var parent) &&
                                 parent.Name.StartsWith("Mod", StringComparison.OrdinalIgnoreCase))
            ?.Id ?? "dummy_attachment_id";

        var synthQuest = new QuestNode
        {
            Id = "smoke_test_mods_only",
            Conditions =
            [
                new ConditionNode
                {
                    Id = "smoke_mods_condition",
                    ConditionType = "CounterCreator",
                    Weapon = [],  // no weapon array
                    WeaponModsInclusive = [[attachmentId]]
                }
            ]
        };

        var synthDb = new InMemoryQuestDatabase([synthQuest]);
        var expander = new WeaponArrayExpander(new TypeSelector(), NullNameResolver.Instance);
        var patcher = new QuestPatcher([expander], NullNameResolver.Instance);

        var act = () => patcher.Patch(synthDb, settings, categorization, NullModLogger.Instance);
        act.Should().NotThrow("weapon expander must not crash when condition has no weapon array");

        var condition = synthDb.Quests["smoke_test_mods_only"].Conditions[0];
        condition.Weapon.Should().BeEmpty("weapon expander is a no-op when condition.Weapon is empty");
    }

    // ── Test 8: Inspector surfaces attachment data end-to-end ───────────────

    [Fact]
    public void InspectorResult_surfaces_attachment_data()
    {
        if (!TryGetDb(out var itemDb)) return;
        var questDb = SliceLoader.LoadQuestDatabase();
        var settings = new OverriddenSettings();

        // Build LoadResult — a sealed class with required init-only properties.
        var loaded = new AddMissingQuestRequirements.Inspector.LoadResult
        {
            Config   = settings.Config,
            Settings = settings,
            ItemDb   = itemDb,
            QuestDb  = questDb,
        };

        var result = AddMissingQuestRequirements.Inspector.PipelineRunner.Run(
            loaded, NullModLogger.Instance);

        result.Attachments.Should().HaveCountGreaterThan(100,
            "real SPT data exposes over a hundred attachments");
        result.AttachmentTypes.Should().HaveCountGreaterThan(10,
            "default attachment rules produce at least ten categories");
        result.Settings.AttachmentRules.Should().NotBeNull(
            "PipelineRunner must populate the attachment rule snapshot even if empty");
    }

    // ── Test 9: Inspector surfaces mod-group diffs end-to-end ─────────────────

    [Fact]
    public void InspectorResult_surfaces_mod_group_diffs()
    {
        if (!TryGetDb(out var itemDb)) return;
        var questDb = SliceLoader.LoadQuestDatabase();
        var settings = new OverriddenSettings();

        var loaded = new AddMissingQuestRequirements.Inspector.LoadResult
        {
            Config   = settings.Config,
            Settings = settings,
            ItemDb   = itemDb,
            QuestDb  = questDb,
        };

        var result = AddMissingQuestRequirements.Inspector.PipelineRunner.Run(
            loaded, NullModLogger.Instance);

        var withInclusive = result.Quests
            .SelectMany(q => q.Conditions)
            .Where(c => c.ModsInclusiveAfter.Any(g => g.Count > 0))
            .ToList();

        withInclusive.Should().NotBeEmpty(
            "base-game quests include at least one condition with weaponModsInclusive groups");
    }

    // ── Test 10: Melee and throwables categorize under Knife/ThrowWeap ────────

    [Fact]
    public void MeleeAndThrowablesCategorize_UnderKnifeAndThrowWeapTypes()
    {
        if (!TryGetDb(out var db)) return;
        var settings = new OverriddenSettings();
        var config = new ModConfig();

        var result = new WeaponCategorizer(DefaultWeaponRulesFor(config, db))
            .Categorize(db, settings, config);

        result.WeaponTypes.Should().ContainKey("Knife");
        result.WeaponTypes["Knife"].Count.Should().BeGreaterThanOrEqualTo(20,
            "base SPT ships ~22 melee weapons under the Knife node");

        result.WeaponTypes.Should().ContainKey("ThrowWeap");
        result.WeaponTypes["ThrowWeap"].Count.Should().BeGreaterThanOrEqualTo(5,
            "base SPT ships grenades under the ThrowWeap node");

        result.WeaponTypes.Should().ContainKey("Launcher");
        result.WeaponTypes["Launcher"].Should().Contain(
            ["5648b62b4bdc2d9d488b4585", "6357c98711fb55120211f7e1", "62e7e7bbe6da9612f743f1e0"],
            "GP-34, M203, and GP-25 Kostyor underbarrel grenade launchers live under Mod → GearMod → Launcher and must categorize via the Launcher ancestor");
    }

    // ── Test 11: Slaughterhouse produces no uncategorized debug spam ──────────

    [Fact]
    public void SlaughterhousePatch_ProducesNoUncategorizedDebugSpam()
    {
        const string slaughterhouseId = "63a9b36cc31b00242d28a99f";
        if (!TryGetDb(out var itemDb)) return;
        var questDb = SliceLoader.LoadQuestDatabase();
        var config = new ModConfig { Debug = true };
        var settings = new OverriddenSettings();
        var logger = new CapturingModLogger();

        var weaponCat = new WeaponCategorizer(DefaultWeaponRulesFor(config, itemDb))
            .Categorize(itemDb, settings, config);
        var attachmentCat = new AttachmentCategorizer(DefaultAttachmentRules)
            .Categorize(itemDb, settings);

        var nameResolver = new ItemDbNameResolver(itemDb);
        var patcher = new QuestPatcher(
            [
                new WeaponArrayExpander(new TypeSelector(), nameResolver),
                new WeaponModsExpander(attachmentCat, nameResolver),
            ],
            nameResolver);
        patcher.Patch(questDb, settings, weaponCat, logger);

        logger.Debugs
            .Where(m => m.Contains(slaughterhouseId) && m.Contains("uncategorized ID"))
            .Should().BeEmpty("melee items should categorize via the Knife rule and not hit the uncategorized debug path");
    }
}
