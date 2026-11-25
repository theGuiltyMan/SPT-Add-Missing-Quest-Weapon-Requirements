using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Util;

public class MergeHelperTests
{
    // ── MergeStringLists ────────────────────────────────────────────────────

    [Fact]
    public void MergeStringLists_IGNORE_keeps_existing_and_adds_new_items()
    {
        var existing = new List<string> { "a", "b" };
        var incoming = new List<string> { "b", "c", "d" };

        var result = MergeHelper.MergeStringLists(existing, incoming, OverrideBehaviour.IGNORE);

        result.Should().Equal("a", "b", "c", "d");
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
    public void MergeTypeRules_file_IGNORE_adds_rules_for_new_types_and_skips_known_types()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AKM"), Rule("AK74") };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.IGNORE);

        result.Select(r => r.Type).Should().Equal("AKM", "AK74");
    }

    [Fact]
    public void MergeTypeRules_file_IGNORE_keeps_all_rules_of_a_new_type_from_the_same_file()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AK74"), Rule("AK74") };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.IGNORE);

        result.Select(r => r.Type).Should().Equal("AKM", "AK74", "AK74");
    }

    [Fact]
    public void MergeTypeRules_file_IGNORE_honours_explicit_per_rule_behaviour()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule> { Rule("AKM", OverrideBehaviour.REPLACE) };

        var result = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.IGNORE);

        result.Should().ContainSingle().Which.Should().BeSameAs(incoming[0]);
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

    [Fact]
    public void MergeTypeRules_preserves_ApplyToManualOverrides_flag_through_merge_and_replace()
    {
        var existing = new List<TypeRule> { Rule("AKM") };
        var incoming = new List<TypeRule>
        {
            new() { Type = "HK_Family", ApplyToManualOverrides = true }
        };

        var merged = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.MERGE);
        merged.Single(r => r.Type == "HK_Family").ApplyToManualOverrides.Should().BeTrue();

        var replaced = MergeHelper.MergeTypeRules(existing, incoming, OverrideBehaviour.REPLACE);
        replaced.Single().ApplyToManualOverrides.Should().BeTrue();
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
}
