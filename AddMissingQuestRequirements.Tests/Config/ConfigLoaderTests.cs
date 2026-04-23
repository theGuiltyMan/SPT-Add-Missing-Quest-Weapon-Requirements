using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Config;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Config;

public class ConfigLoaderTests
{
    // Minimal versioned config type for testing
    private sealed class TestConfig : IVersionedConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public int? Version { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? NewField { get; init; }
    }

    private static JsonObject Rename_old_to_new(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("OldField", out var val))
        {
            obj.Remove("OldField");
            obj["NewField"] = val?.DeepClone();
        }
        return obj;
    }

    [Fact]
    public void Loads_current_version_without_migration()
    {
        var jsonc = """{"version":1,"name":"hello","NewField":"present"}""";

        var result = ConfigLoader.LoadFromString<TestConfig>(
            jsonc, currentVersion: 1, migrations: [Rename_old_to_new]);

        result.Config.Name.Should().Be("hello");
        result.Config.NewField.Should().Be("present");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Migrates_v0_config_before_deserializing()
    {
        var jsonc = """{"version":0,"name":"old","OldField":"migrated"}""";

        var result = ConfigLoader.LoadFromString<TestConfig>(
            jsonc, currentVersion: 1, migrations: [Rename_old_to_new]);

        result.Config.Name.Should().Be("old");
        result.Config.NewField.Should().Be("migrated");
    }

    [Fact]
    public void Missing_version_treated_as_v0()
    {
        var jsonc = """{"name":"no_version","OldField":"yes"}""";

        var result = ConfigLoader.LoadFromString<TestConfig>(
            jsonc, currentVersion: 1, migrations: [Rename_old_to_new]);

        result.Config.NewField.Should().Be("yes");
    }

    [Fact]
    public void Newer_version_returns_warning()
    {
        var jsonc = """{"version":99,"name":"future"}""";

        var result = ConfigLoader.LoadFromString<TestConfig>(
            jsonc, currentVersion: 1, migrations: [Rename_old_to_new]);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("99");
        result.Config.Name.Should().Be("future");
    }

    [Fact]
    public void Missing_file_returns_default_instance()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonc");
        // file does not exist

        var result = ConfigLoader.LoadFromFile<TestConfig>(
            path, currentVersion: 1, migrations: [Rename_old_to_new]);

        result.Config.Should().NotBeNull();
        result.Config.Version.Should().BeNull();
    }

    [Fact]
    public void Handles_JSONC_comments_and_trailing_commas()
    {
        var jsonc = """
        {
            // comment
            "version": 1,
            "name": "jsonc_test",
        }
        """;

        var result = ConfigLoader.LoadFromString<TestConfig>(
            jsonc, currentVersion: 1, migrations: [Rename_old_to_new]);

        result.Config.Name.Should().Be("jsonc_test");
    }
}
