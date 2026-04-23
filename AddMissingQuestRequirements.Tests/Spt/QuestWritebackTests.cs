using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Spt;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Spt;

public class QuestWritebackTests
{
    private sealed class FakeTarget : IConditionWriteTarget
    {
        public HashSet<string>? Weapon { get; set; }
        public IEnumerable<List<string>>? WeaponModsInclusive { get; set; }
        public IEnumerable<List<string>>? WeaponModsExclusive { get; set; }
    }

    [Fact]
    public void WriteBack_copies_weapon_set()
    {
        var patched = new ConditionNode { Id = "c1", Weapon = ["w1", "w2", "w3"] };
        var target = new FakeTarget { Weapon = ["old"] };

        QuestWriteback.WriteBack([(patched, target)]);

        target.Weapon.Should().NotBeNull();
        target.Weapon!.Should().BeEquivalentTo(new[] { "w1", "w2", "w3" });
    }

    [Fact]
    public void WriteBack_copies_inclusive_groups_in_order()
    {
        var patched = new ConditionNode
        {
            Id = "c1",
            WeaponModsInclusive = [["a"], ["b", "c"]],
        };
        var target = new FakeTarget();

        QuestWriteback.WriteBack([(patched, target)]);

        target.WeaponModsInclusive.Should().NotBeNull();
        var groups = target.WeaponModsInclusive!.ToList();
        groups.Should().HaveCount(2);
        groups[0].Should().BeEquivalentTo(new[] { "a" });
        groups[1].Should().BeEquivalentTo(new[] { "b", "c" });
    }

    [Fact]
    public void WriteBack_copies_exclusive_groups_in_order()
    {
        var patched = new ConditionNode
        {
            Id = "c1",
            WeaponModsExclusive = [["x"], ["y", "z"]],
        };
        var target = new FakeTarget();

        QuestWriteback.WriteBack([(patched, target)]);

        target.WeaponModsExclusive.Should().NotBeNull();
        var groups = target.WeaponModsExclusive!.ToList();
        groups.Should().HaveCount(2);
        groups[0].Should().BeEquivalentTo(new[] { "x" });
        groups[1].Should().BeEquivalentTo(new[] { "y", "z" });
    }

    [Fact]
    public void WriteBack_is_defensive_mutation_of_source_does_not_leak_to_target()
    {
        var patched = new ConditionNode
        {
            Id = "c1",
            Weapon = ["w1"],
            WeaponModsInclusive = [["a"]],
        };
        var target = new FakeTarget();

        QuestWriteback.WriteBack([(patched, target)]);

        // Mutate the source after WriteBack
        patched.Weapon.Add("leak");
        patched.WeaponModsInclusive[0].Add("leak-group");

        target.Weapon.Should().NotContain(
            "leak",
            "WriteBack must defensively copy Weapon into a new HashSet");

        var firstIncGroup = target.WeaponModsInclusive!.First();
        firstIncGroup.Should().NotContain(
            "leak-group",
            "WriteBack must defensively copy each inclusive group into a new List");
    }
}
