namespace AddMissingQuestRequirements.Pipeline.Database;

/// <summary>Test double — backed by in-memory collections.</summary>
public sealed class InMemoryItemDatabase : IItemDatabase
{
    private readonly Dictionary<string, ItemNode> _items;
    private readonly Dictionary<string, string> _localeNames;
    private readonly Dictionary<string, string> _localeDescriptions;
    private readonly Dictionary<string, string> _questNames;
    private readonly Dictionary<string, string> _questDescriptions;
    private readonly Dictionary<string, string> _conditionDescriptions;

    public InMemoryItemDatabase(
        IEnumerable<ItemNode> items,
        Dictionary<string, string>? localeNames = null,
        Dictionary<string, string>? localeDescriptions = null,
        Dictionary<string, string>? questNames = null,
        Dictionary<string, string>? questDescriptions = null,
        Dictionary<string, string>? conditionDescriptions = null)
    {
        _items = items.ToDictionary(i => i.Id);
        _localeNames = localeNames ?? [];
        _localeDescriptions = localeDescriptions ?? [];
        _questNames = questNames ?? [];
        _questDescriptions = questDescriptions ?? [];
        _conditionDescriptions = conditionDescriptions ?? [];
    }

    public static InMemoryItemDatabase FromItemsOnly(IReadOnlyDictionary<string, ItemNode> items)
    {
        return new InMemoryItemDatabase(items.Values);
    }

    public IReadOnlyDictionary<string, ItemNode> Items => _items;

    public string? GetLocaleName(string id)
    {
        return _localeNames.GetValueOrDefault(id);
    }

    public string? GetLocaleDescription(string id)
    {
        return _localeDescriptions.GetValueOrDefault(id);
    }

    public string? GetQuestName(string questId)
    {
        return _questNames.GetValueOrDefault(questId);
    }

    public string? GetQuestDescription(string questId)
    {
        return _questDescriptions.GetValueOrDefault(questId);
    }

    public string? GetConditionDescription(string conditionId)
    {
        return _conditionDescriptions.GetValueOrDefault(conditionId);
    }
}
