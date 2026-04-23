using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Shared;

/// <summary>
/// <see cref="INameResolver"/> backed by an <see cref="IItemDatabase"/>.
/// Falls back to raw IDs whenever a locale entry is missing or blank.
/// </summary>
public sealed class ItemDbNameResolver : INameResolver
{
    private readonly IItemDatabase _db;

    public ItemDbNameResolver(IItemDatabase db)
    {
        _db = db;
    }

    public string FormatItem(string itemId)
    {
        var name = _db.GetLocaleName(itemId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return itemId;
        }
        return $"{name} ({itemId})";
    }

    public string GetQuestName(string questId)
    {
        var name = _db.GetQuestName(questId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return questId;
        }
        return name;
    }
}
