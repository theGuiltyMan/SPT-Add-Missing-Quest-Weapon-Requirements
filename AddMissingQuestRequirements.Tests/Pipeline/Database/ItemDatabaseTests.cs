using System.Text.Json;
using AddMissingQuestRequirements.Pipeline.Database;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Database;

public class ItemDatabaseTests
{
    private static ItemNode MakeNode(string id, string name, string? parentId = null,
        string itemType = "Item", string? caliber = null, bool? boltAction = null)
    {
        var props = new Dictionary<string, JsonElement>();
        if (caliber is not null)
        {
            props["ammoCaliber"] = JsonDocument.Parse($"\"{caliber}\"").RootElement;
        }
        if (boltAction is not null)
        {
            props["BoltAction"] = JsonDocument.Parse(boltAction.Value ? "true" : "false").RootElement;
        }

        return new ItemNode
        {
            Id = id,
            Name = name,
            ParentId = parentId,
            NodeType = itemType,
            Props = props
        };
    }

    [Fact]
    public void InMemoryItemDatabase_returns_all_items()
    {
        var items = new[]
        {
            MakeNode("weapon_root", "Weapon", null, "Node"),
            MakeNode("ak74", "AKS-74U", "weapon_root", "Item", caliber: "Caliber545x39")
        };

        var db = new InMemoryItemDatabase(items);

        db.Items.Should().HaveCount(2);
        db.Items["ak74"].Name.Should().Be("AKS-74U");
        db.Items["ak74"].Props["ammoCaliber"].GetString().Should().Be("Caliber545x39");
    }

    [Fact]
    public void InMemoryItemDatabase_GetLocaleName_returns_locale_name()
    {
        var db = new InMemoryItemDatabase(
            [MakeNode("ak74", "AKS74U")],
            new Dictionary<string, string> { ["ak74"] = "AKS-74U 5.45x39 assault rifle" });

        db.GetLocaleName("ak74").Should().Be("AKS-74U 5.45x39 assault rifle");
    }

    [Fact]
    public void InMemoryItemDatabase_GetLocaleName_returns_null_for_unknown_id()
    {
        var db = new InMemoryItemDatabase([MakeNode("ak74", "AKS74U")]);
        db.GetLocaleName("unknown").Should().BeNull();
    }

    [Fact]
    public void ItemNode_Props_BoltAction_accessible()
    {
        var node = MakeNode("sv98", "SV-98", "sr_node", "Item", boltAction: true);
        node.Props["BoltAction"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void GetLocaleDescription_returns_value_when_present()
    {
        var db = new InMemoryItemDatabase(
            [new ItemNode { Id = "x", Name = "x", NodeType = "Item" }],
            localeDescriptions: new() { ["x"] = "Some description" });
        db.GetLocaleDescription("x").Should().Be("Some description");
    }

    [Fact]
    public void GetLocaleDescription_returns_null_when_absent()
    {
        var db = new InMemoryItemDatabase(
            [new ItemNode { Id = "x", Name = "x", NodeType = "Item" }]);
        db.GetLocaleDescription("x").Should().BeNull();
    }
}
