using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Rules;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Rules;

public class RuleEngineTests
{
    // root → weapon → sniper_rifle → sv98 (BoltAction=true)
    //              → assault_rifle → ak74
    //              → pistol → revolver → rhino
    private static readonly IItemDatabase Db = new InMemoryItemDatabase(
    [
        new ItemNode { Id = "root",          Name = "Item",         ParentId = null,           NodeType = "Node" },
        new ItemNode { Id = "weapon",        Name = "Weapon",       ParentId = "root",          NodeType = "Node" },
        new ItemNode { Id = "sniper_rifle",  Name = "SniperRifle",  ParentId = "weapon",        NodeType = "Node" },
        new ItemNode { Id = "assault_rifle", Name = "AssaultRifle", ParentId = "weapon",        NodeType = "Node" },
        new ItemNode { Id = "pistol",        Name = "Pistol",       ParentId = "weapon",        NodeType = "Node" },
        new ItemNode { Id = "revolver",      Name = "Revolver",     ParentId = "pistol",        NodeType = "Node" },
        new ItemNode
        {
            Id = "sv98", Name = "sv98", ParentId = "sniper_rifle", NodeType = "Item",
            Props = new() { ["BoltAction"] = JsonDocument.Parse("true").RootElement }
        },
        new ItemNode { Id = "ak74",  Name = "ak74",  ParentId = "assault_rifle", NodeType = "Item", Props = [] },
        new ItemNode { Id = "rhino", Name = "rhino", ParentId = "revolver",      NodeType = "Item", Props = [] },
    ]);

    private static TypeRule MakeRule(string type, string[]? alsoAs = null,
        string? hasAncestor = null, bool? boltAction = null)
    {
        var conditions = new Dictionary<string, JsonElement>();

        if (hasAncestor is not null)
        {
            conditions["hasAncestor"] = JsonDocument.Parse($"\"{hasAncestor}\"").RootElement;
        }

        if (boltAction is not null)
        {
            var obj = $"{{\"BoltAction\":{(boltAction.Value ? "true" : "false")}}}";
            conditions["properties"] = JsonDocument.Parse(obj).RootElement;
        }

        return new TypeRule { Type = type, Conditions = conditions, AlsoAs = [..alsoAs ?? []] };
    }

    [Fact]
    public void All_matching_rules_contribute_types()
    {
        // Both rules match sv98 — both should fire
        var rules = new[]
        {
            MakeRule("BoltActionSniperRifle", hasAncestor: "SniperRifle", boltAction: true),
            MakeRule("SniperRifle",           hasAncestor: "SniperRifle"),
        };
        var engine = new RuleEngine(rules, Db);

        var matches = engine.EvaluateAll(Db.Items["sv98"]);

        matches.Select(m => m.Type).Should().BeEquivalentTo(["BoltActionSniperRifle", "SniperRifle"]);
    }

    [Fact]
    public void Specific_and_catchall_both_fire_for_same_item()
    {
        var rules = new[]
        {
            MakeRule("BoltActionSniperRifle", hasAncestor: "SniperRifle", boltAction: true),
            MakeRule("CatchAll",              hasAncestor: "Weapon"),
        };
        var engine = new RuleEngine(rules, Db);

        var matches = engine.EvaluateAll(Db.Items["sv98"]);

        matches.Select(m => m.Type).Should().BeEquivalentTo(["BoltActionSniperRifle", "CatchAll"]);
    }

    [Fact]
    public void Only_matching_rules_fire()
    {
        var rules = new[]
        {
            MakeRule("BoltActionSniperRifle", hasAncestor: "SniperRifle", boltAction: true),
            MakeRule("CatchAll",              hasAncestor: "Weapon"),
        };
        var engine = new RuleEngine(rules, Db);

        var matches = engine.EvaluateAll(Db.Items["ak74"]);

        // ak74 has no BoltAction, so only CatchAll fires
        matches.Select(m => m.Type).Should().BeEquivalentTo(["CatchAll"]);
    }

    [Fact]
    public void Returns_empty_when_no_rule_matches()
    {
        var engine = new RuleEngine([], Db);
        engine.EvaluateAll(Db.Items["ak74"]).Should().BeEmpty();
    }

    [Fact]
    public void AlsoAs_is_returned_with_match()
    {
        var rules = new[]
        {
            MakeRule("Revolver", alsoAs: ["Pistol"], hasAncestor: "Revolver"),
        };
        var engine = new RuleEngine(rules, Db);

        var matches = engine.EvaluateAll(Db.Items["rhino"]);

        matches.Should().HaveCount(1);
        matches[0].Type.Should().Be("Revolver");
        matches[0].AlsoAs.Should().BeEquivalentTo(["Pistol"]);
    }

    [Fact]
    public void DirectChildOf_template_resolves_to_direct_child_name()
    {
        // {directChildOf:Weapon} for sv98 → "SniperRifle"
        var conditions = new Dictionary<string, JsonElement>
        {
            ["hasAncestor"] = JsonDocument.Parse("\"Weapon\"").RootElement
        };
        var rule = new TypeRule { Type = "{directChildOf:Weapon}", Conditions = conditions, AlsoAs = [] };
        var engine = new RuleEngine([rule], Db);

        var matches = engine.EvaluateAll(Db.Items["sv98"]);

        matches.Should().HaveCount(1);
        matches[0].Type.Should().Be("SniperRifle");
    }

    [Fact]
    public void DirectChildOf_template_resolves_differently_per_item()
    {
        var conditions = new Dictionary<string, JsonElement>
        {
            ["hasAncestor"] = JsonDocument.Parse("\"Weapon\"").RootElement
        };
        var rule = new TypeRule { Type = "{directChildOf:Weapon}", Conditions = conditions, AlsoAs = [] };
        var engine = new RuleEngine([rule], Db);

        engine.EvaluateAll(Db.Items["sv98"])[0].Type.Should().Be("SniperRifle");
        engine.EvaluateAll(Db.Items["ak74"])[0].Type.Should().Be("AssaultRifle");
        engine.EvaluateAll(Db.Items["rhino"])[0].Type.Should().Be("Pistol");
    }
}
