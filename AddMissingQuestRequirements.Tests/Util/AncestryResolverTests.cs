using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Util;

public class AncestryResolverTests
{
    // Tree: root → weapon → assault_rifle → ak74
    private static IItemDatabase MakeDb() => new InMemoryItemDatabase(
    [
        new ItemNode { Id = "root",         Name = "Item",         ParentId = null,           NodeType = "Node" },
        new ItemNode { Id = "weapon",       Name = "Weapon",       ParentId = "root",          NodeType = "Node" },
        new ItemNode { Id = "assault_rifle",Name = "AssaultRifle", ParentId = "weapon",        NodeType = "Node" },
        new ItemNode { Id = "ak74",         Name = "AKS74U",       ParentId = "assault_rifle", NodeType = "Item" },
        new ItemNode { Id = "orphan",       Name = "Orphan",       ParentId = "missing_parent",NodeType = "Item" },
    ]);

    [Fact]
    public void GetAncestors_returns_chain_from_parent_to_root()
    {
        var db = MakeDb();
        var resolver = new AncestryResolver();

        var ancestors = resolver.GetAncestors("ak74", db);

        ancestors.Select(a => a.Name).Should()
            .ContainInOrder("AssaultRifle", "Weapon", "Item");
    }

    [Fact]
    public void GetAncestors_root_node_returns_empty()
    {
        var resolver = new AncestryResolver();
        var ancestors = resolver.GetAncestors("root", MakeDb());
        ancestors.Should().BeEmpty();
    }

    [Fact]
    public void GetAncestors_missing_parent_stops_chain()
    {
        var resolver = new AncestryResolver();
        // "orphan" has ParentId "missing_parent" which doesn't exist in the db
        var ancestors = resolver.GetAncestors("orphan", MakeDb());
        ancestors.Should().BeEmpty(); // parent not found — stop cleanly
    }

    [Fact]
    public void HasAncestorWithName_true_for_ancestor_in_chain()
    {
        var resolver = new AncestryResolver();
        resolver.HasAncestorWithName("ak74", "Weapon", MakeDb()).Should().BeTrue();
        resolver.HasAncestorWithName("ak74", "AssaultRifle", MakeDb()).Should().BeTrue();
    }

    [Fact]
    public void HasAncestorWithName_false_when_not_in_chain()
    {
        var resolver = new AncestryResolver();
        resolver.HasAncestorWithName("ak74", "SniperRifle", MakeDb()).Should().BeFalse();
    }

    [Fact]
    public void GetAncestorPath_returns_slash_separated_names_from_root()
    {
        var resolver = new AncestryResolver();
        var path = resolver.GetAncestorPath("ak74", MakeDb());
        path.Should().Be("Item/Weapon/AssaultRifle/AKS74U");
    }

    [Fact]
    public void GetDirectChildOf_returns_name_of_child_directly_below_target()
    {
        var resolver = new AncestryResolver();
        // direct child of "Weapon" in ak74's chain is "AssaultRifle"
        var child = resolver.GetDirectChildOf("Weapon", "ak74", MakeDb());
        child.Should().Be("AssaultRifle");
    }

    [Fact]
    public void GetDirectChildOf_returns_null_when_target_not_in_chain()
    {
        var resolver = new AncestryResolver();
        var child = resolver.GetDirectChildOf("SniperRifle", "ak74", MakeDb());
        child.Should().BeNull();
    }

    [Fact]
    public void GetAncestors_detects_cycle_and_stops()
    {
        // A→B, B→A (cycle)
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "A", Name = "A", ParentId = "B", NodeType = "Item" },
            new ItemNode { Id = "B", Name = "B", ParentId = "A", NodeType = "Node" },
        ]);
        var resolver = new AncestryResolver();

        var act = () => resolver.GetAncestors("A", db);

        // Must not throw or hang — cycle detection stops the walk
        act.Should().NotThrow();
    }
}
