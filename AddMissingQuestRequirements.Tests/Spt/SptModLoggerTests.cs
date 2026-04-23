using AddMissingQuestRequirements.Spt;
using AddMissingQuestRequirements.Tests.Spt.Fakes;
using FluentAssertions;
using SPTarkov.Server.Core.Models.Logging;

namespace AddMissingQuestRequirements.Tests.Spt;

public class SptModLoggerTests
{
    // Tag type — lets ISptLogger<T> resolve without pulling in a real SPT type.
    private sealed class SampleTag
    {
    }

    [Fact]
    public void Success_routes_to_inner_Success()
    {
        var fake = new FakeSptLogger<SampleTag>();
        var logger = new SptModLogger<SampleTag>(fake);

        logger.Success("all good");

        fake.Successes.Should().ContainSingle().Which.Should().Be("all good");
        fake.Warnings.Should().BeEmpty();
        fake.Debugs.Should().BeEmpty();
        fake.WithColor.Should().BeEmpty();
    }

    [Fact]
    public void Warning_routes_to_inner_Warning()
    {
        var fake = new FakeSptLogger<SampleTag>();
        var logger = new SptModLogger<SampleTag>(fake);

        logger.Warning("careful");

        fake.Warnings.Should().ContainSingle().Which.Should().Be("careful");
        fake.Successes.Should().BeEmpty();
        fake.Debugs.Should().BeEmpty();
        fake.WithColor.Should().BeEmpty();
    }

    [Fact]
    public void Info_routes_to_inner_Info()
    {
        var fake = new FakeSptLogger<SampleTag>();
        var logger = new SptModLogger<SampleTag>(fake);

        logger.Info("hello");

        fake.Infos.Should().ContainSingle().Which.Should().Be("hello");
        fake.WithColor.Should().BeEmpty();
    }

    [Fact]
    public void Debug_routes_to_inner_Debug()
    {
        var fake = new FakeSptLogger<SampleTag>();
        var logger = new SptModLogger<SampleTag>(fake);

        logger.Debug("verbose");

        fake.Debugs.Should().ContainSingle().Which.Should().Be("verbose");
        fake.Successes.Should().BeEmpty();
        fake.Warnings.Should().BeEmpty();
        fake.WithColor.Should().BeEmpty();
    }
}
