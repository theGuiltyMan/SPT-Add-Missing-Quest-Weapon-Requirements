using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Config;

namespace AddMissingQuestRequirements.Models;

public sealed class RulesFile : IVersionedConfig
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("OverrideBehaviour")]
    public OverrideBehaviour OverrideBehaviour { get; init; } = OverrideBehaviour.IGNORE;

    [JsonPropertyName("Rules")]
    public List<TypeRule> Rules { get; init; } = [];
}
