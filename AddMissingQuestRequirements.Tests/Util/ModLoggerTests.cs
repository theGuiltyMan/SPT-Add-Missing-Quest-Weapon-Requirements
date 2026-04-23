using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Util;

public class ModLoggerTests
{
    // ── CapturingModLogger ───────────────────────────────────────────────────

    [Fact]
    public void CapturingModLogger_stores_info_messages()
    {
        var logger = new CapturingModLogger();
        logger.Info("hello");
        logger.Infos.Should().ContainSingle().Which.Should().Be("hello");
    }

    [Fact]
    public void CapturingModLogger_stores_success_messages()
    {
        var logger = new CapturingModLogger();
        logger.Success("ok");
        logger.Successes.Should().ContainSingle().Which.Should().Be("ok");
    }

    [Fact]
    public void CapturingModLogger_stores_warning_messages()
    {
        var logger = new CapturingModLogger();
        logger.Warning("careful");
        logger.Warnings.Should().ContainSingle().Which.Should().Be("careful");
    }

    [Fact]
    public void CapturingModLogger_stores_debug_messages()
    {
        var logger = new CapturingModLogger();
        logger.Debug("verbose");
        logger.Debugs.Should().ContainSingle().Which.Should().Be("verbose");
    }

    [Fact]
    public void CapturingModLogger_keeps_messages_in_separate_lists()
    {
        var logger = new CapturingModLogger();
        logger.Info("i");
        logger.Warning("w");
        logger.Debug("d");

        logger.Infos.Should().ContainSingle();
        logger.Warnings.Should().ContainSingle();
        logger.Debugs.Should().ContainSingle();
        logger.Successes.Should().BeEmpty();
    }

    // ── NullModLogger ────────────────────────────────────────────────────────

    [Fact]
    public void NullModLogger_does_not_throw_on_any_call()
    {
        var logger = NullModLogger.Instance;
        var act = () =>
        {
            logger.Info("x");
            logger.Success("x");
            logger.Warning("x");
            logger.Debug("x");
        };
        act.Should().NotThrow();
    }

    // ── DebugFilteringModLogger ──────────────────────────────────────────────

    [Fact]
    public void DebugFilteringModLogger_forwards_debug_when_enabled()
    {
        var inner = new CapturingModLogger();
        var logger = new DebugFilteringModLogger(inner, debugEnabled: true);
        logger.Debug("verbose");
        inner.Debugs.Should().ContainSingle().Which.Should().Be("verbose");
    }

    [Fact]
    public void DebugFilteringModLogger_suppresses_debug_when_disabled()
    {
        var inner = new CapturingModLogger();
        var logger = new DebugFilteringModLogger(inner, debugEnabled: false);
        logger.Debug("verbose");
        inner.Debugs.Should().BeEmpty();
    }

    [Fact]
    public void DebugFilteringModLogger_always_forwards_non_debug_levels()
    {
        var inner = new CapturingModLogger();
        var logger = new DebugFilteringModLogger(inner, debugEnabled: false);
        logger.Info("i");
        logger.Success("s");
        logger.Warning("w");

        inner.Infos.Should().ContainSingle();
        inner.Successes.Should().ContainSingle();
        inner.Warnings.Should().ContainSingle();
        inner.Debugs.Should().BeEmpty();
    }
}
