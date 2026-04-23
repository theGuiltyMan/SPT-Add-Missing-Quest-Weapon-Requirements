using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Util;

public class TeeModLoggerTests
{
    [Fact]
    public void NonDebug_levels_reach_both_sinks()
    {
        var console = new CapturingModLogger();
        var file    = new CapturingModLogger();
        var tee     = new TeeModLogger(console, file);

        tee.Info("i");
        tee.Success("s");
        tee.Warning("w");

        console.Infos.Should().ContainSingle().Which.Should().Be("i");
        console.Successes.Should().ContainSingle().Which.Should().Be("s");
        console.Warnings.Should().ContainSingle().Which.Should().Be("w");

        file.Infos.Should().ContainSingle().Which.Should().Be("i");
        file.Successes.Should().ContainSingle().Which.Should().Be("s");
        file.Warnings.Should().ContainSingle().Which.Should().Be("w");
    }

    [Fact]
    public void Debug_reaches_file_sink_only()
    {
        var console = new CapturingModLogger();
        var file    = new CapturingModLogger();
        var tee     = new TeeModLogger(console, file);

        tee.Debug("d");

        console.Debugs.Should().BeEmpty();
        file.Debugs.Should().ContainSingle().Which.Should().Be("d");
    }
}
