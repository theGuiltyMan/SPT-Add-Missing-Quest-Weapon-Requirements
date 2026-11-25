using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Util;
using FluentAssertions;
using Xunit;

namespace AddMissingQuestRequirements.Tests.Pipeline.Quest;

/// <summary>
/// End-to-end semantics of quest overrides: each file goes through
/// <see cref="MergeHelper.MergeQuestEntries"/> in load order, then
/// <see cref="QuestOverrideResolver.Resolve"/> answers per condition.
/// Each entry carries one marker weapon in <c>IncludedWeapons</c> so the
/// assertions can tell which entry (or merge of entries) a condition received.
/// </summary>
public sealed class QuestOverrideMergeResolveTests
{
    private const string Quest = "q1";

    private sealed record ModFile(OverrideBehaviour Behaviour, params QuestOverrideEntry[] Entries);

    private static OverriddenSettings Load(params ModFile[] files)
    {
        var acc = new Dictionary<string, List<QuestOverrideEntry>>();
        foreach (var file in files)
        {
            acc = MergeHelper.MergeQuestEntries(acc, [..file.Entries], file.Behaviour);
        }
        return new OverriddenSettings { QuestOverrides = acc };
    }

    private static QuestOverrideEntry Entry(
        string marker, string[] conditions, OverrideBehaviour? behaviour = null) => new()
    {
        Id              = Quest,
        Conditions      = [..conditions],
        IncludedWeapons = [marker],
        Behaviour       = behaviour,
    };

    private static QuestOverrideEntry Generic(string marker, OverrideBehaviour? behaviour = null) =>
        Entry(marker, [], behaviour);

    // Sub-condition id is the bare name; its CounterCreator parent is "outer-<name>".
    private static ConditionNode Cond(string id) => new()
    {
        Id                = id,
        ParentConditionId = "outer-" + id,
        ConditionType     = "CounterCreator",
    };

    private static List<string>? Markers(OverriddenSettings s, string condition) =>
        QuestOverrideResolver.Resolve(s, Quest, Cond(condition))?.IncludedWeapons;

    // ── Same file ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OverrideBehaviour.IGNORE)]
    [InlineData(OverrideBehaviour.MERGE)]
    [InlineData(OverrideBehaviour.REPLACE)]
    public void Same_file_entries_for_different_conditions_each_apply(OverrideBehaviour fileBehaviour)
    {
        var s = Load(new ModFile(fileBehaviour, Entry("a", ["A"]), Entry("b", ["B"])));

        Markers(s, "A").Should().Equal("a");
        Markers(s, "B").Should().Equal("b");
    }

    [Fact]
    public void Same_file_IGNORE_first_entry_wins_for_a_shared_condition()
    {
        var s = Load(new ModFile(OverrideBehaviour.IGNORE, Entry("first", ["A", "B"]), Entry("second", ["A"])));

        Markers(s, "A").Should().Equal("first");
        Markers(s, "B").Should().Equal("first");
    }

    [Fact]
    public void Same_file_REPLACE_last_entry_wins_for_a_shared_condition()
    {
        var s = Load(new ModFile(OverrideBehaviour.REPLACE, Entry("first", ["A", "B"]), Entry("second", ["A"])));

        Markers(s, "A").Should().Equal("second");
        Markers(s, "B").Should().Equal("first");
    }

