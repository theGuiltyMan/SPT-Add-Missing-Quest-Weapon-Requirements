using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Models;

public class OverrideBehaviourTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData("\"IGNORE\"", OverrideBehaviour.IGNORE)]
    [InlineData("\"MERGE\"", OverrideBehaviour.MERGE)]
    [InlineData("\"REPLACE\"", OverrideBehaviour.REPLACE)]
    [InlineData("\"DELETE\"", OverrideBehaviour.DELETE)]
    public void Deserializes_from_string(string json, OverrideBehaviour expected)
    {
        var result = JsonSerializer.Deserialize<OverrideBehaviour>(json, Options);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(OverrideBehaviour.IGNORE, "\"IGNORE\"")]
    [InlineData(OverrideBehaviour.MERGE, "\"MERGE\"")]
    [InlineData(OverrideBehaviour.REPLACE, "\"REPLACE\"")]
    [InlineData(OverrideBehaviour.DELETE, "\"DELETE\"")]
    public void Serializes_to_string(OverrideBehaviour value, string expectedJson)
    {
        var result = JsonSerializer.Serialize(value, Options);
        result.Should().Be(expectedJson);
    }
}
