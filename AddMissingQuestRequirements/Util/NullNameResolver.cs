namespace AddMissingQuestRequirements.Util;

/// <summary>
/// No-op <see cref="INameResolver"/> that returns raw IDs unchanged.
/// Used by tests and fallback code paths with no locale data available.
/// </summary>
public sealed class NullNameResolver : INameResolver
{
    public static readonly NullNameResolver Instance = new();

    private NullNameResolver()
    {
    }

    public string FormatItem(string itemId)
    {
        return itemId;
    }

    public string GetQuestName(string questId)
    {
        return questId;
    }
}
