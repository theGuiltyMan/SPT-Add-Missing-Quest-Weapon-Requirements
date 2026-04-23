using AddMissingQuestRequirements.Pipeline.Quest;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Quest;

public class QuestDatabaseTests
{
    [Fact]
    public void InMemoryQuestDatabase_stores_quests_keyed_by_id()
    {
        var quests = new[]
        {
            new QuestNode { Id = "quest1", Conditions = [] },
            new QuestNode { Id = "quest2", Conditions = [] },
        };
        var db = new InMemoryQuestDatabase(quests);

        db.Quests.Should().ContainKey("quest1");
        db.Quests.Should().ContainKey("quest2");
        db.Quests["quest1"].Id.Should().Be("quest1");
    }

    /// <summary>
    /// Asserts the contract that QuestPatcher expanders require: the Weapon list must be mutable
    /// so expanders can modify it in-place. This test will catch any change that makes it read-only.
    /// </summary>
    [Fact]
    public void ConditionNode_weapon_field_is_mutable_list()
    {
        var condition = new ConditionNode
        {
            Id            = "cond1",
            ConditionType = "CounterCreator",
            Weapon        = ["wpn1", "wpn2"],
        };

        condition.Weapon.Should().BeEquivalentTo(["wpn1", "wpn2"]);
        condition.Weapon.Add("wpn3");
        condition.Weapon.Should().HaveCount(3);
    }

    [Fact]
    public void ConditionNode_caliber_and_mods_fields_are_accessible()
    {
        var condition = new ConditionNode
        {
            Id                  = "cond1",
            ConditionType       = "CounterCreator",
            WeaponCaliber       = ["Caliber556x45NATO"],
            WeaponModsInclusive = [["mod_a", "mod_b"], ["mod_c"]],
            WeaponModsExclusive = [["mod_x"]],
        };

        condition.WeaponCaliber.Should().BeEquivalentTo(["Caliber556x45NATO"]);
        condition.WeaponModsInclusive.Should().HaveCount(2);
        condition.WeaponModsInclusive[0].Should().BeEquivalentTo(["mod_a", "mod_b"]);
        condition.WeaponModsExclusive[0].Should().BeEquivalentTo(["mod_x"]);
    }

    [Fact]
    public void QuestNode_conditions_list_contains_conditions()
    {
        var quest = new QuestNode
        {
            Id = "quest1",
            Conditions =
            [
                new ConditionNode { Id = "cond_a", ConditionType = "CounterCreator" },
                new ConditionNode { Id = "cond_b", ConditionType = "Elimination" },
            ],
        };

        quest.Conditions.Should().HaveCount(2);
        quest.Conditions[0].Id.Should().Be("cond_a");
    }
}
