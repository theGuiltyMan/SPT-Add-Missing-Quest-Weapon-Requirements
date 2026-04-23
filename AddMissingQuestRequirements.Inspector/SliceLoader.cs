using System.Text.Json;
using System.Text.RegularExpressions;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Quest;

namespace AddMissingQuestRequirements.Inspector;

/// <summary>
/// Loads real SPT database files (items.json, quests.json, en.json) into in-memory databases.
/// Used by the Inspector CLI and integration smoke tests.
/// </summary>
public static class SliceLoader
{
    public static string TemplatesDir =>
        Environment.GetEnvironmentVariable("SPT_TEMPLATES_DIR")
        ?? LocalExportDir
        ?? string.Empty;

    public static string LocaleEnPath =>
        Environment.GetEnvironmentVariable("SPT_LOCALE_EN_PATH")
        ?? (LocalExportDir is { } d ? Path.Combine(d, "locale_en.json") : string.Empty);

    /// <summary>True iff both items.json and locale_en.json are reachable via the resolved paths.</summary>
    public static bool IsAvailable =>
        !string.IsNullOrEmpty(TemplatesDir)
        && File.Exists(Path.Combine(TemplatesDir, "items.json"))
        && File.Exists(LocaleEnPath);

    // Walk upward from the running binary (or CWD) looking for a checked-in
    // SptDbExporter/export/ slice. Lets integration tests and the Inspector
    // run out of a clean clone without env vars or a local SPT install.
    private static string? LocalExportDir
    {
        get
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start))
                {
                    continue;
                }

                var dir = new DirectoryInfo(start);
                while (dir is not null)
                {
                    var candidate = Path.Combine(dir.FullName, "SptDbExporter", "export");
                    if (File.Exists(Path.Combine(candidate, "items.json")))
                    {
                        return candidate;
                    }
                    dir = dir.Parent;
                }
            }
            return null;
        }
    }

    public static string ItemsJsonPath => Path.Combine(TemplatesDir, "items.json");
    public static string QuestsJsonPath => Path.Combine(TemplatesDir, "quests.json");

    /// <summary>
    /// Loads the full SPT items.json and en.json locale into an <see cref="InMemoryItemDatabase"/>.
    /// Uses hardcoded SPT source paths.
    /// </summary>
    public static InMemoryItemDatabase LoadItemDatabase()
    {
        return LoadItemDatabase(ItemsJsonPath, LocaleEnPath);
    }

    /// <summary>
    /// Loads items and locale from explicit file paths into an <see cref="InMemoryItemDatabase"/>.
    /// </summary>
    public static InMemoryItemDatabase LoadItemDatabase(string itemsJsonPath, string localeJsonPath)
    {
        using var itemsStream = File.OpenRead(itemsJsonPath);
        using var itemsDoc = JsonDocument.Parse(itemsStream);

        var nodes = new List<ItemNode>();
        foreach (var prop in itemsDoc.RootElement.EnumerateObject())
        {
            var entry = prop.Value;
            var id = entry.TryGetProperty("_id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            var name = entry.TryGetProperty("_name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
            var parentRaw = entry.TryGetProperty("_parent", out var parentEl) ? parentEl.GetString() : null;
            var nodeType = entry.TryGetProperty("_type", out var typeEl) ? typeEl.GetString() ?? "Item" : "Item";

            var parentId = string.IsNullOrEmpty(parentRaw) ? null : parentRaw;

            var props = new Dictionary<string, JsonElement>();
            if (entry.TryGetProperty("_props", out var propsEl))
            {
                foreach (var p in propsEl.EnumerateObject())
                {
                    props[p.Name] = p.Value.Clone();
                }
            }

            nodes.Add(new ItemNode
            {
                Id = id,
                Name = name,
                ParentId = parentId,
                NodeType = nodeType,
                Props = props
            });
        }

        var locale = LoadLocale(localeJsonPath);
        return new InMemoryItemDatabase(
            nodes,
            localeNames: locale.Names,
            localeDescriptions: locale.Descriptions,
            questNames: locale.QuestNames,
            questDescriptions: locale.QuestDescriptions,
            conditionDescriptions: locale.ConditionDescriptions);
    }

    /// <summary>
    /// Loads the full SPT quests.json into an <see cref="InMemoryQuestDatabase"/>.
    /// Uses the hardcoded SPT source path.
    /// </summary>
    public static InMemoryQuestDatabase LoadQuestDatabase()
    {
        return LoadQuestDatabase(QuestsJsonPath);
    }

    /// <summary>
    /// Loads quests from an explicit file path into an <see cref="InMemoryQuestDatabase"/>.
    /// Only CounterCreator conditions (with weapon/mod sub-conditions) are extracted.
    /// </summary>
    public static InMemoryQuestDatabase LoadQuestDatabase(string questsJsonPath)
    {
        using var questsStream = File.OpenRead(questsJsonPath);
        using var questsDoc = JsonDocument.Parse(questsStream);

        var quests = new List<QuestNode>();

        foreach (var questProp in questsDoc.RootElement.EnumerateObject())
        {
            var questEl = questProp.Value;
            var questId   = questEl.TryGetProperty("_id",      out var qIdEl)  ? qIdEl.GetString()  ?? questProp.Name : questProp.Name;
            var traderId  = questEl.TryGetProperty("traderId",  out var tEl)    ? tEl.GetString()    ?? string.Empty   : string.Empty;
            var location  = questEl.TryGetProperty("location",  out var locEl)  ? locEl.GetString()  ?? string.Empty   : string.Empty;
            var questType = questEl.TryGetProperty("type",      out var qtEl)   ? qtEl.GetString()   ?? string.Empty   : string.Empty;

            var conditions = new List<ConditionNode>();

            if (questEl.TryGetProperty("conditions", out var conditionsEl))
            {
                foreach (var sectionName in new[] { "AvailableForFinish", "AvailableForStart" })
                {
                    if (!conditionsEl.TryGetProperty(sectionName, out var sectionEl))
                    {
                        continue;
                    }

                    foreach (var topCondEl in sectionEl.EnumerateArray())
                    {
                        var condType = topCondEl.TryGetProperty("conditionType", out var ctEl) ? ctEl.GetString() : null;
                        if (condType != "CounterCreator")
                        {
                            continue;
                        }

                        if (!topCondEl.TryGetProperty("counter", out var counterEl))
                        {
                            continue;
                        }

                        if (!counterEl.TryGetProperty("conditions", out var subConditionsEl))
                        {
                            continue;
                        }

                        var topCondId = topCondEl.TryGetProperty("id", out var topIdEl)
                            ? topIdEl.GetString() ?? string.Empty
                            : string.Empty;

                        int? killCount = null;
                        if (topCondEl.TryGetProperty("value", out var killEl) && killEl.TryGetInt32(out var killInt))
                        {
                            killCount = killInt;
                        }

                        foreach (var subCondEl in subConditionsEl.EnumerateArray())
                        {
                            var subCondId = subCondEl.TryGetProperty("id", out var sidEl) ? sidEl.GetString() ?? string.Empty : string.Empty;

                            var weapon = ReadStringArray(subCondEl, "weapon");
                            var weaponCaliber = ReadStringArray(subCondEl, "weaponCaliber");
                            var weaponModsInclusive = ReadStringArrayArray(subCondEl, "weaponModsInclusive");
                            var weaponModsExclusive = ReadStringArrayArray(subCondEl, "weaponModsExclusive");
                            var enemyTypes   = ReadStringArray(subCondEl, "savageRole");
                            var condLocation = ReadStringArray(subCondEl, "location");
                            string? distance = null;
                            if (subCondEl.TryGetProperty("distance", out var distEl)
                                && distEl.ValueKind != JsonValueKind.Null)
                            {
                                if (distEl.TryGetProperty("value", out var dvEl) &&
                                    dvEl.TryGetInt32(out var dvInt) &&
                                    dvInt > 0)
                                {
                                    var method = distEl.TryGetProperty("compareMethod", out var cmEl)
                                        ? cmEl.GetString() ?? string.Empty
                                        : string.Empty;
                                    distance = $"{method} {dvInt}".Trim();
                                }
                            }

                            var subCondType = subCondEl.TryGetProperty("conditionType", out var sctEl)
                                ? sctEl.GetString() ?? string.Empty
                                : string.Empty;
                            var effectiveKillCount = (subCondType is "Kills" or "Shots") ? killCount : null;

                            conditions.Add(new ConditionNode
                            {
                                Id = subCondId,
                                ParentConditionId = topCondId,
                                ConditionType = "CounterCreator",
                                Weapon = weapon,
                                WeaponCaliber = weaponCaliber,
                                WeaponModsInclusive = weaponModsInclusive,
                                WeaponModsExclusive = weaponModsExclusive,
                                KillCount = effectiveKillCount,
                                EnemyTypes = enemyTypes,
                                ConditionLocation = condLocation,
                                Distance = distance
                            });
                        }
                    }
                }
            }

            quests.Add(new QuestNode
            {
                Id = questId,
                TraderId = traderId,
                Location = location,
                QuestType = questType,
                Conditions = conditions
            });
        }

        return new InMemoryQuestDatabase(quests);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static readonly Regex ItemLocaleKeyPattern = new(@"^([a-f0-9]{24}) (Name|Description|Nickname)$", RegexOptions.Compiled);
    private static readonly Regex QuestLocaleKeyPattern = new(@"^([a-f0-9]{24}) (name|description)$", RegexOptions.Compiled);
    private static readonly Regex ConditionLocaleKeyPattern = new(@"^([a-f0-9]{24})$", RegexOptions.Compiled);

    private sealed class LocaleBundle
    {
        public Dictionary<string, string> Names { get; } = [];
        public Dictionary<string, string> Descriptions { get; } = [];
        public Dictionary<string, string> QuestNames { get; } = [];
        public Dictionary<string, string> QuestDescriptions { get; } = [];
        public Dictionary<string, string> ConditionDescriptions { get; } = [];
    }

    private static LocaleBundle LoadLocale(string localePath)
    {
        using var localeStream = File.OpenRead(localePath);
        using var localeDoc = JsonDocument.Parse(localeStream);

        var bundle = new LocaleBundle();

        var claimedIds = new HashSet<string>();

        foreach (var prop in localeDoc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var value = prop.Value.GetString() ?? string.Empty;

            var itemMatch = ItemLocaleKeyPattern.Match(prop.Name);
            if (itemMatch.Success)
            {
                var itemId = itemMatch.Groups[1].Value;
                claimedIds.Add(itemId);
                var fieldType = itemMatch.Groups[2].Value;
                if (fieldType is "Name" or "Nickname")
                {
                    bundle.Names[itemId] = value;
                }
                else
                {
                    bundle.Descriptions[itemId] = value;
                }
                continue;
            }

            var questMatch = QuestLocaleKeyPattern.Match(prop.Name);
            if (questMatch.Success)
            {
                var questId = questMatch.Groups[1].Value;
                claimedIds.Add(questId);
                var questFieldType = questMatch.Groups[2].Value;
                if (questFieldType == "name")
                {
                    bundle.QuestNames[questId] = value;
                }
                else
                {
                    bundle.QuestDescriptions[questId] = value;
                }
                continue;
            }

            if (ConditionLocaleKeyPattern.IsMatch(prop.Name)
                && !string.IsNullOrEmpty(value)
                && !claimedIds.Contains(prop.Name))
            {
                bundle.ConditionDescriptions[prop.Name] = value;
            }
        }

        foreach (var claimed in claimedIds)
        {
            bundle.ConditionDescriptions.Remove(claimed);
        }

        return bundle;
    }

    private static List<string> ReadStringArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var arrEl) || arrEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var result = new List<string>();
        foreach (var item in arrEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                result.Add(item.GetString() ?? string.Empty);
            }
        }
        return result;
    }

    private static List<List<string>> ReadStringArrayArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var outerEl) || outerEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var result = new List<List<string>>();
        foreach (var innerEl in outerEl.EnumerateArray())
        {
            if (innerEl.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            var inner = new List<string>();
            foreach (var item in innerEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    inner.Add(item.GetString() ?? string.Empty);
                }
            }
            result.Add(inner);
        }
        return result;
    }
}
