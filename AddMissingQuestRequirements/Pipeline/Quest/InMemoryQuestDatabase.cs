namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>Test double — backed by an in-memory dictionary keyed by quest ID.</summary>
public sealed class InMemoryQuestDatabase : IQuestDatabase
{
    public InMemoryQuestDatabase(IEnumerable<QuestNode> quests)
    {
        Quests = quests.ToDictionary(q => q.Id);
    }

    public IReadOnlyDictionary<string, QuestNode> Quests { get; }
}
