using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Models;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Models;

public class OverridableTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void BareString_Deserializes_to_value_with_null_behaviour()
    {
        var json = "\"weapon_abc\"";
        var result = JsonSerializer.Deserialize<Overridable<string>>(json, Options);
        result!.Value.Should().Be("weapon_abc");
        result.Behaviour.Should().BeNull();
    }

    [Fact]
    public void ObjectForm_Deserializes_value_and_behaviour()
    {
        var json = """{"value":"weapon_abc","behaviour":"DELETE"}""";
        var result = JsonSerializer.Deserialize<Overridable<string>>(json, Options);
        result!.Value.Should().Be("weapon_abc");
        result.Behaviour.Should().Be(OverrideBehaviour.DELETE);
    }

    [Fact]
    public void BareEnum_Deserializes_to_value_with_null_behaviour()
    {
        var json = "\"MERGE\"";
        var result = JsonSerializer.Deserialize<Overridable<OverrideBehaviour>>(json, Options);
        result!.Value.Should().Be(OverrideBehaviour.MERGE);
        result.Behaviour.Should().BeNull();
    }

    [Fact]
    public void ObjectForm_with_enum_Deserializes()
    {
        var json = """{"value":"REPLACE","behaviour":"DELETE"}""";
        var result = JsonSerializer.Deserialize<Overridable<OverrideBehaviour>>(json, Options);
        result!.Value.Should().Be(OverrideBehaviour.REPLACE);
        result.Behaviour.Should().Be(OverrideBehaviour.DELETE);
    }

    [Fact]
    public void BareInteger_Deserializes_to_value_with_null_behaviour()
    {
        var json = "42";
        var result = JsonSerializer.Deserialize<Overridable<int>>(json, Options);
        result!.Value.Should().Be(42);
        result.Behaviour.Should().BeNull();
    }
}
