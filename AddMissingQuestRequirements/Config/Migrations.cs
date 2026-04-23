using System.Text.Json.Nodes;

namespace AddMissingQuestRequirements.Config;

/// <summary>
/// Contains all versioned migration functions for config files.
/// Each function upgrades a JsonObject from version N to version N+1.
/// Pass the relevant functions to <see cref="ConfigMigrator.Migrate"/> in order.
/// </summary>
public static class Migrations
{
    /// <summary>Identity migration — used as a placeholder when a file type has no v0→v1 changes.</summary>
    public static JsonObject NoOp(JsonObject obj)
    {
        return obj;
    }

    /// <summary>
    /// Migrates ModConfig from version 0 (TypeScript era) to version 1 (C# rewrite).
    /// - Renames <c>categorizeWithLessRestrive</c> (typo) → <c>categorizeWithLessRestrictive</c>
    /// - Removes the unused <c>delay</c> field
    /// </summary>
    public static JsonObject v0_to_v1(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("categorizeWithLessRestrive", out var val))
        {
            obj.Remove("categorizeWithLessRestrive");
            if (!obj.ContainsKey("categorizeWithLessRestrictive"))
            {
                obj["categorizeWithLessRestrictive"] = val?.DeepClone();
            }
        }

        obj.Remove("delay");

