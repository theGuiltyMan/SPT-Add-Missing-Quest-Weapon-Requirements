using AddMissingQuestRequirements.Reporting;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Reporting;

public class QuestStatusTests
{
    [Fact]
    public void Blacklisted_beats_everything_else()
    {
        ReportBuilder.ClassifyStatus(blacklisted: true, eligibleConditionCount: 3, allConditionsNoop: false)
            .Should().Be(QuestStatus.Blacklisted);
    }

    [Fact]
    public void Blacklisted_beats_no_eligible_conditions()
    {
        ReportBuilder.ClassifyStatus(blacklisted: true, eligibleConditionCount: 0, allConditionsNoop: true)
            .Should().Be(QuestStatus.Blacklisted);
    }

    [Fact]
    public void NoEligibleConditions_when_filtered_list_is_empty()
    {
        ReportBuilder.ClassifyStatus(blacklisted: false, eligibleConditionCount: 0, allConditionsNoop: true)
            .Should().Be(QuestStatus.NoEligibleConditions);
    }

    [Fact]
    public void Noop_when_every_condition_is_noop()
    {
        ReportBuilder.ClassifyStatus(blacklisted: false, eligibleConditionCount: 2, allConditionsNoop: true)
            .Should().Be(QuestStatus.Noop);
    }

    [Fact]
    public void Expanded_when_any_condition_changed()
    {
        ReportBuilder.ClassifyStatus(blacklisted: false, eligibleConditionCount: 2, allConditionsNoop: false)
            .Should().Be(QuestStatus.Expanded);
    }
}
