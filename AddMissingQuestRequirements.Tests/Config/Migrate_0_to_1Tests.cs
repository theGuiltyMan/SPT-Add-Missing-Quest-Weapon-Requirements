using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Config;

public class Migrate_0_to_1Tests
{
    [Fact]
    public void Renames_categorizeWithLessRestrive_typo()
    {
        var json = JsonNode.Parse("""
        {
            "categorizeWithLessRestrive": true,
            "kindOf": {}
        }
        """)!.AsObject();

        var result = Migrations.v0_to_v1(json);

        result.ContainsKey("categorizeWithLessRestrive").Should().BeFalse();
        result["categorizeWithLessRestrictive"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Removes_delay_field()
    {
        var json = JsonNode.Parse("""{"delay":0,"kindOf":{}}""")!.AsObject();

        var result = Migrations.v0_to_v1(json);

        result.ContainsKey("delay").Should().BeFalse();
    }

    [Fact]
    public void Preserves_all_other_fields()
    {
        var json = JsonNode.Parse("""
        {
            "kindOf": {"Revolver":"Pistol"},
            "BlackListedItems": ["item_a"],
            "debug": false,
            "logType": "file"
        }
        """)!.AsObject();

        var result = Migrations.v0_to_v1(json);

        result.ContainsKey("kindOf").Should().BeTrue();
        result["BlackListedItems"]!.AsArray().Should().HaveCount(1);
        result["debug"]!.GetValue<bool>().Should().BeFalse();
        result["logType"]!.GetValue<string>().Should().Be("file");
    }

    [Fact]
    public void Is_noop_when_typo_field_absent()
    {
        // Already using the correct field name (e.g. manually corrected config)
        var json = JsonNode.Parse("""{"categorizeWithLessRestrictive":false}""")!.AsObject();

        var result = Migrations.v0_to_v1(json);

        result["categorizeWithLessRestrictive"]!.GetValue<bool>().Should().BeFalse();
    }
}
