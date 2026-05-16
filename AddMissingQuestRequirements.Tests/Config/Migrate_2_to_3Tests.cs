using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Config;

public class Migrate_2_to_3Tests
{
    [Fact]
    public void KeepUnknownWeaponsTrue_MapsToKeepInDb()
    {
        var input  = JsonNode.Parse("""{"keepUnknownWeapons":true}""")!.AsObject();
        var result = Migrations.v2_to_v3_Config(input);

        result.ContainsKey("keepUnknownWeapons").Should().BeFalse();
        result["unknownWeaponHandling"]!.GetValue<int>().Should().Be((int)UnknownWeaponHandling.KeepInDb);
    }

    [Fact]
    public void KeepUnknownWeaponsFalse_MapsToStrip()
    {
        var input  = JsonNode.Parse("""{"keepUnknownWeapons":false}""")!.AsObject();
        var result = Migrations.v2_to_v3_Config(input);

        result.ContainsKey("keepUnknownWeapons").Should().BeFalse();
        result["unknownWeaponHandling"]!.GetValue<int>().Should().Be((int)UnknownWeaponHandling.Strip);
    }

    [Fact]
    public void Absent_MapsToKeepAll()
    {
        var input  = new JsonObject();
        var result = Migrations.v2_to_v3_Config(input);

        result["unknownWeaponHandling"]!.GetValue<int>().Should().Be((int)UnknownWeaponHandling.KeepAll);
    }

    [Fact]
    public void UserAuthoredUnknownWeaponHandling_IsPreserved()
    {
        // If the user hand-wrote the new key, do not overwrite even if the old key is present.
        var input = JsonNode.Parse("""{"keepUnknownWeapons":true,"unknownWeaponHandling":0}""")!.AsObject();
        var result = Migrations.v2_to_v3_Config(input);

        result.ContainsKey("keepUnknownWeapons").Should().BeFalse();
        result["unknownWeaponHandling"]!.GetValue<int>().Should().Be((int)UnknownWeaponHandling.Strip);
    }

    [Fact]
    public void IntegrationViaConfigLoader_OldBoolTrue_ProducesKeepInDb()
    {
        const string jsonc = """
            {
              "version": 1,
              "keepUnknownWeapons": true
            }
            """;

        var result = ConfigLoader.LoadFromString<ModConfig>(
            jsonc,
            currentVersion: 3,
            migrations: [Migrations.v0_to_v1, Migrations.v1_to_v2_Config, Migrations.v2_to_v3_Config]);

        result.Config.UnknownWeaponHandling.Should().Be(UnknownWeaponHandling.KeepInDb);
    }

    // ── v2_to_v3_Quest ────────────────────────────────────────────────────────

    [Fact]
    public void Quest_v2_to_v3_plants_sentinel_when_any_entry_has_non_empty_excludedMods()
    {
        var json = JsonNode.Parse("""
        {
          "version": 2,
          "overrides": [
            { "id": "q1", "excludedMods": ["all_flashlights"] },
            { "id": "q2", "excludedMods": [] }
          ]
        }
        """)!.AsObject();

        var result = Migrations.v2_to_v3_Quest(json);

        result["_v2_to_v3_excludedMods_semantic_changed"]?.GetValue<bool>()
            .Should().BeTrue();
    }

    [Fact]
    public void Quest_v2_to_v3_no_sentinel_when_all_excludedMods_empty_or_missing()
    {
        var json = JsonNode.Parse("""
        {
          "version": 2,
          "overrides": [
            { "id": "q1", "excludedMods": [] },
            { "id": "q2" }
          ]
        }
        """)!.AsObject();

        var result = Migrations.v2_to_v3_Quest(json);

        result.ContainsKey("_v2_to_v3_excludedMods_semantic_changed").Should().BeFalse();
    }
}
