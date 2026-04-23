using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Rules;
using AddMissingQuestRequirements.Pipeline.Shared;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Shared;

public class CategorizerCoreTests
{
    private static InMemoryItemDatabase MakeDb()
    {
        var items = new List<ItemNode>
        {
            new() { Id = "node_weapon", Name = "Weapon", NodeType = "Node" },
            new() { Id = "leaf_a", Name = "a", ParentId = "node_weapon", NodeType = "Item" },
            new() { Id = "leaf_b", Name = "b", ParentId = "node_weapon", NodeType = "Item" },
            new() { Id = "leaf_c", Name = "c", ParentId = "node_weapon", NodeType = "Item" },
        };
        var locale = new Dictionary<string, string>
        {
            ["leaf_a"] = "AKM Rifle",
            ["leaf_b"] = "AKM Rifle",
            ["leaf_c"] = "Glock Pistol",
        };
        return new InMemoryItemDatabase(items, localeNames: locale);
    }

    private static TypeRule Rule(string type, string hasAncestor)
    {
        return new TypeRule
        {
            Conditions = new Dictionary<string, JsonElement>
            {
                ["hasAncestor"] = JsonSerializer.SerializeToElement(hasAncestor)
            },
            Type = type
        };
    }

    private static CategorizerInput Input(
        Dictionary<string, string>? manual = null,
        Dictionary<string, HashSet<string>>? canBeUsedAsSeeds = null,
        IReadOnlyList<string>? stripWords = null,
        IReadOnlyList<string>? excludeIds = null)
    {
        return new CategorizerInput(
            manual ?? [],
            canBeUsedAsSeeds ?? [],
            match => [match.Type, ..match.AlsoAs],
            stripWords ?? [],
            excludeIds ?? []);
    }

    [Fact]
    public void Empty_input_produces_empty_maps()
    {
        var db = MakeDb();
        var engine = new RuleEngine([], db);

        var (itemToType, typeToItems, canBeUsedAs) =
            CategorizerCore.Categorize(db, [], engine, Input());

        itemToType.Should().BeEmpty();
        typeToItems.Should().BeEmpty();
        canBeUsedAs.Should().BeEmpty();
    }

    [Fact]
    public void Single_rule_match_populates_maps()
    {
        var db = MakeDb();
        var engine = new RuleEngine([Rule("Rifle", "Weapon")], db);
        var leaves = db.Items.Values.Where(i => i.NodeType == "Item");

        var (itemToType, typeToItems, _) =
            CategorizerCore.Categorize(db, leaves, engine, Input());

        itemToType["leaf_a"].Should().Contain("Rifle");
        typeToItems["Rifle"].Should().Contain(["leaf_a", "leaf_b", "leaf_c"]);
    }

    [Fact]
    public void Manual_override_takes_precedence_over_rules()
    {
        var db = MakeDb();
        var engine = new RuleEngine([Rule("Rifle", "Weapon")], db);
        var leaves = db.Items.Values.Where(i => i.NodeType == "Item");

        var (itemToType, _, _) = CategorizerCore.Categorize(
            db, leaves, engine,
            Input(manual: new Dictionary<string, string> { ["leaf_c"] = "Custom" }));

        itemToType["leaf_c"].Should().Contain("Custom");
        // Core rule (hasAncestor) still merges in — confirm (see CategorizationHelper.BuildTypeMaps)
        itemToType["leaf_c"].Should().Contain("Rifle");
    }

    [Fact]
    public void Short_name_aliases_cross_link_pairs_with_same_normalized_name()
    {
        var db = MakeDb();
        var engine = new RuleEngine([Rule("Rifle", "Weapon")], db);
        var leaves = db.Items.Values.Where(i => i.NodeType == "Item");

        var (_, _, canBeUsedAs) =
            CategorizerCore.Categorize(db, leaves, engine, Input());

        canBeUsedAs["leaf_a"].Should().Contain("leaf_b");
        canBeUsedAs["leaf_b"].Should().Contain("leaf_a");
        canBeUsedAs.Should().NotContainKey("leaf_c");
    }

    [Fact]
    public void AliasExcludeIds_skips_listed_id_from_auto_aliasing()
    {
        var db = MakeDb();
        var engine = new RuleEngine([Rule("Rifle", "Weapon")], db);
        var leaves = db.Items.Values.Where(i => i.NodeType == "Item");

        var (_, _, canBeUsedAs) = CategorizerCore.Categorize(
            db, leaves, engine,
            Input(excludeIds: ["leaf_a"]));

        canBeUsedAs.Should().NotContainKey("leaf_a");
        (canBeUsedAs.TryGetValue("leaf_b", out var bLinks) ? bLinks : [])
            .Should().NotContain("leaf_a");
    }

    [Fact]
    public void Transitive_closure_links_three_items_via_chain()
    {
        var db = MakeDb();
        var engine = new RuleEngine([Rule("Rifle", "Weapon")], db);
        var leaves = db.Items.Values.Where(i => i.NodeType == "Item");

        var seeds = new Dictionary<string, HashSet<string>>
        {
            ["leaf_a"] = ["leaf_b"],
            ["leaf_b"] = ["leaf_c"],
        };

        // excludeIds suppresses the auto short-name linker so the assertion
        // proves the transitive closure is doing the work, not the linker.
        var (_, _, canBeUsedAs) = CategorizerCore.Categorize(
            db, leaves, engine,
            Input(canBeUsedAsSeeds: seeds, excludeIds: ["leaf_a", "leaf_b", "leaf_c"]));

        canBeUsedAs["leaf_a"].Should().Contain("leaf_c");
        canBeUsedAs["leaf_c"].Should().Contain("leaf_a");
    }
}
