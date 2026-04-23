using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Config;

public class ConfigMigratorTests
{
    // A migration that renames "old_field" to "new_field"
    private static JsonObject Rename_old_to_new(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("old_field", out var val))
        {
            obj.Remove("old_field");
            obj["new_field"] = val?.DeepClone();
        }
        return obj;
    }

    // A migration that adds "added_in_v2": true
    private static JsonObject Add_v2_field(JsonObject obj)
    {
        obj["added_in_v2"] = true;
        return obj;
    }

    [Fact]
    public void Already_at_current_version_is_noop()
    {
        var json = """{"version":1,"name":"test"}""";
        var node = JsonNode.Parse(json)!.AsObject();

        var result = ConfigMigrator.Migrate(node, currentVersion: 1, [Rename_old_to_new]);

        result.Warnings.Should().BeEmpty();
        result.Json["name"]!.GetValue<string>().Should().Be("test");
        result.Json["version"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void Missing_version_field_treated_as_v0_triggers_migration()
    {
        var json = """{"old_field":"value"}""";
        var node = JsonNode.Parse(json)!.AsObject();

        var result = ConfigMigrator.Migrate(node, currentVersion: 1, [Rename_old_to_new]);

        result.Warnings.Should().BeEmpty();
        result.Json.ContainsKey("old_field").Should().BeFalse();
        result.Json["new_field"]!.GetValue<string>().Should().Be("value");
        result.Json["version"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void Version_0_triggers_migration()
    {
        var json = """{"version":0,"old_field":"value"}""";
        var node = JsonNode.Parse(json)!.AsObject();

        var result = ConfigMigrator.Migrate(node, currentVersion: 1, [Rename_old_to_new]);

        result.Json.ContainsKey("old_field").Should().BeFalse();
        result.Json["new_field"]!.GetValue<string>().Should().Be("value");
    }

    [Fact]
    public void Chain_v0_to_v2_runs_both_migrations()
    {
        var json = """{"version":0,"old_field":"hello"}""";
        var node = JsonNode.Parse(json)!.AsObject();

        var result = ConfigMigrator.Migrate(node, currentVersion: 2, [Rename_old_to_new, Add_v2_field]);

        result.Json["new_field"]!.GetValue<string>().Should().Be("hello");
        result.Json["added_in_v2"]!.GetValue<bool>().Should().BeTrue();
        result.Json["version"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void Partial_chain_v1_to_v2_skips_first_migration()
    {
        var json = """{"version":1,"old_field":"still_here"}""";
        var node = JsonNode.Parse(json)!.AsObject();

        var result = ConfigMigrator.Migrate(node, currentVersion: 2, [Rename_old_to_new, Add_v2_field]);

        // Rename_old_to_new should NOT have run (already past v0→v1)
        result.Json.ContainsKey("old_field").Should().BeTrue();
        result.Json["added_in_v2"]!.GetValue<bool>().Should().BeTrue();
        result.Json["version"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void Newer_than_expected_produces_warning_and_returns_unchanged()
    {
        var json = """{"version":99,"name":"future"}""";
        var node = JsonNode.Parse(json)!.AsObject();

        var result = ConfigMigrator.Migrate(node, currentVersion: 1, [Rename_old_to_new]);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("99");
        result.Json["name"]!.GetValue<string>().Should().Be("future");
    }
}
