using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Models;

public class RulesFileTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Deserializes_rules_file_with_multiple_rules()
    {
        var json = """
        {
            "version": 1,
            "OverrideBehaviour": "IGNORE",
            "Rules": [
                {
                    "comment": "Bolt-action snipers",
                    "conditions": {
                        "hasAncestor": "SniperRifle",
                        "property": { "BoltAction": true }
                    },
                    "type": "BoltActionSniperRifle",
                    "alsoAs": ["SniperRifle"]
                },
                {
                    "conditions": { "hasAncestor": "Weapon" },
                    "type": "{directChildOf:Weapon}"
                }
            ]
        }
        """;

        var result = JsonSerializer.Deserialize<RulesFile>(json, Options);

        result!.Version.Should().Be(1);
        result.OverrideBehaviour.Should().Be(OverrideBehaviour.IGNORE);
        result.Rules.Should().HaveCount(2);

        var first = result.Rules[0];
        first.Comment.Should().Be("Bolt-action snipers");
        first.Conditions.Should().ContainKey("hasAncestor");
        first.Conditions["hasAncestor"].GetString().Should().Be("SniperRifle");
        first.Type.Should().Be("BoltActionSniperRifle");
        first.AlsoAs.Should().BeEquivalentTo(["SniperRifle"]);

        var second = result.Rules[1];
        second.Comment.Should().BeNull();
        second.Conditions.Should().ContainKey("hasAncestor");
        second.Conditions["hasAncestor"].GetString().Should().Be("Weapon");
        second.Type.Should().Be("{directChildOf:Weapon}");
        second.AlsoAs.Should().BeEmpty();
    }

    [Fact]
    public void TypeRule_Priority_defaults_to_null()
    {
        var json = """
        {
            "Rules": [
                { "conditions": {}, "type": "MyType" }
            ]
        }
        """;
        var result = JsonSerializer.Deserialize<RulesFile>(json, Options);
        result!.Rules[0].Priority.Should().BeNull();
    }

    [Fact]
    public void TypeRule_Priority_before_defaults_deserializes()
    {
        var json = """
        {
            "Rules": [
                { "conditions": {}, "type": "MyType", "priority": "before-defaults" }
            ]
        }
        """;
        var result = JsonSerializer.Deserialize<RulesFile>(json, Options);
        result!.Rules[0].Priority.Should().Be("before-defaults");
    }
}
