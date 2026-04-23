using System.Text.Json;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Weapon;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Weapon;

public class DefaultWeaponRuleFactoryTests
{
    private static InMemoryItemDatabase BuildDb()
    {
        // Tree:
        //   root (Node)
        //   ├── Weapon (Node)
        //   │   └── AssaultRifle (Node)
        //   │       └── m4a1 (Item)  — subtree: directChildOf:Weapon fires
        //   ├── Knife (Node)
        //   │   └── bayonet (Item)   — no subtree: literal "Knife"
        //   └── Lonely (Node)         — no descendant items: literal "Lonely"
        var items = new Dictionary<string, ItemNode>
        {
            ["root"]     = new() { Id = "root",     Name = "Item",         NodeType = "Node" },
            ["weap"]     = new() { Id = "weap",     Name = "Weapon",       ParentId = "root", NodeType = "Node" },
            ["ar"]       = new() { Id = "ar",       Name = "AssaultRifle", ParentId = "weap", NodeType = "Node" },
            ["m4a1"]     = new() { Id = "m4a1",     Name = "M4A1",         ParentId = "ar",   NodeType = "Item" },
            ["knife"]    = new() { Id = "knife",    Name = "Knife",        ParentId = "root", NodeType = "Node" },
            ["bayonet"]  = new() { Id = "bayonet",  Name = "Bayonet",      ParentId = "knife", NodeType = "Item" },
            ["lonely"]   = new() { Id = "lonely",   Name = "Lonely",       ParentId = "root", NodeType = "Node" },
        };
        return InMemoryItemDatabase.FromItemsOnly(items);
    }

    [Fact]
    public void WeaponAncestor_WithSubtree_EmitsDirectChildOfTemplate()
    {
        var db = BuildDb();
        var rules = DefaultWeaponRuleFactory.Build(db, ["Weapon"]);
        rules.Should().ContainSingle();
        rules[0].Type.Should().Be("{directChildOf:Weapon}");
        rules[0].Conditions["hasAncestor"].GetString().Should().Be("Weapon");
    }

    [Fact]
    public void KnifeAncestor_DirectLeavesOnly_EmitsLiteralType()
    {
        var db = BuildDb();
        var rules = DefaultWeaponRuleFactory.Build(db, ["Knife"]);
        rules.Should().ContainSingle();
        rules[0].Type.Should().Be("Knife");
        rules[0].Conditions["hasAncestor"].GetString().Should().Be("Knife");
    }

    [Fact]
    public void LonelyAncestor_NoDescendantItems_EmitsLiteralType()
    {
        var db = BuildDb();
        var rules = DefaultWeaponRuleFactory.Build(db, ["Lonely"]);
        rules.Should().ContainSingle();
        rules[0].Type.Should().Be("Lonely");
    }

    [Fact]
    public void MultipleAncestors_EmitOneRuleEachInInputOrder()
    {
        var db = BuildDb();
        var rules = DefaultWeaponRuleFactory.Build(db, ["Knife", "Weapon", "Lonely"]);
        rules.Should().HaveCount(3);
        rules[0].Type.Should().Be("Knife");
        rules[1].Type.Should().Be("{directChildOf:Weapon}");
        rules[2].Type.Should().Be("Lonely");
    }

    [Fact]
    public void EmptyAncestors_ReturnsEmpty()
    {
        var db = BuildDb();
        DefaultWeaponRuleFactory.Build(db, []).Should().BeEmpty();
    }
}
