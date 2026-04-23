using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Util;

public class JsoncReaderTests
{
    private record SimpleModel(string Name, int Count);

    [Fact]
    public void Deserializes_plain_json()
    {
        var result = JsoncReader.Deserialize<SimpleModel>("""{"name":"test","count":3}""");
        result!.Name.Should().Be("test");
        result.Count.Should().Be(3);
    }

    [Fact]
    public void Handles_line_comments()
    {
        var jsonc = """
        {
            // this is a comment
            "name": "hello",
            "count": 1
        }
        """;
        var result = JsoncReader.Deserialize<SimpleModel>(jsonc);
        result!.Name.Should().Be("hello");
    }

    [Fact]
    public void Handles_block_comments()
    {
        var jsonc = """
        {
            /* block comment */
            "name": "world",
            "count": 2
        }
        """;
        var result = JsoncReader.Deserialize<SimpleModel>(jsonc);
        result!.Name.Should().Be("world");
    }

    [Fact]
    public void Handles_trailing_commas()
    {
        var jsonc = """
        {
            "name": "trail",
            "count": 5,
        }
        """;
        var result = JsoncReader.Deserialize<SimpleModel>(jsonc);
        result!.Name.Should().Be("trail");
        result.Count.Should().Be(5);
    }

    [Fact]
    public void Handles_trailing_commas_in_arrays()
    {
        var jsonc = """["a","b","c",]""";
        var result = JsoncReader.Deserialize<List<string>>(jsonc);
        result.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void Returns_null_for_null_json()
    {
        var result = JsoncReader.Deserialize<SimpleModel>("null");
        result.Should().BeNull();
    }
}
