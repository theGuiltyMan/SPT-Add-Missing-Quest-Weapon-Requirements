using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Attachment;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Weapon;

namespace AddMissingQuestRequirements.Reporting;

/// <summary>
/// Builds an <see cref="InspectorResult"/> from a completed pipeline run.
/// Consumed by both the standalone Inspector CLI and the SPT loader's debug-report path.
/// The pipeline execution itself lives in the caller; this class only shapes the output.
/// </summary>
public static class ReportBuilder
{
    private static readonly TypeSelector _typeSelector = new();

    public static InspectorResult Build(
        OverriddenSettings settings,
        ModConfig config,
        IItemDatabase itemDb,
        IQuestDatabase questDb,
        IReadOnlyDictionary<ConditionNode, List<string>> prePatchWeapons,
        IReadOnlyDictionary<ConditionNode, (List<List<string>> Incl, List<List<string>> Excl)> prePatchMods,
        CategorizationResult weaponCategorization,
        AttachmentCategorizationResult attachmentCategorization)
    {
        return new InspectorResult
        {
            Settings = BuildSettingsSnapshot(settings),
            Weapons = BuildWeaponList(weaponCategorization, itemDb),
            Types = BuildTypeMap(weaponCategorization, itemDb),
            Attachments = BuildAttachmentList(attachmentCategorization, itemDb),
            AttachmentTypes = BuildAttachmentTypeMap(attachmentCategorization, itemDb),
            Quests = BuildQuestList(questDb, prePatchWeapons, prePatchMods, itemDb, weaponCategorization, config, settings),
        };
    }

    private static SettingsSnapshot BuildSettingsSnapshot(OverriddenSettings settings)
    {
        return new SettingsSnapshot
        {
            ExcludedQuestCount = settings.ExcludedQuests.Count,
            ExcludedQuests = [..settings.ExcludedQuests],
            ManualTypeOverrides = settings.ManualTypeOverrides,
            Rules = settings.TypeRules.Select(r => new RuleSnapshot
            {
                Type = r.Type,
                Conditions = BuildRuleConditions(r.Conditions),
                AlsoAs = r.AlsoAs,
                Priority = r.Priority,
            }).ToList(),
            AttachmentRules = settings.AttachmentTypeRules.Select(r => new RuleSnapshot
            {
                Type = r.Type,
                Conditions = BuildRuleConditions(r.Conditions),
                AlsoAs = r.AlsoAs,
                Priority = r.Priority,
            }).ToList(),
            IncludeParentCategories = settings.Config.IncludeParentCategories,
            BestCandidateExpansion = settings.Config.BestCandidateExpansion,
        };
    }

    private static List<WeaponResult> BuildWeaponList(CategorizationResult cat, IItemDatabase db)
    {
        return cat.WeaponToType
            .Select(kvp =>
            {
                var name = db.GetLocaleName(kvp.Key)
                    ?? db.Items.GetValueOrDefault(kvp.Key)?.Name
                    ?? kvp.Key;
                var caliber = cat.WeaponToCaliber.GetValueOrDefault(kvp.Key);
                return new WeaponResult
                {
                    Id = kvp.Key,
                    Name = name,
                    Types = [..kvp.Value],
                    Caliber = caliber,
                };
            })
            .OrderBy(w => w.Name)
            .ToList();
    }

