using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using FluentAssertions;
using Xunit;

namespace AddMissingQuestRequirements.Tests.Pipeline.Quest;

public sealed class QuestOverrideResolverTests
{
    private static OverriddenSettings BuildSettings(params QuestOverrideEntry[] entries)
    {
        var dict = new Dictionary<string, List<QuestOverrideEntry>>();
        foreach (var e in entries)
        {
            if (!dict.TryGetValue(e.Id, out var list))
            {
                list = [];
                dict[e.Id] = list;
            }
            list.Add(e);
        }
        return new OverriddenSettings
        {
            QuestOverrides = dict,
        };
    }

    private static ConditionNode Cond(string id, string parentId) => new()
    {
        Id = id,
        ParentConditionId = parentId,
        ConditionType = "CounterCreator",
    };

    [Fact]
    public void Resolve_returns_null_when_quest_unknown()
    {
        var settings = BuildSettings();
        QuestOverrideResolver.Resolve(settings, "q-missing", Cond("sub", "outer")).Should().BeNull();
    }

    [Fact]
    public void Resolve_matches_sub_condition_id()
    {
        var entry = new QuestOverrideEntry { Id = "q1", Conditions = ["sub-1"] };
        var settings = BuildSettings(entry);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeSameAs(entry);
    }

    [Fact]
    public void Resolve_matches_parent_condition_id()
    {
        var entry = new QuestOverrideEntry { Id = "q1", Conditions = ["outer-1"] };
        var settings = BuildSettings(entry);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeSameAs(entry);
    }

    [Fact]
    public void Resolve_prefers_specific_match_over_generic()
    {
        var generic  = new QuestOverrideEntry { Id = "q1", Conditions = [] };
        var specific = new QuestOverrideEntry { Id = "q1", Conditions = ["outer-1"] };
        var settings = BuildSettings(generic, specific);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeSameAs(specific);
    }

    [Fact]
    public void Resolve_falls_back_to_generic_when_no_specific_match()
    {
        var generic  = new QuestOverrideEntry { Id = "q1", Conditions = [] };
        var specific = new QuestOverrideEntry { Id = "q1", Conditions = ["other-id"] };
        var settings = BuildSettings(specific, generic);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeSameAs(generic);
    }

    [Fact]
    public void Resolve_returns_null_when_specific_misses_and_no_generic()
    {
        var entry = new QuestOverrideEntry { Id = "q1", Conditions = ["other-id"] };
        var settings = BuildSettings(entry);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeNull();
    }
}
