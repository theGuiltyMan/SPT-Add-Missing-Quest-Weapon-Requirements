using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Models;

public class AttachmentOverridesFileTests
{
    [Fact]
    public void Deserializes_all_fields()
    {
        var json = """
            {
                "version": 1,
                "overrideBehaviour": "MERGE",
                "manualAttachmentTypeOverrides": { "scope_x": "Scope,TacticalScope" },
                "canBeUsedAs": { "scope_a": ["scope_b"] },
                "aliasNameStripWords": ["FDE"]
            }
            """;

        var result = ConfigLoader.LoadFromString<AttachmentOverridesFile>(json, 1, []);
        var file   = result.Config;

        file.Version.Should().Be(1);
        file.OverrideBehaviour.Should().Be(OverrideBehaviour.MERGE);
        file.ManualAttachmentTypeOverrides.Should().ContainKey("scope_x")
            .WhoseValue.Should().Be("Scope,TacticalScope");
        file.CanBeUsedAs.Should().ContainKey("scope_a");
        file.CanBeUsedAs["scope_a"].Should().ContainSingle()
            .Which.Value.Should().Be("scope_b");
        file.AliasNameStripWords.Should().Equal("FDE");
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var result = ConfigLoader.LoadFromFile<AttachmentOverridesFile>(
            "nonexistent.jsonc", 1, []);
        var file   = result.Config;

        file.Version.Should().BeNull();
        file.OverrideBehaviour.Should().Be(OverrideBehaviour.IGNORE);
        file.ManualAttachmentTypeOverrides.Should().BeEmpty();
        file.CanBeUsedAs.Should().BeEmpty();
        file.AliasNameStripWords.Should().BeEmpty();
    }

    [Fact]
    public void CanBeUsedAs_supports_Overridable_delete_syntax()
    {
        var json = """
            {
                "version": 1,
                "canBeUsedAs": {
                    "scope_a": [
                        "scope_b",
                        { "value": "scope_c", "behaviour": "DELETE" }
                    ]
                }
            }
            """;

        var result  = ConfigLoader.LoadFromString<AttachmentOverridesFile>(json, 1, []);
        var aliases = result.Config.CanBeUsedAs["scope_a"];

        aliases.Should().HaveCount(2);
        aliases[0].Value.Should().Be("scope_b");
        aliases[0].Behaviour.Should().BeNull();
        aliases[1].Value.Should().Be("scope_c");
        aliases[1].Behaviour.Should().Be(OverrideBehaviour.DELETE);
    }
}
