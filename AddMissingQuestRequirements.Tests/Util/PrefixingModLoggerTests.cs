using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Util;

public class PrefixingModLoggerTests
{
    [Fact]
    public void Prepends_prefix_to_every_level()
    {
        var inner = new CapturingModLogger();
        var logger = new PrefixingModLogger(inner, "[Q1 (quest_1)] ");

        logger.Info("info");
        logger.Success("ok");
        logger.Warning("careful");
        logger.Debug("verbose");

        inner.Infos.Should().ContainSingle().Which.Should().Be("[Q1 (quest_1)] info");
        inner.Successes.Should().ContainSingle().Which.Should().Be("[Q1 (quest_1)] ok");
        inner.Warnings.Should().ContainSingle().Which.Should().Be("[Q1 (quest_1)] careful");
        inner.Debugs.Should().ContainSingle().Which.Should().Be("[Q1 (quest_1)] verbose");
    }

    [Fact]
    public void Empty_prefix_leaves_messages_unchanged()
    {
        var inner = new CapturingModLogger();
        var logger = new PrefixingModLogger(inner, string.Empty);

        logger.Info("hello");
        logger.Warning("watch out");

        inner.Infos.Should().ContainSingle().Which.Should().Be("hello");
        inner.Warnings.Should().ContainSingle().Which.Should().Be("watch out");
    }

    [Fact]
    public void Forwards_each_call_through_to_inner_exactly_once()
    {
        var inner = new CapturingModLogger();
        var logger = new PrefixingModLogger(inner, "P ");

        logger.Info("a");
        logger.Info("b");
        logger.Debug("c");

        inner.Infos.Should().BeEquivalentTo(["P a", "P b"], o => o.WithStrictOrdering());
        inner.Debugs.Should().ContainSingle().Which.Should().Be("P c");
        inner.Warnings.Should().BeEmpty();
        inner.Successes.Should().BeEmpty();
    }
}
