using System.Text.Json;
using AddMissingQuestRequirements.Pipeline.Database;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace AddMissingQuestRequirements.Spt;

/// <summary>
/// Adapter over <see cref="DatabaseServer"/>'s item table and
/// <see cref="LocaleService"/>'s client-locale dictionary, exposing the shape
/// the pipeline phases expect via <see cref="IItemDatabase"/>.
/// </summary>
/// <remarks>
/// Construction builds a snapshot of the item table — iterating
/// <c>DatabaseServer.GetTables().Templates.Items</c> once and mapping every
/// <see cref="TemplateItem"/> to a framework-agnostic <see cref="ItemNode"/>.
/// The client-locale dictionary is fetched once via
/// <c>LocaleService.GetLocaleDb()</c> and kept for cheap <c>TryGetValue</c>
/// lookups. Locale keys follow the game convention:
/// <c>"{id} Name"</c>, <c>"{id} description"</c>, <c>"{questId} name"</c>,
/// <c>"{questId} description"</c>, and bare <c>"{conditionId}"</c>.
/// </remarks>
public sealed class SptItemDatabase : IItemDatabase
{
    private readonly Dictionary<string, ItemNode> _items;
    private readonly IReadOnlyDictionary<string, string> _locale;

    public SptItemDatabase(
        DatabaseServer databaseServer,
        LocaleService localeService,
        ISptLogger<SptItemDatabase> logger)
    {
        _locale = localeService.GetLocaleDb() ?? new Dictionary<string, string>();

        var raw = databaseServer.GetTables().Templates?.Items
            ?? throw new InvalidOperationException(
                "DatabaseServer.GetTables().Templates.Items is null — database not loaded.");

        _items = new Dictionary<string, ItemNode>(raw.Count);

        foreach (var kvp in raw)
        {
            var template = kvp.Value;
            var id = template.Id.ToString();
            _items[id] = new ItemNode
            {
                Id = id,
                Name = template.Name ?? string.Empty,
                ParentId = template.Parent.ToString(),
                NodeType = template.Type ?? string.Empty,
                Props = SerializeProps(template.Properties),
            };
        }

        logger.Debug(
            $"SptItemDatabase loaded {_items.Count} items, {_locale.Count} locale keys.");
    }

    public IReadOnlyDictionary<string, ItemNode> Items => _items;

    public string? GetLocaleName(string id)
    {
        return Get($"{id} Name");
    }

    public string? GetLocaleDescription(string id)
    {
        return Get($"{id} description");
    }

    public string? GetQuestName(string questId)
    {
        return Get($"{questId} name");
    }

    public string? GetQuestDescription(string questId)
    {
        return Get($"{questId} description");
    }

    public string? GetConditionDescription(string conditionId)
    {
        return Get(conditionId);
    }

    private string? Get(string key)
    {
        return _locale.TryGetValue(key, out var value) ? value : null;
    }

    private static Dictionary<string, JsonElement> SerializeProps(TemplateItemProperties? properties)
    {
        if (properties is null)
        {
            return [];
        }

        var element = JsonSerializer.SerializeToElement(properties);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<string, JsonElement>();
        foreach (var prop in element.EnumerateObject())
        {
            result[prop.Name] = prop.Value.Clone();
        }
        return result;
    }
}
