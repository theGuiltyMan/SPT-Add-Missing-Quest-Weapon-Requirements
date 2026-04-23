namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Resolves human-readable names for item and quest IDs for log output.
/// Implementations may back onto the SPT locale database, return raw IDs
/// for tests, or any other lookup strategy.
/// </summary>
public interface INameResolver
{
    /// <summary>
    /// Returns <c>"&lt;name&gt; (id)"</c> when the item has a locale name;
    /// otherwise just the raw <paramref name="itemId"/>.
    /// </summary>
    string FormatItem(string itemId);

    /// <summary>
    /// Returns the quest's locale name, or the raw <paramref name="questId"/>
    /// when no locale entry exists.
    /// </summary>
    string GetQuestName(string questId);
}
