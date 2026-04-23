using AddMissingQuestRequirements.Pipeline.Shared;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Shared;

public class ConditionDiffTests
{
    // ── WeaponsChanged ───────────────────────────────────────────────────────

    [Fact]
    public void WeaponsChanged_false_when_same_set_same_order()
    {
        ConditionDiff.WeaponsChanged(["A", "B", "C"], ["A", "B", "C"]).Should().BeFalse();
    }

    [Fact]
    public void WeaponsChanged_false_when_same_set_reordered()
    {
        ConditionDiff.WeaponsChanged(["A", "B", "C"], ["C", "A", "B"]).Should().BeFalse();
    }

    [Fact]
    public void WeaponsChanged_true_when_id_added()
    {
        ConditionDiff.WeaponsChanged(["A", "B"], ["A", "B", "C"]).Should().BeTrue();
    }

    [Fact]
    public void WeaponsChanged_true_when_id_removed()
    {
        ConditionDiff.WeaponsChanged(["A", "B", "C"], ["A", "B"]).Should().BeTrue();
    }

    [Fact]
    public void WeaponsChanged_true_when_id_swapped_same_count()
    {
        ConditionDiff.WeaponsChanged(["A", "B", "C"], ["A", "B", "D"]).Should().BeTrue();
    }

    [Fact]
    public void WeaponsChanged_false_on_empty_empty()
    {
        ConditionDiff.WeaponsChanged([], []).Should().BeFalse();
    }

    // ── GroupsChanged ────────────────────────────────────────────────────────

    [Fact]
    public void GroupsChanged_false_when_empty_group_dropped()
    {
        ConditionDiff.GroupsChanged([[]], []).Should().BeFalse();
    }

    [Fact]
    public void GroupsChanged_false_when_identical_single_group()
    {
        ConditionDiff.GroupsChanged([["A", "B"]], [["A", "B"]]).Should().BeFalse();
    }

    [Fact]
    public void GroupsChanged_false_when_group_contents_reordered()
    {
        ConditionDiff.GroupsChanged([["A", "B"]], [["B", "A"]]).Should().BeFalse();
    }

    [Fact]
    public void GroupsChanged_false_when_group_order_reordered_across_groups()
    {
        ConditionDiff.GroupsChanged([["A"], ["B"]], [["B"], ["A"]]).Should().BeFalse();
    }

    [Fact]
    public void GroupsChanged_true_when_group_added()
    {
        ConditionDiff.GroupsChanged([["A"]], [["A"], ["B"]]).Should().BeTrue();
    }

    [Fact]
    public void GroupsChanged_true_when_id_in_group_changed()
    {
        ConditionDiff.GroupsChanged([["A", "B"]], [["A", "C"]]).Should().BeTrue();
    }
}
