namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>Flat DTO representing a quest and its patchable conditions.</summary>
public sealed class QuestNode
{
    public string Id { get; init; } = string.Empty;
    public string TraderId { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string QuestType { get; init; } = string.Empty;
    public List<ConditionNode> Conditions { get; init; } = [];
}
