using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Config;

/// <summary>
/// Tests for Migrations.v0_to_v1_Weapons.
/// All tests use JsonNode comparison via ToString() after normalisation,
/// or access individual properties by path.
/// </summary>
public class Migrate_0_to_1_WeaponsTests
{
    private static JsonObject Migrate(string json)
    {
        var obj = JsonNode.Parse(json)!.AsObject();
        return Migrations.v0_to_v1_Weapons(obj);
    }

    [Fact]
    public void No_CustomCategories_key_leaves_json_unchanged()
    {
        var result = Migrate("""{ "manualTypeOverrides": { "wep1": "AssaultRifle" } }""");

        result.ContainsKey("customTypeRules").Should().BeFalse();
        result["manualTypeOverrides"]!["wep1"]!.GetValue<string>().Should().Be("AssaultRifle");
    }

    [Fact]
    public void Empty_CustomCategories_produces_no_customTypeRules()
    {
        var result = Migrate("""{ "CustomCategories": [] }""");

        result.ContainsKey("CustomCategories").Should().BeFalse();
        var rules = result["customTypeRules"]?.AsArray();
        (rules is null || rules.Count == 0).Should().BeTrue();
    }

    [Fact]
    public void Ids_only_goes_to_manualTypeOverrides_no_TypeRule()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AKM", "ids": ["wep_akm", "wep_akm2"] }] }
            """);

        var overrides = result["manualTypeOverrides"]!.AsObject();
        overrides["wep_akm"]!.GetValue<string>().Should().Be("AKM");
        overrides["wep_akm2"]!.GetValue<string>().Should().Be("AKM");

        var rules = result["customTypeRules"]?.AsArray();
        (rules is null || rules.Count == 0).Should().BeTrue();
    }

    [Fact]
    public void Single_keyword_produces_flat_nameMatches_rule()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AKM", "whiteListedKeywords": ["AKM\\w*"] }] }
            """);

        var rules = result["customTypeRules"]!.AsArray();
        rules.Should().HaveCount(1);
        var rule = rules[0]!.AsObject();
        rule["type"]!.GetValue<string>().Should().Be("AKM");
        rule["conditions"]!["nameMatches"]!.GetValue<string>().Should().Be("AKM\\w*");
        rule["conditions"]!.AsObject().ContainsKey("or").Should().BeFalse();
    }

    [Fact]
    public void Multiple_keywords_produce_flat_nameMatches_rule()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AKM", "whiteListedKeywords": ["AKM\\w*", "VPO\\w*"] }] }
            """);

        var rules = result["customTypeRules"]!.AsArray();
        rules.Should().HaveCount(1);
        var conditions = rules[0]!["conditions"]!.AsObject();
        conditions["nameMatches"]!.GetValue<string>().Should().Be("AKM\\w*|VPO\\w*");
        conditions.ContainsKey("or").Should().BeFalse();
    }

    [Fact]
    public void Multiple_keywords_with_alsoCheckDescription_collapses_to_two_item_or()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AR15", "whiteListedKeywords": ["AR-15\\w*", "M4\\w*"], "alsoCheckDescription": true }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions.ContainsKey("nameMatches").Should().BeFalse();
        var orArray = conditions["or"]!.AsArray();
        orArray.Should().HaveCount(2);
        orArray[0]!["nameMatches"]!.GetValue<string>().Should().Be("AR-15\\w*|M4\\w*");
        orArray[1]!["descriptionMatches"]!.GetValue<string>().Should().Be("AR-15\\w*|M4\\w*");
    }

    [Fact]
    public void Keyword_plus_single_caliber_produces_flat_conditions()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AKM", "whiteListedKeywords": ["AKM\\w*"], "allowedCalibres": ["Caliber762x39"] }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions["nameMatches"]!.GetValue<string>().Should().Be("AKM\\w*");
        conditions["caliber"]!.GetValue<string>().Should().Be("Caliber762x39");
        conditions.ContainsKey("and").Should().BeFalse();
    }

    [Fact]
    public void Keyword_plus_multiple_calibers_produces_flat_conditions_with_or_for_calibers()
    {
        // nameMatches and or are distinct keys — no and wrapping needed
        var result = Migrate("""
            { "CustomCategories": [{ "name": "X", "whiteListedKeywords": ["X\\w*"], "allowedCalibres": ["CalA", "CalB"] }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions["nameMatches"]!.GetValue<string>().Should().Be("X\\w*");
        var orArray = conditions["or"]!.AsArray();
        orArray[0]!["caliber"]!.GetValue<string>().Should().Be("CalA");
        orArray[1]!["caliber"]!.GetValue<string>().Should().Be("CalB");
        conditions.ContainsKey("and").Should().BeFalse();
    }

    [Fact]
    public void Single_blacklisted_keyword_produces_flat_not_nameMatches()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AK74", "whiteListedKeywords": ["AK-74\\w*"], "blackListedKeywords": ["AKS-74U\\w*"] }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions["nameMatches"]!.GetValue<string>().Should().Be("AK-74\\w*");
        conditions["not"]!["nameMatches"]!.GetValue<string>().Should().Be("(AKS-74U\\w*)");
        conditions.ContainsKey("and").Should().BeFalse();
    }

    [Fact]
    public void Multiple_blacklisted_keywords_combined_into_or_regex()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AK74", "whiteListedKeywords": ["AK-74\\w*"], "blackListedKeywords": ["AKS-74U\\w*", "AKMS\\w*"] }] }
            """);

        var notObj = result["customTypeRules"]![0]!["conditions"]!["not"]!.AsObject();
        notObj["nameMatches"]!.GetValue<string>().Should().Be("(AKS-74U\\w*)|(AKMS\\w*)");
    }

    [Fact]
    public void AlsoCheckDescription_single_keyword_produces_or_name_description()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AR15", "whiteListedKeywords": ["AR-15\\w*"], "alsoCheckDescription": true }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions.ContainsKey("nameMatches").Should().BeFalse();
        var orArray = conditions["or"]!.AsArray();
        orArray.Should().HaveCount(2);
        orArray[0]!["nameMatches"]!.GetValue<string>().Should().Be("AR-15\\w*");
        orArray[1]!["descriptionMatches"]!.GetValue<string>().Should().Be("AR-15\\w*");
    }

    [Fact]
    public void AlsoCheckDescription_with_blacklisted_keywords_uses_not_or()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AR15", "whiteListedKeywords": ["AR-15\\w*"], "blackListedKeywords": ["M16\\w*"], "alsoCheckDescription": true }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        var notObj = conditions["not"]!.AsObject();
        var notOrArray = notObj["or"]!.AsArray();
        notOrArray[0]!["nameMatches"]!.GetValue<string>().Should().Be("(M16\\w*)");
        notOrArray[1]!["descriptionMatches"]!.GetValue<string>().Should().Be("(M16\\w*)");
    }

    [Fact]
    public void Single_weaponType_produces_hasAncestor_condition()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "MyAR", "weaponTypes": ["AssaultRifle"] }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions["hasAncestor"]!.GetValue<string>().Should().Be("AssaultRifle");
    }

    [Fact]
    public void Multiple_weaponTypes_produce_or_hasAncestor()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "MyAR", "weaponTypes": ["AssaultRifle", "AssaultCarbine"] }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        var orArray = conditions["or"]!.AsArray();
        orArray[0]!["hasAncestor"]!.GetValue<string>().Should().Be("AssaultRifle");
        orArray[1]!["hasAncestor"]!.GetValue<string>().Should().Be("AssaultCarbine");
    }

    [Fact]
    public void Single_blacklistedCalibre_produces_not_caliber()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "X", "whiteListedKeywords": ["X\\w*"], "blackListedCalibres": ["Caliber9x19"] }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions["not"]!["caliber"]!.GetValue<string>().Should().Be("Caliber9x19");
    }

    [Fact]
    public void Multiple_blacklistedCalibres_produce_not_or_caliber()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "X", "whiteListedKeywords": ["X\\w*"], "blackListedCalibres": ["CalA", "CalB"] }] }
            """);

        var notObj = result["customTypeRules"]![0]!["conditions"]!["not"]!.AsObject();
        var orArray = notObj["or"]!.AsArray();
        orArray[0]!["caliber"]!.GetValue<string>().Should().Be("CalA");
        orArray[1]!["caliber"]!.GetValue<string>().Should().Be("CalB");
    }

    [Fact]
    public void Single_blacklistedWeaponType_produces_not_hasAncestor()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "X", "whiteListedKeywords": ["X\\w*"], "blackListedWeaponTypes": ["Pistol"] }] }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions["not"]!["hasAncestor"]!.GetValue<string>().Should().Be("Pistol");
    }

    [Fact]
    public void Multiple_blacklistedWeaponTypes_produce_not_or_hasAncestor()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "X", "whiteListedKeywords": ["X\\w*"], "blackListedWeaponTypes": ["Pistol", "Shotgun"] }] }
            """);

        var notObj = result["customTypeRules"]![0]!["conditions"]!["not"]!.AsObject();
        var orArray = notObj["or"]!.AsArray();
        orArray[0]!["hasAncestor"]!.GetValue<string>().Should().Be("Pistol");
        orArray[1]!["hasAncestor"]!.GetValue<string>().Should().Be("Shotgun");
    }

    [Fact]
    public void Complex_category_with_keywords_weaponTypes_and_blacklist_produces_flat_conditions()
    {
        // keywords → nameMatches (flat key, no or-wrapper)
        // weaponTypes → or key
        // blacklist → not key
        // All distinct — no and-wrapping needed
        var result = Migrate("""
            {
                "CustomCategories": [{
                    "name": "MyCategory",
                    "whiteListedKeywords": ["p1\\w*", "p2\\w*"],
                    "weaponTypes": ["AssaultRifle", "AssaultCarbine"],
                    "blackListedKeywords": ["bl1\\w*"]
                }]
            }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions.ContainsKey("and").Should().BeFalse();

        conditions["nameMatches"]!.GetValue<string>().Should().Be("p1\\w*|p2\\w*");

        var orArray = conditions["or"]!.AsArray();
        orArray[0]!["hasAncestor"]!.GetValue<string>().Should().Be("AssaultRifle");
        orArray[1]!["hasAncestor"]!.GetValue<string>().Should().Be("AssaultCarbine");

        conditions["not"]!["nameMatches"]!.GetValue<string>().Should().Be("(bl1\\w*)");
    }

    [Fact]
    public void CustomCategories_key_is_removed_after_migration()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AKM", "whiteListedKeywords": ["AKM\\w*"] }] }
            """);

        result.ContainsKey("CustomCategories").Should().BeFalse();
    }

    [Fact]
    public void ManualTypeOverrides_created_when_absent()
    {
        var result = Migrate("""
            { "CustomCategories": [{ "name": "AKM", "ids": ["wep_akm"] }] }
            """);

        result["manualTypeOverrides"]!["wep_akm"]!.GetValue<string>().Should().Be("AKM");
    }

    [Fact]
    public void ManualTypeOverrides_merged_when_already_present()
    {
        var result = Migrate("""
            {
                "manualTypeOverrides": { "existing_wep": "SomeType" },
                "CustomCategories": [{ "name": "AKM", "ids": ["wep_akm"] }]
            }
            """);

        result["manualTypeOverrides"]!["existing_wep"]!.GetValue<string>().Should().Be("SomeType");
        result["manualTypeOverrides"]!["wep_akm"]!.GetValue<string>().Should().Be("AKM");
    }

    // ── Real-data regression tests ────────────────────────────────────────────

    [Fact]
    public void Real_AKM_entry_produces_correct_rule()
    {
        // From old_mod.log: name=AKM, whiteListedKeywords=["\\b(AKM|AK-1|VPO|Draco)\\w*"], allowedCalibres=["Caliber762x39"]
        // 1 keyword, 1 caliber — distinct keys → flat conditions
        var result = Migrate("""
            {
                "CustomCategories": [{
                    "name": "AKM",
                    "whiteListedKeywords": ["\\b(AKM|AK-1|VPO|Draco)\\w*"],
                    "allowedCalibres": ["Caliber762x39"]
                }]
            }
            """);

        var rule = result["customTypeRules"]![0]!;
        rule["type"]!.GetValue<string>().Should().Be("AKM");
        rule["comment"]!.GetValue<string>().Should().Be("Migrated from CustomCategories 'AKM'");
        var conditions = rule["conditions"]!.AsObject();
        conditions["nameMatches"]!.GetValue<string>().Should().Be("\\b(AKM|AK-1|VPO|Draco)\\w*");
        conditions["caliber"]!.GetValue<string>().Should().Be("Caliber762x39");
        conditions.ContainsKey("and").Should().BeFalse();
    }

    [Fact]
    public void Real_AK74_entry_produces_correct_rule()
    {
        // From old_mod.log: name=AK-74, whiteListedKeywords=["\\b(AK-74|AKS-74|...)\\w*"], blackListedKeywords=["\\b(AKS-74U)\\w*"]
        // 1 keyword, 1 blacklisted keyword — nameMatches and not are distinct keys → flat
        var result = Migrate("""
            {
                "CustomCategories": [{
                    "name": "AK-74",
                    "whiteListedKeywords": ["\\b(AK-74|AKS-74|SAG AK-545|Saiga SGL31|Saiga MK Ver. 030)\\w*"],
                    "blackListedKeywords": ["\\b(AKS-74U)\\w*"]
                }]
            }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        conditions["nameMatches"]!.GetValue<string>()
            .Should().Be("\\b(AK-74|AKS-74|SAG AK-545|Saiga SGL31|Saiga MK Ver. 030)\\w*");
        conditions["not"]!["nameMatches"]!.GetValue<string>().Should().Be("(\\b(AKS-74U)\\w*)");
        conditions.ContainsKey("and").Should().BeFalse();
    }

    [Fact]
    public void Real_AR15_entry_produces_correct_rule()
    {
        // From old_mod.log: name=AR-15, whiteListedKeywords=["\\b(AR-15|M4A1|ATL-15)\\w*"], alsoCheckDescription=true
        var result = Migrate("""
            {
                "CustomCategories": [{
                    "name": "AR-15",
                    "whiteListedKeywords": ["\\b(AR-15|M4A1|ATL-15)\\w*"],
                    "alsoCheckDescription": true
                }]
            }
            """);

        var conditions = result["customTypeRules"]![0]!["conditions"]!.AsObject();
        var orArray = conditions["or"]!.AsArray();
        orArray.Should().HaveCount(2);
        orArray[0]!["nameMatches"]!.GetValue<string>().Should().Be("\\b(AR-15|M4A1|ATL-15)\\w*");
        orArray[1]!["descriptionMatches"]!.GetValue<string>().Should().Be("\\b(AR-15|M4A1|ATL-15)\\w*");
    }
}
