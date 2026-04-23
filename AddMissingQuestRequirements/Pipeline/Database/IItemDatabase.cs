namespace AddMissingQuestRequirements.Pipeline.Database;

/// <summary>Source of truth for the SPT item database.</summary>
public interface IItemDatabase
{
    IReadOnlyDictionary<string, ItemNode> Items { get; }

    /// <summary>Returns the localised display name for the item, or null if not found.</summary>
    string? GetLocaleName(string id);

    /// <summary>Returns the localised description for the item, or null if not found.</summary>
    string? GetLocaleDescription(string id);

    /// <summary>Returns the localised quest name (<c>{questId} name</c>), or null if not found.</summary>
    string? GetQuestName(string questId);

    /// <summary>Returns the localised quest description (<c>{questId} description</c>), or null if not found.</summary>
    string? GetQuestDescription(string questId);

    /// <summary>Returns the localised condition description (bare <c>{conditionId}</c> key), or null if not found.</summary>
    string? GetConditionDescription(string conditionId);
}
