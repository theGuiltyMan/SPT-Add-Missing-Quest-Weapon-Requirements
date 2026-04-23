using System.Text.Json;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Rules.RuleConditions;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Rules.RuleConditions;

public class ConditionTests
{
    // ── Shared test database ─────────────────────────────────────────────────
    // root → weapon → sniper_rifle → sv98  (BoltAction=true, caliber=762x54R)
    //              → assault_rifle → ak74  (BoltAction=false, caliber=545x39)
    //              → pistol → revolver → rhino
    private static readonly IItemDatabase Db = new InMemoryItemDatabase(
    [
        new ItemNode { Id = "root",          Name = "Item",         ParentId = null,          NodeType = "Node" },
        new ItemNode { Id = "weapon",        Name = "Weapon",       ParentId = "root",         NodeType = "Node" },
        new ItemNode { Id = "sniper_rifle",  Name = "SniperRifle",  ParentId = "weapon",       NodeType = "Node" },
        new ItemNode { Id = "assault_rifle", Name = "AssaultRifle", ParentId = "weapon",       NodeType = "Node" },
        new ItemNode { Id = "pistol",        Name = "Pistol",       ParentId = "weapon",       NodeType = "Node" },
        new ItemNode { Id = "revolver",      Name = "Revolver",     ParentId = "pistol",       NodeType = "Node" },
        new ItemNode
        {
            Id = "sv98", Name = "sv98", ParentId = "sniper_rifle", NodeType = "Item",
            Props = new()
            {
                ["BoltAction"]   = Bool(true),
                ["ammoCaliber"]  = Str("Caliber762x54R")
            }
        },
        new ItemNode
        {
            Id = "ak74", Name = "ak74", ParentId = "assault_rifle", NodeType = "Item",
            Props = new()
            {
                ["BoltAction"]  = Bool(false),
                ["ammoCaliber"] = Str("Caliber545x39")
            }
        },
        new ItemNode { Id = "rhino", Name = "rhino", ParentId = "revolver", NodeType = "Item", Props = [] },
    ],
    localeNames: new()
    {
        ["sv98"]  = "SV-98 bolt-action sniper rifle",
        ["ak74"]  = "AKS-74U 5.45x39 assault rifle",
        ["rhino"] = "Chiappa Rhino 200DS revolver"
    });

    private static readonly AncestryResolver Resolver = new();

    // ── HasAncestorCondition ─────────────────────────────────────────────────

