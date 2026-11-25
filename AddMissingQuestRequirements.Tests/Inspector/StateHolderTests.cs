using System.IO;
using AddMissingQuestRequirements.Inspector;
using AddMissingQuestRequirements.Inspector.Serve;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Inspector;

public class StateHolderTests
{
    [Fact]
    public void ApplyReload_BumpsVersionAndClearsError()
    {
        var holder = new StateHolder();
        holder.GetSnapshot().Version.Should().Be(0);
        holder.GetSnapshot().Result.Should().BeNull();

        using var fx = new InspectorFixture();
        var loaded = InspectorLoader.Load(fx.Config, fx.Logger);
        var result = PipelineRunner.Run(loaded, fx.Logger);

        holder.ApplyReloadError("boom");
        holder.GetSnapshot().LastError.Should().Be("boom");

        holder.ApplyReload(loaded, result);
        var snap = holder.GetSnapshot();
        snap.Result.Should().BeSameAs(result);
        snap.Loaded.Should().BeSameAs(loaded);
        snap.LastError.Should().BeNull();
        snap.Version.Should().Be(1);

        holder.ApplyReload(loaded, result);
        holder.GetSnapshot().Version.Should().Be(2);
    }

    [Fact]
    public void ApplyReloadError_PreservesPreviousResult()
    {
        using var fx = new InspectorFixture();
        var loaded = InspectorLoader.Load(fx.Config, fx.Logger);
        var result = PipelineRunner.Run(loaded, fx.Logger);

        var holder = new StateHolder();
        holder.ApplyReload(loaded, result);
        holder.ApplyReloadError("broken jsonc");

        var snap = holder.GetSnapshot();
        snap.Result.Should().BeSameAs(result);
        snap.LastError.Should().Be("broken jsonc");
        snap.Version.Should().Be(1);
    }

    [Fact]
    public void ReloadPipeline_FailureLeavesResultIntact()
    {
        using var fx = new InspectorFixture();
        var loaded = InspectorLoader.Load(fx.Config, fx.Logger);
        var result = PipelineRunner.Run(loaded, fx.Logger);

        var holder = new StateHolder();
        holder.ApplyReload(loaded, result);

        // Break the config file mid-flight.
        File.WriteAllText(Path.Combine(fx.ConfigPath, "config.jsonc"), "{ broken");

        var ok = ReloadPipeline.Run(fx.Config, holder, fx.Logger);
        ok.Should().BeFalse();

        var snap = holder.GetSnapshot();
        snap.Result.Should().BeSameAs(result);
        snap.LastError.Should().NotBeNullOrEmpty();
        snap.Version.Should().Be(1);
    }

    [Fact]
    public void ReloadPipeline_BrokenMainOverrideFile_FailsReloadAndKeepsLastGoodState()
    {
        using var fx = new InspectorFixture();
        var loaded = InspectorLoader.Load(fx.Config, fx.Logger);
        var result = PipelineRunner.Run(loaded, fx.Logger);
        var holder = new StateHolder();
        holder.ApplyReload(loaded, result);

        var brokenPath = Path.Combine(fx.ConfigPath, "MissingQuestWeapons", "WeaponOverrides.jsonc");
        File.WriteAllText(brokenPath, "{ \"overrideBehaviour\": \"MERG\" }");

        var ok = ReloadPipeline.Run(fx.Config, holder, fx.Logger);

        ok.Should().BeFalse();
        var snap = holder.GetSnapshot();
        snap.Result.Should().BeSameAs(result);
        snap.LastError.Should().Contain("WeaponOverrides.jsonc");
        snap.Version.Should().Be(1);
    }

    [Fact]
    public void ReloadPipeline_BrokenOtherModFile_DoesNotFailReload()
    {
        using var fx = new InspectorFixture();
        var container = Path.Combine(fx.RootDir, "mods");
        var otherMqw = Path.Combine(container, "OtherMod", "MissingQuestWeapons");
        Directory.CreateDirectory(otherMqw);
        File.WriteAllText(Path.Combine(otherMqw, "WeaponOverrides.jsonc"), "{ broken");
        var config = new InspectorConfig
        {
            ExportedDbPath = fx.DbPath,
            MainConfigPath = fx.ConfigPath,
            OtherModConfigPaths = [container],
            OutputReportPath = fx.Config.OutputReportPath,
        };

        var holder = new StateHolder();
        var ok = ReloadPipeline.Run(config, holder, fx.Logger);

        ok.Should().BeTrue();
        holder.GetSnapshot().LastError.Should().BeNull();
    }
}