    private static Dictionary<string, List<WeaponResult>> BuildTypeMap(
        CategorizationResult cat,
        IItemDatabase db)
    {
        return cat.WeaponTypes.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Select(id =>
                {
                    var name = db.GetLocaleName(id)
                        ?? db.Items.GetValueOrDefault(id)?.Name
                        ?? id;
                    return new WeaponResult
                    {
                        Id = id,
                        Name = name,
                        Types = cat.WeaponToType.TryGetValue(id, out var types) ? [..types] : [],
                        Caliber = cat.WeaponToCaliber.GetValueOrDefault(id),
                    };
                })
                .OrderBy(w => w.Name)
                .ToList()
        );
    }

    private static List<AttachmentResult> BuildAttachmentList(
        AttachmentCategorizationResult cat,
        IItemDatabase db)
    {
        return cat.AttachmentToType
            .Select(kvp =>
            {
                var name = db.GetLocaleName(kvp.Key)
                    ?? db.Items.GetValueOrDefault(kvp.Key)?.Name
                    ?? kvp.Key;
                return new AttachmentResult
                {
                    Id = kvp.Key,
                    Name = name,
                    Types = [..kvp.Value],
                };
            })
            .OrderBy(a => a.Name)
            .ToList();
    }

    private static Dictionary<string, List<AttachmentResult>> BuildAttachmentTypeMap(
        AttachmentCategorizationResult cat,
        IItemDatabase db)
    {
        return cat.AttachmentTypes.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Select(id =>
                {
                    var name = db.GetLocaleName(id)
                        ?? db.Items.GetValueOrDefault(id)?.Name
                        ?? id;
                    return new AttachmentResult
                    {
                        Id = id,
                        Name = name,
                        Types = cat.AttachmentToType.TryGetValue(id, out var types) ? [..types] : [],
                    };
                })
                .OrderBy(a => a.Name)
                .ToList()
        );
    }

    private static List<QuestResult> BuildQuestList(
        IQuestDatabase questDb,
        IReadOnlyDictionary<ConditionNode, List<string>> prePatch,
        IReadOnlyDictionary<ConditionNode, (List<List<string>> Incl, List<List<string>> Excl)> prePatchMods,
        IItemDatabase db,
        CategorizationResult cat,
        ModConfig config,
        OverriddenSettings settings)
    {
        return questDb.Quests.Values
            .Select(quest =>
            {
                var conditions = quest.Conditions
                    .Where(c =>
                        (prePatch.TryGetValue(c, out var pre) && pre.Count > 0)
                        || c.Weapon.Count > 0
                        || c.WeaponModsInclusive.Count > 0
                        || c.WeaponModsExclusive.Count > 0
                        || (prePatchMods.TryGetValue(c, out var preM) &&
                            (preM.Incl.Count > 0 || preM.Excl.Count > 0)))
                    .Select(c =>
                    {
                        var before = prePatch.TryGetValue(c, out var pre) ? pre : [];
                        var (matched, nextBest, nextBestCount) = ComputeTypeMatch(before, cat, config);
                        var caliberFilter = c.WeaponCaliber.Count > 0 ? string.Join(", ", c.WeaponCaliber) : null;
                        var overrideEntry = QuestOverrideResolver.Resolve(settings, quest.Id, c.Id);
                        var expansionMode = (overrideEntry?.ExpansionMode ?? ExpansionMode.Auto).ToString();
                        var overrideIncluded = overrideEntry?.IncludedWeapons.ToList() ?? [];
                        var overrideExcluded = overrideEntry?.ExcludedWeapons.ToList() ?? [];

                        List<List<string>> preIncl;
                        List<List<string>> preExcl;
                        if (prePatchMods.TryGetValue(c, out var pm))
                        {
                            preIncl = pm.Incl;
                            preExcl = pm.Excl;
                        }
                        else
                        {
                            preIncl = [];
                            preExcl = [];
                        }

                        return new ConditionResult
                        {
                            Id = c.Id,
                            Description = LookupConditionDescription(c, db),
                            Before = before.Select(id => ToWeaponRef(id, db)).ToList(),
                            After = c.Weapon.Select(id => ToWeaponRef(id, db)).ToList(),
                            MatchedType = matched,
                            NextBestType = nextBest,
                            NextBestTypeCount = nextBestCount,
                            ExpansionMode = expansionMode,
                            OverrideMatched = overrideEntry is not null,
                            OverrideIncludedWeapons = overrideIncluded,
                            OverrideExcludedWeapons = overrideExcluded,
                            KillCount = c.KillCount,
                            EnemyTypes = c.EnemyTypes,
                            ConditionLocation = c.ConditionLocation,
                            CaliberFilter = caliberFilter,
                            Distance = c.Distance,
                            ModsInclusiveBefore = ToWeaponRefGroups(preIncl, db),
                            ModsInclusiveAfter = ToWeaponRefGroups(c.WeaponModsInclusive, db),
                            ModsExclusiveBefore = ToWeaponRefGroups(preExcl, db),
                            ModsExclusiveAfter = ToWeaponRefGroups(c.WeaponModsExclusive, db),
                            ModsExpansionMode = (overrideEntry?.ModsExpansionMode ?? ExpansionMode.Auto).ToString(),
                            OverrideIncludedMods = overrideEntry?.IncludedMods.ToList() ?? [],
                            OverrideExcludedMods = overrideEntry?.ExcludedMods.ToList() ?? [],
                        };
                    })
                    .ToList();

                var questName = db.GetQuestName(quest.Id) ?? quest.Id;
                var questDescription = db.GetQuestDescription(quest.Id);

                var traderName = !string.IsNullOrEmpty(quest.TraderId)
                    ? db.GetLocaleName(quest.TraderId) ?? quest.TraderId
                    : null;

                return new QuestResult
                {
                    Id = quest.Id,
                    Name = questName,
                    Description = questDescription,
                    Trader = traderName,
                    Location = string.IsNullOrEmpty(quest.Location) ? null : quest.Location,
                    QuestType = string.IsNullOrEmpty(quest.QuestType) ? null : quest.QuestType,
                    Conditions = conditions,
                };
            })
            .OrderBy(q => q.Name)
            .ToList();
    }

    private static (string? matchedType, string? nextBestType, int nextBestCount) ComputeTypeMatch(
        IReadOnlyList<string> preWeapons,
        CategorizationResult cat,
        ModConfig config)
    {
        if (preWeapons.Count < 2)
        {
            return (null, null, 0);
        }

        var selector = _typeSelector;
        var selection = selector.Select(preWeapons, cat.WeaponToType, cat.WeaponTypes, config.ParentTypes);
        var matched = selection.BestType ?? selection.BestCandidate;

        string? nextBestType = null;
        var nextBestCount = 0;

        if (selection.BestType is not null)
        {
            var nb = cat.WeaponTypes
                .Select(kvp => (type: kvp.Key, count: preWeapons.Count(id => kvp.Value.Contains(id))))
                .Where(x => x.count > 0 && x.type != selection.BestType)
                .OrderByDescending(x => x.count)
                .FirstOrDefault();
            nextBestType = nb.type;
            nextBestCount = nb.count;
        }

        return (matched, nextBestType, nextBestCount);
    }

    private static RuleConditionNode BuildConditionNode(string key, JsonElement value)
    {
        if (key is "and" or "or")
        {
            var children = value.EnumerateArray()
                .Select(el => BuildConditionNodeFromObject(el))
                .ToList();
            return new RuleConditionNode { Op = key, Children = children };
        }

        if (key == "not")
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return new RuleConditionNode { Op = "not", Children = [] };
            }

            var inner = BuildConditionNodeFromObject(value);
            return new RuleConditionNode { Op = "not", Children = [inner] };
        }

        // Leaf condition — value may be a plain string or a JSON object
        var valueStr = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
        return new RuleConditionNode { Key = key, Value = valueStr };
    }

    private static RuleConditionNode BuildConditionNodeFromObject(JsonElement obj)
    {
        var entries = obj.EnumerateObject().ToList();
        if (entries.Count == 1)
        {
            return BuildConditionNode(entries[0].Name, entries[0].Value);
        }

        var children = entries.Select(e => BuildConditionNode(e.Name, e.Value)).ToList();
        return new RuleConditionNode { Op = "and", Children = children };
    }

    private static RuleConditionNode BuildRuleConditions(Dictionary<string, JsonElement> conditions)
    {
        if (conditions.Count == 0)
        {
            return new RuleConditionNode { Op = "and", Children = [] };
        }

        if (conditions.Count == 1)
        {
            var (k, v) = conditions.First();
            return BuildConditionNode(k, v);
        }

        var children = conditions.Select(kvp => BuildConditionNode(kvp.Key, kvp.Value)).ToList();
        return new RuleConditionNode { Op = "and", Children = children };
    }

    private static string? LookupConditionDescription(ConditionNode c, IItemDatabase db)
    {
        // Description lives under the parent CounterCreator's bare-ID locale key.
        if (string.IsNullOrEmpty(c.ParentConditionId))
        {
            return null;
        }
        return db.GetConditionDescription(c.ParentConditionId);
    }

    private static WeaponRef ToWeaponRef(string id, IItemDatabase db)
    {
        var name = db.GetLocaleName(id)
            ?? db.Items.GetValueOrDefault(id)?.Name
            ?? id;
        return new WeaponRef { Id = id, Name = name };
    }

    private static List<List<WeaponRef>> ToWeaponRefGroups(
        List<List<string>> groups, IItemDatabase db)
    {
        return groups.Select(g => g.Select(id => ToWeaponRef(id, db)).ToList()).ToList();
    }
}
