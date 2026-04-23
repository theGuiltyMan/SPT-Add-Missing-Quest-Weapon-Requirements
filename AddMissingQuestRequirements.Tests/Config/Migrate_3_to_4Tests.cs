using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Config;

public class Migrate_3_to_4Tests
{
    [Fact]
    public void LogType_IsStripped()
    {
        var input = JsonNode.Parse("""{"logType":"file","debug":true}""")!.AsObject();
        var result = Migrations.v3_to_v4_Config(input);

        result.ContainsKey("logType").Should().BeFalse();
        result["debug"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void MissingLogType_IsNoOp()
    {
        var input = JsonNode.Parse("""{"debug":false}""")!.AsObject();
        var result = Migrations.v3_to_v4_Config(input);

        result.ContainsKey("logType").Should().BeFalse();
        result["debug"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void IntegrationViaConfigLoader_V3ConfigWithLogType_LoadsCleanly()
    {
        const string jsonc = """
            {
              "version": 3,
              "logType": "file",
              "debug": true
            }
            """;

        var result = ConfigLoader.LoadFromString<ModConfig>(
            jsonc,
            currentVersion: 4,
            migrations: [
                Migrations.v0_to_v1,
                Migrations.v1_to_v2_Config,
                Migrations.v2_to_v3_Config,
                Migrations.v3_to_v4_Config,
            ]);

        result.Config.Debug.Should().BeTrue();
    }
}
