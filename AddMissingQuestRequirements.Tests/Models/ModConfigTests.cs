using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Models;

public class ModConfigTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Deserializes_full_config()
    {
        var json = """
        {
            "version": 1,
            "parentTypes": {
                "Revolver": "Pistol",
                "BoltActionSniperRifle": "SniperRifle"
            },
            "excludedItems": ["item_a", "item_b"],
            "excludedWeaponTypes": ["ThrowWeap"],
            "includeParentCategories": true,
            "debug": false,
            "bestCandidateExpansion": false,
            "validateOverrideIds": false
        }
        """;

        var result = JsonSerializer.Deserialize<ModConfig>(json, Options);

        result!.Version.Should().Be(1);
        result.ParentTypes.Should().ContainKey("Revolver").WhoseValue.Should().Be("Pistol");
        result.ParentTypes.Should().ContainKey("BoltActionSniperRifle").WhoseValue.Should().Be("SniperRifle");
        result.ExcludedItems.Should().BeEquivalentTo(["item_a", "item_b"]);
        result.ExcludedWeaponTypes.Should().BeEquivalentTo(["ThrowWeap"]);
        result.IncludeParentCategories.Should().BeTrue();
        result.Debug.Should().BeFalse();
        result.BestCandidateExpansion.Should().BeFalse();
        result.ValidateOverrideIds.Should().BeFalse();
    }

    [Fact]
    public void Deserializes_with_defaults_when_fields_missing()
    {
        var json = "{}";
        var result = JsonSerializer.Deserialize<ModConfig>(json, Options);
        result.Should().NotBeNull();
        result!.ExcludedItems.Should().NotBeNull();
        result.ExcludedWeaponTypes.Should().NotBeNull();
        result.ParentTypes.Should().NotBeNull();
    }
}
