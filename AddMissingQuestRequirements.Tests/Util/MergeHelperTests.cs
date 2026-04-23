using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Util;

public class MergeHelperTests
{
    // ── MergeStringLists ────────────────────────────────────────────────────

    [Fact]
    public void MergeStringLists_IGNORE_keeps_existing_skips_new()
    {
        var existing = new List<string> { "a", "b" };
        var incoming = new List<string> { "c", "d" };

        var result = MergeHelper.MergeStringLists(existing, incoming, OverrideBehaviour.IGNORE);

        result.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void MergeStringLists_IGNORE_adds_incoming_when_existing_is_empty()
    {
        var result = MergeHelper.MergeStringLists([], ["c", "d"], OverrideBehaviour.IGNORE);
        result.Should().BeEquivalentTo(["c", "d"]);
    }

    [Fact]
    public void MergeStringLists_MERGE_unions_both_lists()
    {
        var result = MergeHelper.MergeStringLists(["a", "b"], ["b", "c"], OverrideBehaviour.MERGE);
        result.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void MergeStringLists_REPLACE_uses_incoming_only()
    {
        var result = MergeHelper.MergeStringLists(["a", "b"], ["c", "d"], OverrideBehaviour.REPLACE);
        result.Should().BeEquivalentTo(["c", "d"]);
    }

    [Fact]
    public void MergeStringLists_DELETE_removes_incoming_items_from_existing()
    {
        var result = MergeHelper.MergeStringLists(["a", "b", "c"], ["b", "c"], OverrideBehaviour.DELETE);
        result.Should().BeEquivalentTo(["a"]);
    }

    // ── ApplyOverridableList ────────────────────────────────────────────────

    [Fact]
    public void ApplyOverridableList_adds_bare_values_to_existing()
    {
        var existing = new List<string> { "a" };
        var incoming = new List<Overridable<string>>
        {
            new("b"),
            new("c")
        };

        var result = MergeHelper.ApplyOverridableList(existing, incoming, OverrideBehaviour.MERGE);

        result.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void ApplyOverridableList_DELETE_behaviour_removes_value_from_existing()
    {
        var existing = new List<string> { "a", "b", "c" };
        var incoming = new List<Overridable<string>>
        {
            new("b", OverrideBehaviour.DELETE),
            new("d") // new value added
        };

        var result = MergeHelper.ApplyOverridableList(existing, incoming, OverrideBehaviour.MERGE);

        result.Should().BeEquivalentTo(["a", "c", "d"]);
    }

    [Fact]
    public void ApplyOverridableList_REPLACE_discards_existing_and_applies_incoming()
    {
        var existing = new List<string> { "a", "b" };
        var incoming = new List<Overridable<string>> { new("c"), new("d") };

        var result = MergeHelper.ApplyOverridableList(existing, incoming, OverrideBehaviour.REPLACE);

        result.Should().BeEquivalentTo(["c", "d"]);
    }

    // ── MergeStringDicts ────────────────────────────────────────────────────

    [Fact]
    public void MergeStringDicts_IGNORE_keeps_existing_key_skips_incoming()
    {
        var existing = new Dictionary<string, string> { ["weapon_a"] = "AssaultRifle" };
        var incoming = new Dictionary<string, string> { ["weapon_a"] = "Pistol", ["weapon_b"] = "Smg" };

        var result = MergeHelper.MergeStringDicts(existing, incoming, OverrideBehaviour.IGNORE);

        result["weapon_a"].Should().Be("AssaultRifle"); // original preserved
        result["weapon_b"].Should().Be("Smg");           // new key added
    }

    [Fact]
    public void MergeStringDicts_MERGE_unions_comma_separated_types_for_existing_key()
    {
        var existing = new Dictionary<string, string> { ["weapon_a"] = "AssaultRifle" };
        var incoming = new Dictionary<string, string> { ["weapon_a"] = "CustomCarbine" };

        var result = MergeHelper.MergeStringDicts(existing, incoming, OverrideBehaviour.MERGE);

        var types = result["weapon_a"].Split(',').Select(s => s.Trim()).ToHashSet();
        types.Should().BeEquivalentTo(["AssaultRifle", "CustomCarbine"]);
    }

    [Fact]
    public void MergeStringDicts_MERGE_deduplicates_when_type_already_present()
    {
        var existing = new Dictionary<string, string> { ["weapon_a"] = "AssaultRifle,Pistol" };
        var incoming = new Dictionary<string, string> { ["weapon_a"] = "Pistol,Shotgun" };

        var result = MergeHelper.MergeStringDicts(existing, incoming, OverrideBehaviour.MERGE);

        var types = result["weapon_a"].Split(',').Select(s => s.Trim()).ToHashSet();
        types.Should().BeEquivalentTo(["AssaultRifle", "Pistol", "Shotgun"]);
    }

    [Fact]
    public void MergeStringDicts_REPLACE_overwrites_existing_key()
    {
        var existing = new Dictionary<string, string> { ["weapon_a"] = "AssaultRifle" };
        var incoming = new Dictionary<string, string> { ["weapon_a"] = "Pistol" };

        var result = MergeHelper.MergeStringDicts(existing, incoming, OverrideBehaviour.REPLACE);

        result["weapon_a"].Should().Be("Pistol");
    }

    [Fact]
    public void MergeStringDicts_DELETE_removes_key()
    {
        var existing = new Dictionary<string, string> { ["weapon_a"] = "AssaultRifle", ["weapon_b"] = "Smg" };
        var incoming = new Dictionary<string, string> { ["weapon_a"] = "" }; // value irrelevant for DELETE

        var result = MergeHelper.MergeStringDicts(existing, incoming, OverrideBehaviour.DELETE);

        result.Should().NotContainKey("weapon_a");
        result.Should().ContainKey("weapon_b");
    }

    // ── MergeCanBeUsedAs ────────────────────────────────────────────────────

    [Fact]
    public void MergeCanBeUsedAs_MERGE_unions_alias_sets()
    {
        var existing = new Dictionary<string, HashSet<string>>
        {
            ["weapon_a"] = ["weapon_b"]
        };
        var incoming = new Dictionary<string, List<Overridable<string>>>
        {
            ["weapon_a"] = [new("weapon_c")]
        };

        var result = MergeHelper.MergeCanBeUsedAs(existing, incoming, OverrideBehaviour.MERGE);

        result["weapon_a"].Should().BeEquivalentTo(["weapon_b", "weapon_c"]);
    }

    [Fact]
    public void MergeCanBeUsedAs_DELETE_entry_removes_alias_from_set()
    {
        var existing = new Dictionary<string, HashSet<string>>
        {
            ["weapon_a"] = ["weapon_b", "weapon_c"]
        };
        var incoming = new Dictionary<string, List<Overridable<string>>>
        {
            ["weapon_a"] = [new("weapon_b", OverrideBehaviour.DELETE)]
        };

        var result = MergeHelper.MergeCanBeUsedAs(existing, incoming, OverrideBehaviour.MERGE);

        result["weapon_a"].Should().BeEquivalentTo(["weapon_c"]);
    }

    [Fact]
    public void MergeCanBeUsedAs_IGNORE_skips_existing_key()
    {
        var existing = new Dictionary<string, HashSet<string>>
        {
            ["weapon_a"] = ["weapon_b"]
        };
        var incoming = new Dictionary<string, List<Overridable<string>>>
        {
            ["weapon_a"] = [new("weapon_c")]
        };

        var result = MergeHelper.MergeCanBeUsedAs(existing, incoming, OverrideBehaviour.IGNORE);

        result["weapon_a"].Should().BeEquivalentTo(["weapon_b"]); // unchanged
    }

    [Fact]
    public void MergeCanBeUsedAs_adds_new_key_regardless_of_behaviour()
    {
        var existing = new Dictionary<string, HashSet<string>>();
        var incoming = new Dictionary<string, List<Overridable<string>>>
        {
            ["weapon_new"] = [new("weapon_x"), new("weapon_y")]
        };

        var result = MergeHelper.MergeCanBeUsedAs(existing, incoming, OverrideBehaviour.IGNORE);

        result["weapon_new"].Should().BeEquivalentTo(["weapon_x", "weapon_y"]);
    }

    // ── MergeQuestEntries ───────────────────────────────────────────────────

    [Fact]
    public void MergeQuestEntries_IGNORE_skips_second_mods_entry_for_same_quest()
    {
        var existing = EntriesToDict([new QuestOverrideEntry
        {
            Id = "quest1",
            IncludedWeapons = ["weapon_a"]
        }]);
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "quest1", IncludedWeapons = ["weapon_b"] }
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.IGNORE);

        result["quest1"].Should().HaveCount(1);
        result["quest1"][0].IncludedWeapons.Should().BeEquivalentTo(["weapon_a"]);
    }

    [Fact]
    public void MergeQuestEntries_MERGE_combines_whitelist_arrays()
    {
        var existing = EntriesToDict([new QuestOverrideEntry
        {
            Id = "quest1",
            IncludedWeapons = ["weapon_a"]
        }]);
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "quest1", IncludedWeapons = ["weapon_b"] }
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.MERGE);