        return obj;
    }

    /// <summary>
    /// Migrates WeaponOverrides from version 0 (TypeScript era) to version 1.
    /// Converts <c>CustomCategories</c> entries into <c>customTypeRules</c> (TypeRule JSON)
    /// and moves <c>ids</c> entries into <c>manualTypeOverrides</c>.
    /// Removes the <c>CustomCategories</c> key from the output.
    /// </summary>
    public static JsonObject v0_to_v1_Weapons(JsonObject obj)
    {
        if (!obj.TryGetPropertyValue("CustomCategories", out var categoriesNode)
            || categoriesNode is not JsonArray categories)
        {
            return obj;
        }

        obj.Remove("CustomCategories");

        var generatedRules = new JsonArray();

        // Ensure manualTypeOverrides dict exists
        if (!obj.ContainsKey("manualTypeOverrides"))
        {
            obj["manualTypeOverrides"] = new JsonObject();
        }

        var manualOverrides = obj["manualTypeOverrides"]!.AsObject();

        foreach (var categoryNode in categories)
        {
            if (categoryNode is not JsonObject category)
            {
                continue;
            }

            var name = category["name"]?.GetValue<string>() ?? string.Empty;

            // ids → manualTypeOverrides (never become TypeRules)
            if (category["ids"] is JsonArray ids)
            {
                foreach (var idNode in ids)
                {
                    var id = idNode?.GetValue<string>();
                    if (id is not null)
                    {
                        manualOverrides[id] = name;
                    }
                }
            }

            var subConditions = BuildCategorySubConditions(category);

            // 0 sub-conditions: only ids were present — don't emit an empty-conditions catch-all rule
            if (subConditions.Count == 0)
            {
                continue;
            }

            var conditionsObj = AssembleConditionsObject(subConditions);

            var rule = new JsonObject
            {
                ["comment"]    = $"Migrated from CustomCategories '{name}'",
                ["conditions"] = conditionsObj,
                ["type"]       = name
            };

            generatedRules.Add(rule);
        }

        if (generatedRules.Count > 0)
        {
            obj["customTypeRules"] = generatedRules;
        }

        return obj;
    }

    // ── Migration helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the list of sub-condition JsonObjects for a single CustomCategory entry.
    /// Each element becomes one key in the flat conditions dict, or one element in "and:[...]".
    /// </summary>
    private static List<JsonObject> BuildCategorySubConditions(JsonObject category)
    {
        var subConditions = new List<JsonObject>();

        var alsoCheckDescription = category["alsoCheckDescription"]?.GetValue<bool>() ?? false;

        // Positive keyword / description condition
        if (category["whiteListedKeywords"] is JsonArray keywords && keywords.Count > 0)
        {
            var keywordStrings = keywords
                .Select(k => k?.GetValue<string>() ?? string.Empty)
                .ToList();

            subConditions.Add(BuildKeywordCondition(keywordStrings, alsoCheckDescription));
        }

        // Positive caliber condition
        if (category["allowedCalibres"] is JsonArray calibers && calibers.Count > 0)
        {
            var caliberStrings = calibers.Select(c => c?.GetValue<string>() ?? string.Empty).ToList();
            subConditions.Add(BuildSingleOrOrCondition("caliber", caliberStrings));
        }

        // Positive weaponType condition
        if (category["weaponTypes"] is JsonArray weaponTypes && weaponTypes.Count > 0)
        {
            var typeStrings = weaponTypes.Select(t => t?.GetValue<string>() ?? string.Empty).ToList();
            subConditions.Add(BuildSingleOrOrCondition("hasAncestor", typeStrings));
        }

        // Negative keyword condition
        if (category["blackListedKeywords"] is JsonArray blacklistKeywords && blacklistKeywords.Count > 0)
        {
            var blacklistStrings = blacklistKeywords
                .Select(k => k?.GetValue<string>() ?? string.Empty)
                .ToList();
            subConditions.Add(BuildBlacklistKeywordCondition(blacklistStrings, alsoCheckDescription));
        }

        // Negative caliber condition
        if (category["blackListedCalibres"] is JsonArray blacklistCalibres && blacklistCalibres.Count > 0)
        {
            var caliberStrings = blacklistCalibres
                .Select(c => c?.GetValue<string>() ?? string.Empty)
                .ToList();
            var inner = BuildSingleOrOrCondition("caliber", caliberStrings);
            subConditions.Add(new JsonObject { ["not"] = inner });
        }

        // Negative weaponType condition
        if (category["blackListedWeaponTypes"] is JsonArray blacklistTypes && blacklistTypes.Count > 0)
        {
            var typeStrings = blacklistTypes.Select(t => t?.GetValue<string>() ?? string.Empty).ToList();
            var inner = BuildSingleOrOrCondition("hasAncestor", typeStrings);
            subConditions.Add(new JsonObject { ["not"] = inner });
        }

        return subConditions;
    }

    /// <summary>
    /// Builds the keyword/description positive condition object.
    /// Single keyword, no description: { "nameMatches": "p" }
    /// Multiple keywords, no description: { "nameMatches": "p1|p2" }
    /// alsoCheckDescription=true: { "or": [{ "nameMatches": "p1|p2" }, { "descriptionMatches": "p1|p2" }] }
    /// </summary>
    private static JsonObject BuildKeywordCondition(List<string> keywords, bool alsoCheckDescription)
    {
        var combinedPattern = string.Join("|", keywords);

        if (!alsoCheckDescription)
        {
            return new JsonObject { ["nameMatches"] = combinedPattern };
        }

        var orArray = new JsonArray
        {
            new JsonObject { ["nameMatches"] = combinedPattern },
            new JsonObject { ["descriptionMatches"] = combinedPattern }
        };

        return new JsonObject { ["or"] = orArray };
    }

    /// <summary>
    /// Builds the negative keyword condition object.
    /// Single keyword: { "not": { "nameMatches": "(kw)" } }
    /// Multiple keywords: { "not": { "nameMatches": "(kw1)|(kw2)" } }
    /// alsoCheckDescription=true: { "not": { "or": [{ "nameMatches": "(kw1)|(kw2)" }, { "descriptionMatches": "(kw1)|(kw2)" }] } }
    /// </summary>
    private static JsonObject BuildBlacklistKeywordCondition(List<string> keywords, bool alsoCheckDescription)
    {
        // Combine all blacklisted keywords into a single OR-regex pattern
        var combinedPattern = string.Join("|", keywords.Select(k => $"({k})"));

        if (!alsoCheckDescription)
        {
            return new JsonObject
            {
                ["not"] = new JsonObject { ["nameMatches"] = combinedPattern }
            };
        }

        var orArray = new JsonArray
        {
            new JsonObject { ["nameMatches"] = combinedPattern },
            new JsonObject { ["descriptionMatches"] = combinedPattern }
        };

        return new JsonObject
        {
            ["not"] = new JsonObject { ["or"] = orArray }
        };
    }

    /// <summary>
    /// Builds { "conditionKey": "single value" } or { "or": [{ "conditionKey": "v1" }, ...] }
    /// depending on whether there is one or multiple values.
    /// </summary>
    private static JsonObject BuildSingleOrOrCondition(string conditionKey, List<string> values)
    {
        if (values.Count == 1)
        {
            return new JsonObject { [conditionKey] = values[0] };
        }

        var orArray = new JsonArray();
        foreach (var value in values)
        {
            orArray.Add(new JsonObject { [conditionKey] = value });
        }

        return new JsonObject { ["or"] = orArray };
    }

    /// <summary>
    /// Assembles a list of sub-condition objects into the final conditions JsonObject.
    /// <para>
    /// Rule: use a flat dict when all sub-condition objects have distinct top-level keys.
    /// Use "and: [sub1, sub2, ...]" only when two or more sub-conditions share the same key
    /// (most commonly two "or" groups — one for keywords, one for calibers or weaponTypes).
    /// </para>
    /// </summary>
    private static JsonObject AssembleConditionsObject(List<JsonObject> subConditions)
    {
        // Count how many times each key appears across all sub-conditions
        var keyCounts = new Dictionary<string, int>();
        foreach (var sub in subConditions)
        {
            foreach (var prop in sub)
            {
                keyCounts[prop.Key] = keyCounts.GetValueOrDefault(prop.Key) + 1;
            }
        }

        var hasCollision = keyCounts.Values.Any(c => c > 1);

        if (!hasCollision)
        {
            // Flat: merge all sub-condition key-values into one object
            var flat = new JsonObject();
            foreach (var sub in subConditions)
            {
                foreach (var prop in sub)
                {
                    flat[prop.Key] = prop.Value?.DeepClone();
                }
            }

            return flat;
        }

        // Key collision: wrap all sub-conditions in "and: [...]"
        var andArray = new JsonArray();
        foreach (var sub in subConditions)
        {
            andArray.Add(sub.DeepClone());
        }

        return new JsonObject { ["and"] = andArray };
    }

    /// <summary>
    /// Migrates QuestOverrides from version 1 to version 2 (C# naming conventions).
    /// - <c>BlackListedQuests</c> → <c>excludedQuests</c>
    /// - <c>OverrideBehaviour</c> → <c>overrideBehaviour</c>
    /// - <c>Overrides</c> → <c>overrides</c>
    /// - Per-entry: <c>skip: true</c> → <c>expansionMode: "noExpansion"</c>
    /// - Per-entry: <c>onlyUseWhiteListedWeapons: true</c> → <c>expansionMode: "whitelistOnly"</c>
    /// - Per-entry: <c>whiteListedWeapons</c> → <c>includedWeapons</c>
    /// - Per-entry: <c>blackListedWeapons</c> → <c>excludedWeapons</c>
    /// </summary>
    public static JsonObject v1_to_v2_Quest(JsonObject obj)
    {
        RenameKey(obj, "BlackListedQuests", "excludedQuests");
        RenameKey(obj, "OverrideBehaviour", "overrideBehaviour");

        // "Overrides" → "overrides" (also handle if it was already lowercased)
        RenameKey(obj, "Overrides", "overrides");

        if (obj["overrides"] is JsonArray overrides)
        {
            foreach (var item in overrides)
            {
                if (item is not JsonObject entry)
                {
                    continue;
                }

                // skip: true → expansionMode: "NoExpansion"
                if (entry["skip"]?.GetValue<bool>() == true)
                {
                    entry.Remove("skip");
                    entry["expansionMode"] = "NoExpansion";
                }
                else
                {
                    entry.Remove("skip");
                }

                // onlyUseWhiteListedWeapons: true → expansionMode: "WhitelistOnly"
                if (entry["onlyUseWhiteListedWeapons"]?.GetValue<bool>() == true)
                {
                    entry.Remove("onlyUseWhiteListedWeapons");
                    if (!entry.ContainsKey("expansionMode"))
                    {
                        entry["expansionMode"] = "WhitelistOnly";
                    }
                }
                else
                {
                    entry.Remove("onlyUseWhiteListedWeapons");
                }

                RenameKey(entry, "whiteListedWeapons", "includedWeapons");
                RenameKey(entry, "blackListedWeapons", "excludedWeapons");
            }
        }

        return obj;
    }

    /// <summary>
    /// Migrates WeaponOverrides (now <see cref="Models.WeaponOverridesFile"/>) from version 1 to 2.
    /// - <c>Override</c> → merged into <c>manualTypeOverrides</c> (existing keys win)
    /// - <c>CanBeUsedAsShortNameWhitelist</c> → <c>aliasNameStripWords</c>
    /// - <c>CanBeUsedAsShortNameBlacklist</c> → <c>aliasNameExcludeWeapons</c>
    /// - <c>OverrideBehaviour</c> → <c>overrideBehaviour</c>
    /// - <c>CanBeUsedAs</c> → <c>canBeUsedAs</c>
    /// </summary>
    public static JsonObject v1_to_v2_Weapons(JsonObject obj)
    {
        // Override → manualTypeOverrides: must merge, not replace.
        // v0_to_v1_Weapons may have already created manualTypeOverrides from CustomCategories.ids,
        // so RenameKey (which skips if the target exists) would silently drop all Override entries.
        if (obj.TryGetPropertyValue("Override", out var overrideNode)
            && overrideNode is JsonObject overrideDict)
        {
            obj.Remove("Override");

            if (!obj.ContainsKey("manualTypeOverrides"))
            {
                obj["manualTypeOverrides"] = new JsonObject();
            }

            var target = obj["manualTypeOverrides"]!.AsObject();
            foreach (var kvp in overrideDict)
            {
                if (!target.ContainsKey(kvp.Key))
                {
                    target[kvp.Key] = kvp.Value?.DeepClone();
                }
            }
        }

        RenameKey(obj, "CanBeUsedAsShortNameWhitelist", "aliasNameStripWords");
        RenameKey(obj, "CanBeUsedAsShortNameBlacklist", "aliasNameExcludeWeapons");
        RenameKey(obj, "OverrideBehaviour", "overrideBehaviour");
        RenameKey(obj, "CanBeUsedAs", "canBeUsedAs");

        return obj;
    }

    /// <summary>
    /// Migrates ModConfig from version 1 to version 2 (C# naming conventions).
    /// - <c>categorizeWithLessRestrictive</c> → <c>includeParentCategories</c>
    /// - <c>kindOf</c> → <c>parentTypes</c>
    /// - <c>BlackListedItems</c> → <c>excludedItems</c>
    /// - <c>BlackListedWeaponsTypes</c> → <c>excludedWeaponTypes</c>
    /// </summary>
    public static JsonObject v1_to_v2_Config(JsonObject obj)
    {
        RenameKey(obj, "categorizeWithLessRestrictive", "includeParentCategories");
        RenameKey(obj, "kindOf", "parentTypes");
        RenameKey(obj, "BlackListedItems", "excludedItems");
        RenameKey(obj, "BlackListedWeaponsTypes", "excludedWeaponTypes");

        return obj;
    }

    /// <summary>
    /// Migrates ModConfig from version 2 to version 3.
    /// Replaces the boolean <c>keepUnknownWeapons</c> with the three-state
    /// <c>unknownWeaponHandling</c> enum.
    /// - <c>true</c>  → <c>1</c> (KeepInDb)  preserves old effective behavior
    /// - <c>false</c> → <c>0</c> (Strip)     preserves old effective behavior
    /// - absent       → <c>2</c> (KeepAll)   new conservative default
    /// A user-authored <c>unknownWeaponHandling</c> key is preserved verbatim.
    /// </summary>
    public static JsonObject v2_to_v3_Config(JsonObject obj)
    {
        var alreadySet = obj.ContainsKey("unknownWeaponHandling");

        int target = 2; // KeepAll default
        if (obj.TryGetPropertyValue("keepUnknownWeapons", out var oldVal)
            && oldVal is JsonValue v
            && v.TryGetValue<bool>(out var b))
        {
            target = b ? 1 : 0; // KeepInDb or Strip
        }

        obj.Remove("keepUnknownWeapons");

        if (!alreadySet)
        {
            obj["unknownWeaponHandling"] = target;
        }

        return obj;
    }

    /// <summary>
    /// Migrates ModConfig from version 3 to version 4.
    /// Removes the <c>logType</c> field: the loader now always tees to both
    /// console and file, so the knob is redundant. Present values are dropped
    /// silently; the user does not need to edit their config.
    /// </summary>
    public static JsonObject v3_to_v4_Config(JsonObject obj)
    {
        obj.Remove("logType");
        return obj;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void RenameKey(JsonObject obj, string oldKey, string newKey)
    {
        if (!obj.TryGetPropertyValue(oldKey, out var value))
        {
            return;
        }

        obj.Remove(oldKey);

        if (!obj.ContainsKey(newKey))
        {
            obj[newKey] = value?.DeepClone();
        }
    }
}