    [Fact]
    public void Same_file_MERGE_merges_entries_for_a_shared_condition()
    {
        var s = Load(new ModFile(OverrideBehaviour.MERGE, Entry("first", ["A", "B"]), Entry("second", ["A"])));

        Markers(s, "A").Should().BeEquivalentTo(["first", "second"]);
        Markers(s, "B").Should().Equal("first");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Same_file_specific_entry_beats_quest_wide_entry_in_either_order(bool genericFirst)
    {
        var generic  = Generic("generic");
        var specific = Entry("specific", ["A"]);
        var s = Load(new ModFile(OverrideBehaviour.IGNORE, genericFirst ? [generic, specific] : [specific, generic]));

        Markers(s, "A").Should().Equal("specific");
        Markers(s, "B").Should().Equal("generic");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Same_file_specific_DELETE_beats_quest_wide_entry_in_either_order(bool deleteFirst)
    {
        var delete  = Entry("deleted", ["A"], OverrideBehaviour.DELETE);
        var generic = Generic("generic");
        var s = Load(new ModFile(OverrideBehaviour.IGNORE, deleteFirst ? [delete, generic] : [generic, delete]));

        Markers(s, "A").Should().BeNull();
        Markers(s, "B").Should().Equal("generic");
    }

    [Fact]
    public void DELETE_outcome_does_not_depend_on_earlier_entries_for_other_conditions()
    {
        var withUnrelated = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("unrelated", ["Z"])),
            new ModFile(OverrideBehaviour.IGNORE, Entry("deleted", ["A"], OverrideBehaviour.DELETE), Generic("generic")));
        var without = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("deleted", ["A"], OverrideBehaviour.DELETE), Generic("generic")));

        Markers(withUnrelated, "A").Should().BeNull();
        Markers(without, "A").Should().BeNull();
    }

    // ── Across files: condition-specific vs condition-specific ───────────────

    [Fact]
    public void IGNORE_overlap_keeps_earlier_entry()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A", "B"])),
            new ModFile(OverrideBehaviour.IGNORE, Entry("late", ["A"])));

        Markers(s, "A").Should().Equal("early");
        Markers(s, "B").Should().Equal("early");
    }

    [Fact]
    public void IGNORE_disjoint_adds_later_entry()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.IGNORE, Entry("late", ["C"])));

        Markers(s, "A").Should().Equal("early");
        Markers(s, "C").Should().Equal("late");
    }

    [Fact]
    public void IGNORE_partial_overlap_adds_only_uncovered_conditions()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A", "B"])),
            new ModFile(OverrideBehaviour.IGNORE, Entry("late", ["B", "C"])));

        Markers(s, "A").Should().Equal("early");
        Markers(s, "B").Should().Equal("early");
        Markers(s, "C").Should().Equal("late");
    }

    [Fact]
    public void IGNORE_one_entry_per_condition_after_combined_entry_is_dropped_only_where_covered()
    {
        // Shape of the real case: an earlier file covers two conditions in one entry,
        // a later file ships one entry per condition plus one for a third condition.
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A", "B"])),
            new ModFile(OverrideBehaviour.IGNORE, Entry("lateA", ["A"]), Entry("lateB", ["B"]), Entry("lateC", ["C"])));

        Markers(s, "A").Should().Equal("early");
        Markers(s, "B").Should().Equal("early");
        Markers(s, "C").Should().Equal("lateC");
    }

    [Fact]
    public void REPLACE_overlap_wins_only_for_listed_conditions()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A", "B"])),
            new ModFile(OverrideBehaviour.REPLACE, Entry("late", ["A"])));

        Markers(s, "A").Should().Equal("late");
        Markers(s, "B").Should().Equal("early");
    }

    [Fact]
    public void REPLACE_disjoint_adds_later_entry()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.REPLACE, Entry("late", ["C"])));

        Markers(s, "A").Should().Equal("early");
        Markers(s, "C").Should().Equal("late");
    }

    [Fact]
    public void MERGE_overlap_merges_only_listed_conditions()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A", "B"])),
            new ModFile(OverrideBehaviour.MERGE, Entry("late", ["A"])));

        Markers(s, "A").Should().BeEquivalentTo(["early", "late"]);
        Markers(s, "B").Should().Equal("early");
    }

    [Fact]
    public void MERGE_disjoint_adds_later_entry()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.MERGE, Entry("late", ["C"])));

        Markers(s, "A").Should().Equal("early");
        Markers(s, "C").Should().Equal("late");
    }

    [Fact]
    public void DELETE_overlap_removes_only_listed_conditions()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A", "B"])),
            new ModFile(OverrideBehaviour.DELETE, Entry("late", ["A"])));

        Markers(s, "A").Should().BeNull();
        Markers(s, "B").Should().Equal("early");
    }

    [Fact]
    public void DELETE_disjoint_changes_nothing()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.DELETE, Entry("late", ["C"])));

        Markers(s, "A").Should().Equal("early");
        Markers(s, "C").Should().BeNull();
    }

    [Fact]
    public void Later_file_can_re_add_a_deleted_condition()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.DELETE, Entry("deleted", ["A"])),
            new ModFile(OverrideBehaviour.IGNORE, Entry("late", ["A"])));

        Markers(s, "A").Should().Equal("late");
    }

    [Fact]
    public void Entry_level_behaviour_overrides_file_default()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A", "B"])),
            new ModFile(OverrideBehaviour.IGNORE, Entry("late", ["A"], OverrideBehaviour.REPLACE)));

        Markers(s, "A").Should().Equal("late");
        Markers(s, "B").Should().Equal("early");
    }

    [Fact]
    public void Parent_id_and_sub_id_entries_target_the_same_condition()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["outer-A"])),
            new ModFile(OverrideBehaviour.REPLACE, Entry("late", ["A"])));

        Markers(s, "A").Should().Equal("late");
    }

    [Fact]
    public void Parent_id_and_sub_id_entries_overlap_under_IGNORE()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.IGNORE, Entry("late", ["outer-A"])));

        Markers(s, "A").Should().Equal("early");
    }

    // ── Across files: quest-wide entries count as covering every condition ──

    [Fact]
    public void Earlier_quest_wide_then_specific_IGNORE_keeps_quest_wide()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Generic("generic")),
            new ModFile(OverrideBehaviour.IGNORE, Entry("late", ["A"])));

        Markers(s, "A").Should().Equal("generic");
        Markers(s, "B").Should().Equal("generic");
    }

    [Fact]
    public void Earlier_quest_wide_then_specific_REPLACE_wins_for_listed_condition()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Generic("generic")),
            new ModFile(OverrideBehaviour.REPLACE, Entry("late", ["A"])));

        Markers(s, "A").Should().Equal("late");
        Markers(s, "B").Should().Equal("generic");
    }

    [Fact]
    public void Earlier_quest_wide_then_specific_MERGE_merges_listed_condition()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Generic("generic")),
            new ModFile(OverrideBehaviour.MERGE, Entry("late", ["A"])));

        Markers(s, "A").Should().BeEquivalentTo(["generic", "late"]);
        Markers(s, "B").Should().Equal("generic");
    }

    [Fact]
    public void Earlier_quest_wide_then_specific_DELETE_removes_listed_condition()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Generic("generic")),
            new ModFile(OverrideBehaviour.DELETE, Entry("late", ["A"])));

        Markers(s, "A").Should().BeNull();
        Markers(s, "B").Should().Equal("generic");
    }

    [Fact]
    public void Earlier_specific_then_quest_wide_IGNORE_fills_uncovered_conditions()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.IGNORE, Generic("generic")));

        Markers(s, "A").Should().Equal("early");
        Markers(s, "B").Should().Equal("generic");
    }

    [Fact]
    public void Earlier_specific_then_quest_wide_REPLACE_wins_everywhere()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.REPLACE, Generic("generic")));

        Markers(s, "A").Should().Equal("generic");
        Markers(s, "B").Should().Equal("generic");
    }

    [Fact]
    public void Earlier_specific_then_quest_wide_MERGE_merges_everywhere()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.MERGE, Generic("generic")));

        Markers(s, "A").Should().BeEquivalentTo(["early", "generic"]);
        Markers(s, "B").Should().Equal("generic");
    }

    [Fact]
    public void Quest_wide_DELETE_removes_every_condition()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"]), Generic("generic")),
            new ModFile(OverrideBehaviour.DELETE, Generic("deleted")));

        Markers(s, "A").Should().BeNull();
        Markers(s, "B").Should().BeNull();
    }

    [Fact]
    public void Quest_wide_IGNORE_after_quest_wide_keeps_earlier()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Generic("early")),
            new ModFile(OverrideBehaviour.IGNORE, Generic("late")));

        Markers(s, "A").Should().Equal("early");
    }

    [Fact]
    public void Quest_wide_REPLACE_after_quest_wide_takes_later()
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Generic("early")),
            new ModFile(OverrideBehaviour.REPLACE, Generic("late")));

        Markers(s, "A").Should().Equal("late");
    }

    [Fact]
    public void Later_file_specific_entry_shadows_its_own_quest_wide_entry()
    {
        // The later file's contribution for A is its specific entry; its quest-wide
        // entry applies only to the conditions it does not name.
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["B"])),
            new ModFile(OverrideBehaviour.REPLACE, Generic("lateGeneric"), Entry("lateA", ["A"])));

        Markers(s, "A").Should().Equal("lateA");
        Markers(s, "B").Should().Equal("lateGeneric");
    }

    // ── Other quests and unknowns ────────────────────────────────────────────

    [Fact]
    public void DELETE_for_unknown_quest_is_a_no_op()
    {
        var s = Load(new ModFile(OverrideBehaviour.DELETE, Entry("x", ["A"])));

        Markers(s, "A").Should().BeNull();
    }

    [Fact]
    public void Entries_for_other_quests_do_not_interact()
    {
        var other = new QuestOverrideEntry { Id = "q2", Conditions = ["A"], IncludedWeapons = ["other"] };
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.REPLACE, other));

        Markers(s, "A").Should().Equal("early");
        QuestOverrideResolver.Resolve(s, "q2", Cond("A"))!.IncludedWeapons.Should().Equal("other");
    }

    // ── MERGE field semantics (per condition) ────────────────────────────────

    [Fact]
    public void MERGE_takes_most_restrictive_modes_and_unions_mod_fields()
    {
        var early = new QuestOverrideEntry
        {
            Id                 = Quest,
            Conditions         = ["A"],
            ExpansionMode      = ExpansionMode.NoExpansion,
            ModsExpansionMode  = ExpansionMode.WhitelistOnly,
            IncludedMods       = ["mod-a"],
            ExcludedMods       = ["mod-x"],
            IncludedModBundles = [["barrel_a", "scope_a"]],
        };
        var late = new QuestOverrideEntry
        {
            Id                 = Quest,
            Conditions         = ["A"],
            ExpansionMode      = ExpansionMode.WhitelistOnly,
            ModsExpansionMode  = ExpansionMode.NoExpansion,
            IncludedMods       = ["mod-b"],
            ExcludedMods       = ["mod-y"],
            IncludedModBundles = [["scope_a", "barrel_a"], ["barrel_c"]],
        };

        var s = Load(new ModFile(OverrideBehaviour.IGNORE, early), new ModFile(OverrideBehaviour.MERGE, late));
        var resolved = QuestOverrideResolver.Resolve(s, Quest, Cond("A"))!;

        resolved.ExpansionMode.Should().Be(ExpansionMode.WhitelistOnly);
        resolved.ModsExpansionMode.Should().Be(ExpansionMode.WhitelistOnly);
        resolved.IncludedMods.Should().BeEquivalentTo(["mod-a", "mod-b"]);
        resolved.ExcludedMods.Should().BeEquivalentTo(["mod-x", "mod-y"]);
        resolved.IncludedModBundles.Should().HaveCount(2, "reordered duplicate bundle dedupes");
    }

    [Theory]
    [InlineData(ExpansionMode.NoExpansion, ExpansionMode.Auto, ExpansionMode.NoExpansion)]
    [InlineData(ExpansionMode.Auto, ExpansionMode.WhitelistOnly, ExpansionMode.WhitelistOnly)]
    [InlineData(ExpansionMode.WhitelistOnly, ExpansionMode.NoExpansion, ExpansionMode.WhitelistOnly)]
    public void MERGE_expansion_mode_priority(ExpansionMode early, ExpansionMode late, ExpansionMode expected)
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, new QuestOverrideEntry { Id = Quest, ExpansionMode = early }),
            new ModFile(OverrideBehaviour.MERGE, new QuestOverrideEntry { Id = Quest, ExpansionMode = late }));

        QuestOverrideResolver.Resolve(s, Quest, Cond("A"))!.ExpansionMode.Should().Be(expected);
    }

    [Theory]
    [InlineData(ExpansionMode.NoExpansion, ExpansionMode.Auto, ExpansionMode.NoExpansion)]
    [InlineData(ExpansionMode.Auto, ExpansionMode.WhitelistOnly, ExpansionMode.WhitelistOnly)]
    [InlineData(ExpansionMode.WhitelistOnly, ExpansionMode.NoExpansion, ExpansionMode.WhitelistOnly)]
    public void MERGE_mods_expansion_mode_priority(ExpansionMode early, ExpansionMode late, ExpansionMode expected)
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, new QuestOverrideEntry { Id = Quest, ModsExpansionMode = early }),
            new ModFile(OverrideBehaviour.MERGE, new QuestOverrideEntry { Id = Quest, ModsExpansionMode = late }));

        QuestOverrideResolver.Resolve(s, Quest, Cond("A"))!.ModsExpansionMode.Should().Be(expected);
    }

    [Fact]
    public void MERGE_unions_excluded_weapons_and_dedupes_excluded_mod_bundles()
    {
        var early = new QuestOverrideEntry
        {
            Id                 = Quest,
            Conditions         = ["A"],
            ExcludedWeapons    = ["w-a", "w-shared"],
            ExcludedModBundles = [["barrel_a", "scope_a"]],
        };
        var late = new QuestOverrideEntry
        {
            Id                 = Quest,
            Conditions         = ["A"],
            ExcludedWeapons    = ["w-shared", "w-b"],
            ExcludedModBundles = [["scope_a", "barrel_a"], ["barrel_c"]],
        };

        var s = Load(new ModFile(OverrideBehaviour.IGNORE, early), new ModFile(OverrideBehaviour.MERGE, late));
        var resolved = QuestOverrideResolver.Resolve(s, Quest, Cond("A"))!;

        resolved.ExcludedWeapons.Should().BeEquivalentTo(["w-a", "w-shared", "w-b"]);
        resolved.ExcludedModBundles.Should().BeEquivalentTo(
            [new List<string> { "barrel_a", "scope_a" }, new List<string> { "barrel_c" }],
            "a reordered duplicate bundle dedupes");
    }

    // ── Layers ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OverrideBehaviour.IGNORE, "early")]
    [InlineData(OverrideBehaviour.REPLACE, "late")]
    public void Empty_intermediate_file_keeps_layer_order(OverrideBehaviour lateBehaviour, string expected)
    {
        var s = Load(
            new ModFile(OverrideBehaviour.IGNORE, Entry("early", ["A"])),
            new ModFile(OverrideBehaviour.REPLACE),
            new ModFile(lateBehaviour, Entry("late", ["A"])));

        Markers(s, "A").Should().Equal(expected);
        s.QuestOverrides[Quest].Select(e => e.Layer).Should().BeInAscendingOrder()
            .And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Layer_is_not_serialized_and_is_ignored_on_read()
    {
        var stamped = MergeHelper.MergeQuestEntries(
            MergeHelper.MergeQuestEntries([], [Entry("early", ["A"])], OverrideBehaviour.IGNORE),
            [Entry("late", ["A"])], OverrideBehaviour.IGNORE)[Quest][1];
        stamped.Layer.Should().Be(1);

        System.Text.Json.JsonSerializer.Serialize(stamped, JsoncReader.DefaultOptions)
            .Should().NotContainEquivalentOf("\"layer\"");

        var read = JsoncReader.Deserialize<QuestOverrideEntry>("""{ "id": "q1", "layer": 7 }""")!;
        read.Layer.Should().Be(0);
    }

    [Fact]
    public void Merge_does_not_mutate_earlier_accumulator()
    {
        var first = MergeHelper.MergeQuestEntries([], [Entry("early", ["A"])], OverrideBehaviour.IGNORE);
        var firstCount = first[Quest].Count;

        MergeHelper.MergeQuestEntries(first, [Entry("late", ["A"])], OverrideBehaviour.REPLACE);

        first[Quest].Should().HaveCount(firstCount);
    }
}
