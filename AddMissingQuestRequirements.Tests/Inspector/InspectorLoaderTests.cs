using AddMissingQuestRequirements.Inspector;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Inspector;

public sealed class InspectorLoaderTests
{
    [Fact]
    public void Other_mod_folders_merge_after_the_main_config_without_touching_disk()
    {
        using var fx = new InspectorFixture();
        File.WriteAllText(Path.Combine(fx.ConfigPath, "MissingQuestWeapons", "WeaponOverrides.jsonc"), """
            { "version": 2, "manualTypeOverrides": { "w1": "FromMain" } }
            """);

        var container = Path.Combine(fx.RootDir, "mods");
        var otherMqw = Path.Combine(container, "OtherMod", "MissingQuestWeapons");
        Directory.CreateDirectory(otherMqw);
        // Legacy v0 weapon file: the server would migrate and rewrite it; the Inspector must not.
        var legacyPath = Path.Combine(otherMqw, "OverriddenWeapons.jsonc");
        File.WriteAllText(legacyPath, """
            { "Override": { "w1": "FromOther", "w2": "FromOther" } }
            """);
        File.WriteAllText(Path.Combine(otherMqw, "AttachmentOverrides.jsonc"), """
            {
                "version": 2,
                "customTypeRules": [{ "type": "all_silencers", "conditions": { "hasAncestor": "Silencer" } }]
            }
            """);
        var filesBefore = Directory.GetFiles(otherMqw).Order().ToList();
        var legacyBefore = File.ReadAllBytes(legacyPath);

        var loaded = InspectorLoader.Load(
            new InspectorConfig
            {
                ExportedDbPath = fx.DbPath,
                MainConfigPath = fx.ConfigPath,
                OtherModConfigPaths = [container],
                OutputReportPath = fx.Config.OutputReportPath,
            },
            fx.Logger);

        // Main config loads first, so under the default IGNORE its w1 wins.
        loaded.Settings.ManualTypeOverrides.Should().Contain("w1", "FromMain").And.Contain("w2", "FromOther");
        loaded.Settings.AttachmentTypeRules.Select(r => r.Type).Should().Equal("all_silencers");
        Directory.GetFiles(otherMqw).Order().Should().Equal(filesBefore);
        File.ReadAllBytes(legacyPath).Should().Equal(legacyBefore);
    }
}