    [Fact]
    public void HasAncestor_true_when_ancestor_in_chain()
    {
        var cond = new HasAncestorCondition("SniperRifle");
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void HasAncestor_false_when_ancestor_not_in_chain()
    {
        var cond = new HasAncestorCondition("SniperRifle");
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse();
    }

    // ── PropertyCondition ────────────────────────────────────────────────────

    [Fact]
    public void Property_true_when_bool_matches()
    {
        var cond = new PropertiesCondition(Obj(("BoltAction", Bool(true))));
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void Property_false_when_bool_does_not_match()
    {
        var cond = new PropertiesCondition(Obj(("BoltAction", Bool(true))));
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void Property_false_when_prop_missing()
    {
        var cond = new PropertiesCondition(Obj(("BoltAction", Bool(true))));
        cond.Evaluate(Db.Items["rhino"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void Property_true_when_numeric_values_are_equal_despite_different_json_text()
    {
        // "2.50" and "2.5" are different raw text but the same number.
        // GetRawText() comparison would incorrectly return false.
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root", Name = "Item", ParentId = null, NodeType = "Node" },
            new ItemNode
            {
                Id = "x", Name = "x", ParentId = "root", NodeType = "Item",
                Props = new() { ["Weight"] = JsonDocument.Parse("2.50").RootElement }
            }
        ]);
        var cond = new PropertiesCondition(Obj(("Weight", JsonDocument.Parse("2.5").RootElement)));
        cond.Evaluate(db.Items["x"], db, new AncestryResolver()).Should().BeTrue();
    }

    // ── CaliberCondition ─────────────────────────────────────────────────────

    [Fact]
    public void Caliber_true_when_matches()
    {
        var cond = new CaliberCondition("Caliber762x54R");
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void Caliber_false_when_different_caliber()
    {
        var cond = new CaliberCondition("Caliber762x54R");
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void Caliber_false_when_prop_missing()
    {
        var cond = new CaliberCondition("Caliber762x54R");
        cond.Evaluate(Db.Items["rhino"], Db, Resolver).Should().BeFalse();
    }

    // ── NameContainsCondition ────────────────────────────────────────────────

    [Fact]
    public void NameContains_true_when_locale_name_contains_substring()
    {
        var cond = new NameContainsCondition("bolt-action");
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void NameContains_is_case_insensitive()
    {
        var cond = new NameContainsCondition("BOLT-ACTION");
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void NameContains_false_when_substring_absent()
    {
        var cond = new NameContainsCondition("bolt-action");
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void NameContains_false_when_no_locale_name()
    {
        var cond = new NameContainsCondition("something");
        var db = new InMemoryItemDatabase([new ItemNode { Id = "x", Name = "x", NodeType = "Item" }]);
        cond.Evaluate(db.Items["x"], db, new AncestryResolver()).Should().BeFalse();
    }

    // ── NameMatchesCondition ─────────────────────────────────────────────────

    [Fact]
    public void NameMatches_true_when_regex_matches_locale_name()
    {
        var cond = new NameMatchesCondition(@"sniper rifle");
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void NameMatches_false_when_regex_does_not_match()
    {
        var cond = new NameMatchesCondition(@"sniper rifle");
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse();
    }

    // ── PathMatchesCondition ─────────────────────────────────────────────────

    [Fact]
    public void PathMatches_true_when_path_regex_matches()
    {
        var cond = new PathMatchesCondition(@"Weapon/SniperRifle/");
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void PathMatches_false_when_path_regex_does_not_match()
    {
        var cond = new PathMatchesCondition(@"Weapon/SniperRifle/");
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse();
    }

    // ── AndCondition ────────────────────────────────────────────────────────────

    [Fact]
    public void And_passes_when_all_inner_conditions_pass()
    {
        var cond = new AndCondition([
            new CaliberCondition("Caliber762x54R"),
            new HasAncestorCondition("SniperRifle")
        ]);
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void And_fails_when_any_inner_condition_fails()
    {
        var cond = new AndCondition([
            new CaliberCondition("Caliber762x54R"),
            new HasAncestorCondition("AssaultRifle") // sv98 is NOT an assault rifle
        ]);
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void And_passes_with_single_inner_condition()
    {
        var cond = new AndCondition([new CaliberCondition("Caliber762x54R")]);
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    // ── OrCondition ─────────────────────────────────────────────────────────────

    [Fact]
    public void Or_passes_when_any_inner_condition_passes()
    {
        var cond = new OrCondition([
            new HasAncestorCondition("SniperRifle"),
            new HasAncestorCondition("AssaultRifle")
        ]);
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void Or_fails_when_all_inner_conditions_fail()
    {
        var cond = new OrCondition([
            new HasAncestorCondition("Pistol"),
            new HasAncestorCondition("AssaultRifle")
        ]);
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeFalse();
    }

    // ── NotCondition ─────────────────────────────────────────────────────────────

    [Fact]
    public void Not_passes_when_inner_condition_fails()
    {
        var cond = new NotCondition(new HasAncestorCondition("AssaultRifle"));
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void Not_fails_when_inner_condition_passes()
    {
        var cond = new NotCondition(new HasAncestorCondition("SniperRifle"));
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void Not_with_nested_Or_passes_when_none_of_the_or_branches_match()
    {
        // "not: { or: [pistol, assaultRifle] }" should pass for sv98 (sniper rifle)
        var cond = new NotCondition(new OrCondition([
            new HasAncestorCondition("Pistol"),
            new HasAncestorCondition("AssaultRifle")
        ]));
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
    }

    [Fact]
    public void Not_with_nested_Or_fails_when_one_branch_matches()
    {
        var cond = new NotCondition(new OrCondition([
            new HasAncestorCondition("Pistol"),
            new HasAncestorCondition("SniperRifle")
        ]));
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void Not_with_multi_key_inner_uses_implicit_And_semantics()
    {
        // Inner: caliber=762x54R AND hasAncestor=SniperRifle — both true for sv98
        // So Not → false for sv98
        var inner = new AndCondition([
            new CaliberCondition("Caliber762x54R"),
            new HasAncestorCondition("SniperRifle")
        ]);
        var cond = new NotCondition(inner);
        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeFalse();
    }

    // ── DescriptionMatchesCondition ──────────────────────────────────────────────

    private static readonly IItemDatabase DbWithDescriptions = new InMemoryItemDatabase(
    [
        new ItemNode { Id = "root",   Name = "Item",   ParentId = null,     NodeType = "Node" },
        new ItemNode { Id = "weapon", Name = "Weapon", ParentId = "root",   NodeType = "Node" },
        new ItemNode { Id = "ar15",   Name = "ar15",   ParentId = "weapon", NodeType = "Item", Props = [] },
        new ItemNode { Id = "nodesc", Name = "nodesc", ParentId = "weapon", NodeType = "Item", Props = [] },
    ],
    localeNames: new() { ["ar15"] = "AR-15 assault rifle" },
    localeDescriptions: new() { ["ar15"] = "A carbine based on the AR-15 platform" });

    [Fact]
    public void DescriptionMatches_passes_when_description_matches_regex()
    {
        var cond = new DescriptionMatchesCondition(@"AR-15 platform");
        cond.Evaluate(DbWithDescriptions.Items["ar15"], DbWithDescriptions, Resolver).Should().BeTrue();
    }

    [Fact]
    public void DescriptionMatches_is_case_insensitive()
    {
        var cond = new DescriptionMatchesCondition(@"ar-15 platform");
        cond.Evaluate(DbWithDescriptions.Items["ar15"], DbWithDescriptions, Resolver).Should().BeTrue();
    }

    [Fact]
    public void DescriptionMatches_fails_when_no_match()
    {
        var cond = new DescriptionMatchesCondition(@"sniper");
        cond.Evaluate(DbWithDescriptions.Items["ar15"], DbWithDescriptions, Resolver).Should().BeFalse();
    }

    [Fact]
    public void DescriptionMatches_fails_when_description_is_null()
    {
        var cond = new DescriptionMatchesCondition(@"something");
        cond.Evaluate(DbWithDescriptions.Items["nodesc"], DbWithDescriptions, Resolver).Should().BeFalse();
    }

    // ── ConditionFactory meta-condition round-trips ──────────────────────────────

    [Fact]
    public void ConditionFactory_and_array_evaluates_correctly()
    {
        var json = """
            { "and": [{ "caliber": "Caliber762x54R" }, { "hasAncestor": "SniperRifle" }] }
            """;
        var root = JsonDocument.Parse(json).RootElement;
        var prop = root.EnumerateObject().First();
        var cond = ConditionFactory.Create(prop.Name, prop.Value);

        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void ConditionFactory_or_array_evaluates_correctly()
    {
        var json = """
            { "or": [{ "hasAncestor": "SniperRifle" }, { "hasAncestor": "AssaultRifle" }] }
            """;
        var root = JsonDocument.Parse(json).RootElement;
        var prop = root.EnumerateObject().First();
        var cond = ConditionFactory.Create(prop.Name, prop.Value);

        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeTrue();
        cond.Evaluate(Db.Items["rhino"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void ConditionFactory_not_object_evaluates_correctly()
    {
        var json = """{ "not": { "hasAncestor": "AssaultRifle" } }""";
        var root = JsonDocument.Parse(json).RootElement;
        var prop = root.EnumerateObject().First();
        var cond = ConditionFactory.Create(prop.Name, prop.Value);

        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue();
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse();
    }

    [Fact]
    public void ConditionFactory_not_with_nested_or_evaluates_correctly()
    {
        var json = """
            { "not": { "or": [{ "hasAncestor": "Pistol" }, { "hasAncestor": "AssaultRifle" }] } }
            """;
        var root = JsonDocument.Parse(json).RootElement;
        var prop = root.EnumerateObject().First();
        var cond = ConditionFactory.Create(prop.Name, prop.Value);

        cond.Evaluate(Db.Items["sv98"], Db, Resolver).Should().BeTrue(); // sniper rifle — not pistol/AR
        cond.Evaluate(Db.Items["ak74"], Db, Resolver).Should().BeFalse(); // assault rifle — matched by or
    }

    [Fact]
    public void ConditionFactory_not_with_empty_object_throws()
    {
        var json = """{ "not": {} }""";
        var root = JsonDocument.Parse(json).RootElement;
        var prop = root.EnumerateObject().First();

        var act = () => ConditionFactory.Create(prop.Name, prop.Value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one property*");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JsonElement Str(string v) =>
        JsonDocument.Parse($"\"{v}\"").RootElement;

    private static JsonElement Bool(bool v) =>
        JsonDocument.Parse(v ? "true" : "false").RootElement;

    private static JsonElement Obj(params (string key, JsonElement value)[] props)
    {
        var dict = props.ToDictionary(p => p.key, p => p.value);
        var json = JsonSerializer.Serialize(dict);
        return JsonDocument.Parse(json).RootElement;
    }
}
