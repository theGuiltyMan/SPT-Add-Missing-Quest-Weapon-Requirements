using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Rules;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Rules;

/// <summary>
/// Tests for core-rule detection via <see cref="RuleCoreDetector.IsCore"/>.
/// </summary>
public class RuleCoreDetectorTests
{
    // ── Helper: build a TypeRule from a JSON conditions object ───────────────

    private static TypeRule MakeRule(string conditionsJson)
    {
        var conditions = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(conditionsJson)!;
        return new TypeRule { Conditions = conditions, Type = "T", AlsoAs = [] };
    }

    private static bool IsCore(TypeRule rule) => RuleCoreDetector.IsCore(rule.Conditions);

    // ── Leaf-key tests ────────────────────────────────────────────────────────

    [Fact]
    public void Single_caliber_key_is_core()
    {
        IsCore(MakeRule("{\"caliber\": \"Caliber12g\"}")).Should().BeTrue();
    }

    [Fact]
    public void Single_hasAncestor_key_is_core()
    {
        IsCore(MakeRule("{\"hasAncestor\": \"Weapon\"}")).Should().BeTrue();
    }

    [Fact]
    public void Single_properties_key_is_core()
    {
        IsCore(MakeRule("{\"properties\": {\"BoltAction\": true}}")).Should().BeTrue();
    }

    [Fact]
    public void Single_nameContains_key_is_not_core()
    {
        IsCore(MakeRule("{\"nameContains\": \"pump\"}")).Should().BeFalse();
    }

    [Fact]
    public void Single_nameMatches_key_is_not_core()
    {
        IsCore(MakeRule("{\"nameMatches\": \"AKM\"}")).Should().BeFalse();
    }

    [Fact]
    public void Single_pathMatches_key_is_not_core()
    {
        IsCore(MakeRule("{\"pathMatches\": \"SniperRifle\"}")).Should().BeFalse();
    }

    [Fact]
    public void Single_descriptionMatches_key_is_not_core()
    {
        IsCore(MakeRule("{\"descriptionMatches\": \"bolt\"}")).Should().BeFalse();
    }

    [Fact]
    public void Empty_conditions_is_not_core()
    {
        IsCore(MakeRule("{}")).Should().BeFalse();
    }

    [Fact]
    public void Multiple_core_leaf_keys_at_top_level_is_core()
    {
        IsCore(MakeRule("{\"hasAncestor\": \"SniperRifle\", \"properties\": {\"BoltAction\": true}}"))
            .Should().BeTrue();
    }

    [Fact]
    public void Mixed_core_and_non_core_top_level_is_not_core()
    {
        IsCore(MakeRule("{\"hasAncestor\": \"Shotgun\", \"nameContains\": \"pump\"}"))
            .Should().BeFalse();
    }

    // ── Hazardous case: nested "and" ─────────────────────────────────────────

    /// <summary>
    /// Rule with { and: [{ caliber: ... }, { hasAncestor: ... }] } — all leaves are core,
    /// so the rule IS core (acceptance criterion from task spec).
    /// </summary>
    [Fact]
    public void Core_rule_with_nested_and_is_detected_as_core()
    {
        IsCore(MakeRule("{\"and\": [{\"caliber\": \"Caliber12g\"}, {\"hasAncestor\": \"Weapon\"}]}"))
            .Should().BeTrue();
    }

    [Fact]
    public void Nested_and_with_all_core_leaves_is_core()
    {
        IsCore(MakeRule(
            "{\"and\": [" +
            "  {\"hasAncestor\": \"SniperRifle\"}," +
            "  {\"properties\": {\"BoltAction\": true}}" +
            "]}")).Should().BeTrue();
    }

    // ── Hazardous case: nested "or" with non-core leaf ───────────────────────

    /// <summary>
    /// Rule with { or: [{ caliber: ... }, { nameContains: "X" }] } — one leaf is non-core,
    /// so the rule is NOT core (acceptance criterion from task spec).
    /// </summary>
    [Fact]
    public void Rule_with_or_containing_nameContains_is_not_core()
    {
        IsCore(MakeRule("{\"or\": [{\"caliber\": \"Caliber12g\"}, {\"nameContains\": \"X\"}]}"))
            .Should().BeFalse();
    }

    [Fact]
    public void Nested_or_with_all_core_leaves_is_core()
    {
        IsCore(MakeRule("{\"or\": [{\"caliber\": \"Caliber12g\"}, {\"hasAncestor\": \"Weapon\"}]}"))
            .Should().BeTrue();
    }

    // ── Nested "not" ─────────────────────────────────────────────────────────

    [Fact]
    public void Nested_not_with_core_inner_is_core()
    {
        IsCore(MakeRule("{\"not\": {\"hasAncestor\": \"SniperRifle\"}}")).Should().BeTrue();
    }

    [Fact]
    public void Nested_not_with_non_core_inner_is_not_core()
    {
        IsCore(MakeRule("{\"not\": {\"nameContains\": \"pump\"}}")).Should().BeFalse();
    }

    // ── Deep nesting ─────────────────────────────────────────────────────────

    [Fact]
    public void Deeply_nested_all_core_is_core()
    {
        // and: [ or: [ caliber, hasAncestor ], properties ]
        IsCore(MakeRule(
            "{\"and\": [" +
            "  {\"or\": [{\"caliber\": \"Caliber12g\"}, {\"hasAncestor\": \"Weapon\"}]}," +
            "  {\"properties\": {\"BoltAction\": true}}" +
            "]}")).Should().BeTrue();
    }

    [Fact]
    public void Deeply_nested_non_core_leaf_taints_whole_rule()
    {
        // and: [ or: [ caliber, nameContains ], properties ]
        // nameContains is non-core → whole rule is NOT core.
        IsCore(MakeRule(
            "{\"and\": [" +
            "  {\"or\": [{\"caliber\": \"Caliber12g\"}, {\"nameContains\": \"pump\"}]}," +
            "  {\"properties\": {\"BoltAction\": true}}" +
            "]}")).Should().BeFalse();
    }
}
