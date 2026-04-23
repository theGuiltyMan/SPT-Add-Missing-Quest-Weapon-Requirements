using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Models;

public class WeaponOverridesFileTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Deserializes_manualTypeOverrides_dictionary()
    {
        var json = """
        {
            "version": 2,
            "manualTypeOverrides": {
                "57c44b372459772d2b39b8ce": "AssaultCarbine,AssaultRifle",
                "60db29ce99594040e04c4a27": "Shotgun,Revolver"
            }
        }
        """;

        var result = JsonSerializer.Deserialize<WeaponOverridesFile>(json, Options);

        result!.Version.Should().Be(2);
        result.ManualTypeOverrides.Should().ContainKey("57c44b372459772d2b39b8ce")
            .WhoseValue.Should().Be("AssaultCarbine,AssaultRifle");
        result.ManualTypeOverrides.Should().ContainKey("60db29ce99594040e04c4a27")
            .WhoseValue.Should().Be("Shotgun,Revolver");
    }

    [Fact]
    public void Deserializes_canBeUsedAs_dictionary()
    {
        var json = """
        {
            "canBeUsedAs": {
                "5c46fbd72e2216398b5a8c9c": ["MIRA_weapon_svd"]
            },
            "aliasNameStripWords": ["FDE", "Gold"]
        }
        """;

        var result = JsonSerializer.Deserialize<WeaponOverridesFile>(json, Options);

        result!.CanBeUsedAs.Should().ContainKey("5c46fbd72e2216398b5a8c9c");
        result.CanBeUsedAs["5c46fbd72e2216398b5a8c9c"].Should().ContainSingle().Which.Value.Should().Be("MIRA_weapon_svd");
        result.AliasNameStripWords.Should().BeEquivalentTo(["FDE", "Gold"]);
    }

    [Fact]
    public void CanBeUsedAs_supports_Overridable_value_with_DELETE_behaviour()
    {
        var json = """
        {
            "canBeUsedAs": {
                "weapon_a": [
                    "weapon_b",
                    { "value": "weapon_c", "behaviour": "DELETE" }
                ]
            }
        }
        """;

        var result = JsonSerializer.Deserialize<WeaponOverridesFile>(json, Options);

        var aliases = result!.CanBeUsedAs["weapon_a"];
        aliases.Should().HaveCount(2);
        aliases[0].Value.Should().Be("weapon_b");
        aliases[0].Behaviour.Should().BeNull();
        aliases[1].Value.Should().Be("weapon_c");
        aliases[1].Behaviour.Should().Be(OverrideBehaviour.DELETE);
    }

    [Fact]
    public void Deserializes_empty_file()
    {
        var json = "{}";
        var result = JsonSerializer.Deserialize<WeaponOverridesFile>(json, Options);
        result!.ManualTypeOverrides.Should().NotBeNull();
        result.CanBeUsedAs.Should().NotBeNull();
    }

    [Fact]
    public void Deserializes_AliasNameExcludeWeapons()
    {
        var json = """
        {
            "version": 2,
            "aliasNameExcludeWeapons": ["FDE", "Gold"]
        }
        """;

        var result = JsonSerializer.Deserialize<WeaponOverridesFile>(json, Options);

        result!.AliasNameExcludeWeapons.Should().Equal(["FDE", "Gold"]);
    }

    [Fact]
    public void CustomTypeRules_deserialises_correctly()
    {
        var json = """
            {
                "version": 2,
                "customTypeRules": [
                    {
                        "type": "AKM",
                        "conditions": { "nameMatches": "AKM" },
                        "behaviour": "REPLACE"
                    }
                ]
            }
            """;

        var file = JsonSerializer.Deserialize<WeaponOverridesFile>(json, Options)!;

        file.CustomTypeRules.Should().HaveCount(1);
        file.CustomTypeRules[0].Type.Should().Be("AKM");
        file.CustomTypeRules[0].Behaviour.Should().Be(OverrideBehaviour.REPLACE);
    }

    [Fact]
    public void CustomTypeRules_is_empty_list_when_absent_from_json()
    {
        var json = """{ "version": 2 }""";

        var file = JsonSerializer.Deserialize<WeaponOverridesFile>(json, Options)!;

        file.CustomTypeRules.Should().BeEmpty();
    }

    [Fact]
    public void TypeRule_Behaviour_is_null_when_absent_from_json()
    {
        var json = """
            {
                "version": 2,
                "customTypeRules": [{ "type": "AKM", "conditions": { "nameMatches": "AKM" } }]
            }
            """;

        var file = JsonSerializer.Deserialize<WeaponOverridesFile>(json, Options)!;

        file.CustomTypeRules[0].Behaviour.Should().BeNull();
    }
}
