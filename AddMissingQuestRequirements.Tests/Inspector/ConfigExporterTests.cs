using System.Text.Json;
using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Inspector;
using AddMissingQuestRequirements.Pipeline.Override;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Inspector;

public sealed class ConfigExporterTests
{
    private const string Mqw = "MissingQuestWeapons";

    private static void WriteNonDefaultConfig(InspectorFixture fx)
    {
        File.WriteAllText(Path.Combine(fx.ConfigPath, "config.jsonc"), """
            {
                "version": 4,
                "enabled": false,
                "parentTypes": { "BoltActionSniperRifle": "SniperRifle" },
                "excludedItems": ["item_x"],
                "excludedWeaponTypes": ["Knife"],
                "includeParentCategories": false,
                "debug": true,
                "bestCandidateExpansion": true,
                "unknownWeaponHandling": "Strip",
                "validateOverrideIds": true,
                "weaponLikeAncestors": ["Weapon"],
                "modBundleCartesianCap": 42
            }
            """);
        File.WriteAllText(Path.Combine(fx.ConfigPath, Mqw, "QuestOverrides.jsonc"), """
            {
                "version": 3,
                "overrideBehaviour": "MERGE",
                "excludedQuests": ["q_skip"],
                "overrides": [
                    {
                        "id": "q1",
                        "expansionMode": "WhitelistOnly",
                        "includedWeapons": ["w1"],
                        "includedModBundles": [["Silencer", "m1"]]
                    },
                    {
                        "id": "q1",
                        "conditions": ["c1"],
                        "excludedWeapons": ["w2"]
                    }
                ]
            }
            """);
        File.WriteAllText(Path.Combine(fx.ConfigPath, Mqw, "WeaponOverrides.jsonc"), """
            {
                "version": 2,
                "manualTypeOverrides": { "w1": "AssaultRifle" },
                "canBeUsedAs": { "w1": ["w2"] },
                "aliasNameStripWords": ["mod"],
                "aliasNameExcludeWeapons": ["w3"],
                "customTypeRules": [{ "type": "AKM", "conditions": { "nameMatches": "AKM" } }]
            }
            """);
        File.WriteAllText(Path.Combine(fx.ConfigPath, Mqw, "AttachmentOverrides.jsonc"), """
            {
                "version": 2,
                "manualAttachmentTypeOverrides": { "m1": "Silencer" },
                "canBeUsedAs": { "m1": ["m2"] },
                "aliasNameStripWords": ["suppressor"],
                "customTypeRules": [{ "type": "all_silencers", "conditions": { "hasAncestor": "Silencer" } }]
            }
            """);
    }

    [Fact]
    public void Exported_config_reloads_to_the_same_config_and_settings()
    {
        using var fx = new InspectorFixture();
        WriteNonDefaultConfig(fx);
        var original = InspectorLoader.Load(fx.Config, fx.Logger);
        var exportDir = Path.Combine(fx.RootDir, "export");

        ConfigExporter.Export(original, exportDir);
        var reloaded = InspectorLoader.Load(
            new InspectorConfig
            {
                ExportedDbPath = fx.DbPath,
                MainConfigPath = exportDir,
                OutputReportPath = fx.Config.OutputReportPath,
            },
            fx.Logger);

        reloaded.Config.Should().BeEquivalentTo(original.Config);
        // Rule conditions are JsonElements, which compare by reference; check rules as JSON.
        reloaded.Settings.Should().BeEquivalentTo(original.Settings, o => o
            .Excluding(s => s.TypeRules)
            .Excluding(s => s.AttachmentTypeRules));
        Json(reloaded.Settings.TypeRules).Should().Be(Json(original.Settings.TypeRules));
        Json(reloaded.Settings.AttachmentTypeRules).Should().Be(Json(original.Settings.AttachmentTypeRules));
        original.Settings.QuestOverrides["q1"].Should().HaveCount(2);
        original.Settings.TypeRules.Should().ContainSingle();
        original.Settings.AttachmentTypeRules.Should().ContainSingle();
    }

    private static string Json<T>(T value)
    {
        return JsonSerializer.Serialize(value);
    }

    [Fact]
    public void Exported_files_carry_the_versions_the_loaders_expect()
    {
        using var fx = new InspectorFixture();
        var exportDir = Path.Combine(fx.RootDir, "export");

        ConfigExporter.Export(InspectorLoader.Load(fx.Config, fx.Logger), exportDir);

        int VersionOf(params string[] parts)
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine([exportDir, ..parts])))!;
            return json["version"]!.GetValue<int>();
        }

        VersionOf("config.jsonc").Should().Be(ModConfigFile.CurrentVersion);
        VersionOf(Mqw, "QuestOverrides.jsonc").Should().Be(OverrideReader.CurrentQuestVersion);
        VersionOf(Mqw, "WeaponOverrides.jsonc").Should().Be(OverrideReader.CurrentWeaponVersion);
        VersionOf(Mqw, "AttachmentOverrides.jsonc").Should().Be(OverrideReader.CurrentAttachmentVersion);
    }
}
