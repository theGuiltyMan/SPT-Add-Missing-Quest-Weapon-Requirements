using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Models;

public class QuestOverridesFileTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Deserializes_full_file()
    {
        var json = """
        {
            "version": 2,
            "overrideBehaviour": "MERGE",
            "excludedQuests": ["quest_a", "quest_b"],
            "overrides": [
                {
                    "id": "quest1",
                    "expansionMode": "WhitelistOnly",
                    "conditions": ["cond1"],
                    "includedWeapons": ["weapon_a", "12ga"],
                    "excludedWeapons": ["weapon_b"]
                }
            ]
        }
        """;

        var result = JsonSerializer.Deserialize<QuestOverridesFile>(json, Options);

        result!.Version.Should().Be(2);
        result.OverrideBehaviour.Should().Be(OverrideBehaviour.MERGE);
        result.ExcludedQuests.Should().BeEquivalentTo(["quest_a", "quest_b"]);
        result.Overrides.Should().HaveCount(1);

        var entry = result.Overrides[0];
        entry.Id.Should().Be("quest1");
        entry.ExpansionMode.Should().Be(ExpansionMode.WhitelistOnly);
        entry.Conditions.Should().BeEquivalentTo(["cond1"]);
        entry.IncludedWeapons.Should().BeEquivalentTo(["weapon_a", "12ga"]);
        entry.ExcludedWeapons.Should().BeEquivalentTo(["weapon_b"]);
    }

    [Fact]
    public void Deserializes_minimal_file()
    {
        var json = """{"overrides": []}""";
        var result = JsonSerializer.Deserialize<QuestOverridesFile>(json, Options);
        result!.Overrides.Should().BeEmpty();
        result.ExcludedQuests.Should().NotBeNull();
    }

    [Fact]
    public void QuestOverrideEntry_Behaviour_can_be_specified()
    {
        var json = """
        {
            "overrides": [
                {
                    "id": "quest2",
                    "behaviour": "REPLACE"
                }
            ]
        }
        """;
        var result = JsonSerializer.Deserialize<QuestOverridesFile>(json, Options);
        result!.Overrides[0].Behaviour.Should().Be(OverrideBehaviour.REPLACE);
    }

    [Fact]
    public void ExpansionMode_defaults_to_Auto()
    {
        var json = """{"overrides": [{"id": "quest1"}]}""";
        var result = JsonSerializer.Deserialize<QuestOverridesFile>(json, Options);
        result!.Overrides[0].ExpansionMode.Should().Be(ExpansionMode.Auto);
    }

    [Fact]
    public void ExpansionMode_NoExpansion_deserializes_correctly()
    {
        var json = """{"overrides": [{"id": "q1", "expansionMode": "NoExpansion"}]}""";
        var result = JsonSerializer.Deserialize<QuestOverridesFile>(json, Options);
        result!.Overrides[0].ExpansionMode.Should().Be(ExpansionMode.NoExpansion);
    }
}
