using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Config;

namespace AddMissingQuestRequirements.Models;

public sealed class QuestOverridesFile : IVersionedConfig
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("overrideBehaviour")]
    public OverrideBehaviour OverrideBehaviour { get; init; } = OverrideBehaviour.IGNORE;

    [JsonPropertyName("excludedQuests")]
    public List<string> ExcludedQuests { get; init; } = [];

    [JsonPropertyName("overrides")]
    public List<QuestOverrideEntry> Overrides { get; init; } = [];
}