        result["quest1"][0].IncludedWeapons.Should().BeEquivalentTo(["weapon_a", "weapon_b"]);
    }

    [Fact]
    public void MergeQuestEntries_REPLACE_overwrites_existing_entry()
    {
        var existing = EntriesToDict([new QuestOverrideEntry
        {
            Id = "quest1",
            IncludedWeapons = ["weapon_a"]
        }]);
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "quest1", IncludedWeapons = ["weapon_b"] }
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.REPLACE);

        result["quest1"].Should().HaveCount(1);
        result["quest1"][0].IncludedWeapons.Should().BeEquivalentTo(["weapon_b"]);
    }

    [Fact]
    public void MergeQuestEntries_DELETE_removes_quest_entirely()
    {
        var existing = EntriesToDict([new QuestOverrideEntry { Id = "quest1" }]);
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "quest1", Behaviour = OverrideBehaviour.DELETE }
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.IGNORE);

        result.Should().NotContainKey("quest1");
    }

    [Fact]
    public void MergeQuestEntries_entry_level_behaviour_overrides_file_level()
    {
        // File default is IGNORE, but this specific entry says REPLACE
        var existing = EntriesToDict([new QuestOverrideEntry
        {
            Id = "quest1",
            IncludedWeapons = ["weapon_a"]
        }]);
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "quest1", Behaviour = OverrideBehaviour.REPLACE, IncludedWeapons = ["weapon_b"] }
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.IGNORE);

        result["quest1"][0].IncludedWeapons.Should().BeEquivalentTo(["weapon_b"]);
    }

    [Fact]
    public void MergeQuestEntries_adds_new_quest_id_regardless_of_behaviour()
    {
        var existing = new Dictionary<string, List<QuestOverrideEntry>>();
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "quest_new", IncludedWeapons = ["weapon_x"] }
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.IGNORE);

        result["quest_new"][0].IncludedWeapons.Should().BeEquivalentTo(["weapon_x"]);
    }

    [Fact]
    public void MergeQuestEntries_preserves_mod_fields_on_new_quest_add()
    {
        // Guards a real regression: the clone path dropped IncludedMods /
        // ExcludedMods / ModsExpansionMode on every insert, so user-authored
        // attachment-group overrides silently never reached the expander.
        var existing = new Dictionary<string, List<QuestOverrideEntry>>();
        var incoming = new List<QuestOverrideEntry>
        {
            new()
            {
                Id                = "quest_new",
                ModsExpansionMode = ExpansionMode.WhitelistOnly,
                IncludedMods      = ["attachment_a", "attachment_b"],
                ExcludedMods      = ["attachment_c"]
            }
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.IGNORE);

        var entry = result["quest_new"][0];
        entry.ModsExpansionMode.Should().Be(ExpansionMode.WhitelistOnly);
        entry.IncludedMods.Should().BeEquivalentTo(["attachment_a", "attachment_b"]);
        entry.ExcludedMods.Should().BeEquivalentTo(["attachment_c"]);
    }

    [Fact]
    public void MergeQuestEntries_MERGE_unions_mod_fields_across_entries()
    {
        var existing = EntriesToDict([new QuestOverrideEntry
        {
            Id           = "quest1",
            IncludedMods = ["a1"],
            ExcludedMods = ["x1"]
        }]);
        var incoming = new List<QuestOverrideEntry>
        {
            new()
            {
                Id           = "quest1",
                IncludedMods = ["a2"],
                ExcludedMods = ["x2"]
            }
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.MERGE);

        result["quest1"][0].IncludedMods.Should().BeEquivalentTo(["a1", "a2"]);
        result["quest1"][0].ExcludedMods.Should().BeEquivalentTo(["x1", "x2"]);
    }

    // ── MergeTypeRules ──────────────────────────────────────────────────────

    // File-level behaviour

    [Fact]
    public void MergeTypeRules_file_MERGE_appends_all_incoming()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AK74"), Rule("AR15") };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.MERGE);

        result.Should().HaveCount(3);
        result.Select(r => r.Type).Should().Contain(["AKM", "AK74", "AR15"]);
    }

    [Fact]
    public void MergeTypeRules_file_IGNORE_returns_existing_when_non_empty()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AK74") };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.IGNORE);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be("AKM");
    }

    [Fact]
    public void MergeTypeRules_file_IGNORE_processes_incoming_when_existing_is_empty()
    {
        var incoming = new List<TypeRule> { Rule("AK74") };

        var result = MergeHelper.MergeTypeRules([], incoming, OverrideBehaviour.IGNORE);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be("AK74");
    }

    [Fact]
    public void MergeTypeRules_file_REPLACE_discards_existing_and_processes_incoming()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AK74") };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.REPLACE);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be("AK74");
    }

    [Fact]
    public void MergeTypeRules_file_DELETE_returns_empty_list()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AK74") };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.DELETE);

        result.Should().BeEmpty();
    }

    // Per-rule behaviour (all under file-level MERGE)

    [Fact]
    public void MergeTypeRules_per_rule_null_always_appends()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AKM", null) }; // same type, no behaviour

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.MERGE);

        result.Should().HaveCount(2);
        result.All(r => r.Type == "AKM").Should().BeTrue();
    }

    [Fact]
    public void MergeTypeRules_per_rule_IGNORE_skips_when_type_already_exists()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AKM", OverrideBehaviour.IGNORE) };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.MERGE);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void MergeTypeRules_per_rule_IGNORE_appends_when_type_not_in_accumulated()
    {
        var incoming = new List<TypeRule> { Rule("AK74", OverrideBehaviour.IGNORE) };

        var result = MergeHelper.MergeTypeRules([], incoming, OverrideBehaviour.MERGE);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be("AK74");
    }

    [Fact]
    public void MergeTypeRules_per_rule_REPLACE_removes_same_type_then_appends()
    {
        var existing = new List<TypeRule> { Rule("AKM"), Rule("AKM") }; // two AKM rules
        var incoming = new List<TypeRule> { Rule("AKM", OverrideBehaviour.REPLACE) };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.MERGE);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be("AKM");
        result[0].Behaviour.Should().Be(OverrideBehaviour.REPLACE);
    }

    [Fact]
    public void MergeTypeRules_per_rule_REPLACE_with_no_existing_type_just_appends()
    {
        var incoming = new List<TypeRule> { Rule("AKM", OverrideBehaviour.REPLACE) };

        var result = MergeHelper.MergeTypeRules([], incoming, OverrideBehaviour.MERGE);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void MergeTypeRules_per_rule_DELETE_removes_all_matching_types()
    {
        var existing = new List<TypeRule> { Rule("AKM"), Rule("AKM"), Rule("AK74") };
        var incoming = new List<TypeRule> { Rule("AKM", OverrideBehaviour.DELETE) };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.MERGE);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be("AK74");
    }

    [Fact]
    public void MergeTypeRules_second_incoming_REPLACE_wins_over_first_appended()
    {
        var existing = new List<TypeRule>();
        var rule1 = Rule("AKM");                                         // null → append
        var rule2 = Rule("AKM", OverrideBehaviour.REPLACE);             // REPLACE → remove rule1, append rule2

        var result = MergeHelper.MergeTypeRules(existing, [rule1, rule2], OverrideBehaviour.MERGE);

        result.Should().HaveCount(1);
        result[0].Behaviour.Should().Be(OverrideBehaviour.REPLACE);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static TypeRule Rule(string type, OverrideBehaviour? behaviour = null) => new()
    {
        Type = type,
        Behaviour = behaviour
    };

    private static Dictionary<string, List<QuestOverrideEntry>> EntriesToDict(
        IEnumerable<QuestOverrideEntry> entries) =>
        entries.GroupBy(e => e.Id)
               .ToDictionary(g => g.Key, g => g.ToList());
}
