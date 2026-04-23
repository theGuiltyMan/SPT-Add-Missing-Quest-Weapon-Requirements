namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>Read-only view of the quest database, keyed by quest ID.</summary>
public interface IQuestDatabase
{
    IReadOnlyDictionary<string, QuestNode> Quests { get; }
}
