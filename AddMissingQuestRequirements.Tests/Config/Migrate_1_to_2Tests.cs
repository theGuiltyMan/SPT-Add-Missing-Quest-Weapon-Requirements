using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Config;

public class Migrate_1_to_2Tests
{
    // ── v1_to_v2_Quest ────────────────────────────────────────────────────────

    [Fact]
    public void Quest_renames_BlackListedQuests_to_excludedQuests()
    {
        var json = JsonNode.Parse("""{"BlackListedQuests":["q1","q2"]}""")!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        result.ContainsKey("BlackListedQuests").Should().BeFalse();
        result["excludedQuests"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void Quest_renames_OverrideBehaviour_to_overrideBehaviour()
    {
        var json = JsonNode.Parse("""{"OverrideBehaviour":"MERGE"}""")!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        result.ContainsKey("OverrideBehaviour").Should().BeFalse();
        result["overrideBehaviour"]!.GetValue<string>().Should().Be("MERGE");
    }

    [Fact]
    public void Quest_renames_Overrides_to_overrides()
    {
        var json = JsonNode.Parse("""{"Overrides":[]}""")!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        result.ContainsKey("Overrides").Should().BeFalse();
        result["overrides"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void Quest_converts_skip_true_to_expansionMode_NoExpansion()
    {
        var json = JsonNode.Parse("""
        {
            "Overrides": [
                { "id": "q1", "skip": true }
            ]
        }
        """)!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        var entry = result["overrides"]![0]!.AsObject();
        entry.ContainsKey("skip").Should().BeFalse();
        entry["expansionMode"]!.GetValue<string>().Should().Be("NoExpansion");
    }

    [Fact]
    public void Quest_converts_skip_false_removes_field_without_setting_expansionMode()
    {
        var json = JsonNode.Parse("""
        {
            "Overrides": [
                { "id": "q1", "skip": false }
            ]
        }
        """)!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        var entry = result["overrides"]![0]!.AsObject();
        entry.ContainsKey("skip").Should().BeFalse();
        entry.ContainsKey("expansionMode").Should().BeFalse();
    }

    [Fact]
    public void Quest_converts_onlyUseWhiteListedWeapons_true_to_expansionMode_WhitelistOnly()
    {
        var json = JsonNode.Parse("""
        {
            "Overrides": [
                { "id": "q1", "onlyUseWhiteListedWeapons": true }
            ]
        }
        """)!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        var entry = result["overrides"]![0]!.AsObject();
        entry.ContainsKey("onlyUseWhiteListedWeapons").Should().BeFalse();
        entry["expansionMode"]!.GetValue<string>().Should().Be("WhitelistOnly");
    }

    [Fact]
    public void Quest_skip_true_takes_precedence_when_both_flags_set()
    {
        // skip: true + onlyUseWhiteListedWeapons: true → NoExpansion wins (skip is processed first)
        var json = JsonNode.Parse("""
        {
            "Overrides": [
                { "id": "q1", "skip": true, "onlyUseWhiteListedWeapons": true }
            ]
        }
        """)!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        var entry = result["overrides"]![0]!.AsObject();
        entry["expansionMode"]!.GetValue<string>().Should().Be("NoExpansion");
    }

    [Fact]
    public void Quest_renames_whiteListedWeapons_to_includedWeapons()
    {
        var json = JsonNode.Parse("""
        {
            "Overrides": [
                { "id": "q1", "whiteListedWeapons": ["w_a", "w_b"] }
            ]
        }
        """)!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        var entry = result["overrides"]![0]!.AsObject();
        entry.ContainsKey("whiteListedWeapons").Should().BeFalse();
        entry["includedWeapons"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void Quest_renames_blackListedWeapons_to_excludedWeapons()
    {
        var json = JsonNode.Parse("""
        {
            "Overrides": [
                { "id": "q1", "blackListedWeapons": ["w_c"] }
            ]
        }
        """)!.AsObject();

        var result = Migrations.v1_to_v2_Quest(json);

        var entry = result["overrides"]![0]!.AsObject();
        entry.ContainsKey("blackListedWeapons").Should().BeFalse();
        entry["excludedWeapons"]!.AsArray().Should().HaveCount(1);
    }

    // ── v1_to_v2_Weapons ─────────────────────────────────────────────────────

    [Fact]
    public void Weapons_renames_Override_to_manualTypeOverrides()
    {
        var json = JsonNode.Parse("""{"Override":{"w_a":"AssaultRifle"}}""")!.AsObject();

        var result = Migrations.v1_to_v2_Weapons(json);

        result.ContainsKey("Override").Should().BeFalse();
        result["manualTypeOverrides"]!.AsObject().Should().ContainKey("w_a");
    }

    [Fact]
    public void Weapons_merges_Override_into_existing_manualTypeOverrides_from_ids()
    {
        // Simulates the v0→v1→v2 chain: v0_to_v1 creates manualTypeOverrides from CustomCategories.ids,
        // then v1_to_v2 must merge Override into it rather than silently dropping Override entries.
        var json = JsonNode.Parse("""
        {
            "manualTypeOverrides": { "w_from_ids": "12ga" },
            "Override": {
                "w_from_override": "GrenadeLauncher",
                "w_from_ids": "ShouldNotOverwrite"
            }
        }
        """)!.AsObject();

        var result = Migrations.v1_to_v2_Weapons(json);

        result.ContainsKey("Override").Should().BeFalse();
        var mto = result["manualTypeOverrides"]!.AsObject();
        mto["w_from_override"]!.GetValue<string>().Should().Be("GrenadeLauncher");
        mto["w_from_ids"]!.GetValue<string>().Should().Be("12ga");
    }

    [Fact]
    public void Weapons_renames_CanBeUsedAsShortNameWhitelist_to_aliasNameStripWords()
    {
        var json = JsonNode.Parse("""{"CanBeUsedAsShortNameWhitelist":["FDE","Gold"]}""")!.AsObject();

        var result = Migrations.v1_to_v2_Weapons(json);

        result.ContainsKey("CanBeUsedAsShortNameWhitelist").Should().BeFalse();
        result["aliasNameStripWords"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void Weapons_renames_OverrideBehaviour_to_overrideBehaviour()
    {
        var json = JsonNode.Parse("""{"OverrideBehaviour":"REPLACE"}""")!.AsObject();

        var result = Migrations.v1_to_v2_Weapons(json);

        result.ContainsKey("OverrideBehaviour").Should().BeFalse();
        result["overrideBehaviour"]!.GetValue<string>().Should().Be("REPLACE");
    }

    [Fact]
    public void Weapons_renames_CanBeUsedAs_to_canBeUsedAs()
    {
        var json = JsonNode.Parse("""{"CanBeUsedAs":{"w_a":["w_b"]}}""")!.AsObject();

        var result = Migrations.v1_to_v2_Weapons(json);

        result.ContainsKey("CanBeUsedAs").Should().BeFalse();
        result["canBeUsedAs"]!.AsObject().Should().ContainKey("w_a");
    }

    [Fact]
    public void Weapons_renames_CanBeUsedAsShortNameBlacklist_to_aliasNameExcludeWeapons()
    {
        var json = JsonNode.Parse("""{"CanBeUsedAsShortNameBlacklist":["FDE","Gold"]}""")!.AsObject();

        var result = Migrations.v1_to_v2_Weapons(json);

        result.ContainsKey("CanBeUsedAsShortNameBlacklist").Should().BeFalse();
        result["aliasNameExcludeWeapons"]!.AsArray().Should().HaveCount(2);
    }

    // ── v1_to_v2_Config ──────────────────────────────────────────────────────

    [Fact]
    public void Config_renames_categorizeWithLessRestrictive_to_includeParentCategories()
    {
        var json = JsonNode.Parse("""{"categorizeWithLessRestrictive":true}""")!.AsObject();

        var result = Migrations.v1_to_v2_Config(json);

        result.ContainsKey("categorizeWithLessRestrictive").Should().BeFalse();
        result["includeParentCategories"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Config_renames_kindOf_to_parentTypes()
    {
        var json = JsonNode.Parse("""{"kindOf":{"Revolver":"Pistol"}}""")!.AsObject();

        var result = Migrations.v1_to_v2_Config(json);

        result.ContainsKey("kindOf").Should().BeFalse();
        result["parentTypes"]!.AsObject().Should().ContainKey("Revolver");
    }

    [Fact]
    public void Config_renames_BlackListedItems_to_excludedItems()
    {
        var json = JsonNode.Parse("""{"BlackListedItems":["item_a"]}""")!.AsObject();

        var result = Migrations.v1_to_v2_Config(json);

        result.ContainsKey("BlackListedItems").Should().BeFalse();
        result["excludedItems"]!.AsArray().Should().HaveCount(1);
    }

    [Fact]
    public void Config_renames_BlackListedWeaponsTypes_to_excludedWeaponTypes()
    {
        var json = JsonNode.Parse("""{"BlackListedWeaponsTypes":["ThrowWeap"]}""")!.AsObject();

        var result = Migrations.v1_to_v2_Config(json);

        result.ContainsKey("BlackListedWeaponsTypes").Should().BeFalse();
        result["excludedWeaponTypes"]!.AsArray().Should().HaveCount(1);
    }

    [Fact]
    public void Config_is_noop_when_v1_keys_absent()
    {
        var json = JsonNode.Parse("""{"includeParentCategories":false,"debug":true}""")!.AsObject();

        var result = Migrations.v1_to_v2_Config(json);

        result["includeParentCategories"]!.GetValue<bool>().Should().BeFalse();
        result["debug"]!.GetValue<bool>().Should().BeTrue();
    }
}
