using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Inspector;

public sealed class LoadResult
{
    public required ModConfig Config { get; init; }
    public required OverriddenSettings Settings { get; init; }
    public required InMemoryItemDatabase ItemDb { get; init; }
    public required InMemoryQuestDatabase QuestDb { get; init; }
}

/// <summary>
/// Loads the exported SPT database files and all config sources into a fully-populated
/// <see cref="LoadResult"/> ready for the inspector pipeline.
/// </summary>
public static class InspectorLoader
{
    private const string QuestOverridesFile = "QuestOverrides.jsonc";
    private const string WeaponOverridesFile = "WeaponOverrides.jsonc";
    private const string AttachmentOverridesFile = "AttachmentOverrides.jsonc";
    private const string ConfigFile = "config.jsonc";
    private const string MqwFolder = "MissingQuestWeapons";

    private const int CurrentQuestVersion = 2;
    private const int CurrentWeaponVersion = 2;
    private const int CurrentAttachmentVersion = 1;
    private const int CurrentConfigVersion = 3;

    private static readonly Func<JsonObject, JsonObject>[] QuestMigrations =
        [Migrations.v0_to_v1, Migrations.v1_to_v2_Quest];

    private static readonly Func<JsonObject, JsonObject>[] WeaponsMigrations =
        [Migrations.v0_to_v1_Weapons, Migrations.v1_to_v2_Weapons];

    private static readonly Func<JsonObject, JsonObject>[] AttachmentMigrations = [];

    private static readonly Func<JsonObject, JsonObject>[] ConfigMigrations =
        [Migrations.v0_to_v1, Migrations.v1_to_v2_Config, Migrations.v2_to_v3_Config];

    public static LoadResult Load(InspectorConfig config, IModLogger logger)
    {
        var itemsPath = Path.Combine(config.ExportedDbPath, "items.json");
        var questsPath = Path.Combine(config.ExportedDbPath, "quests.json");
        var localePath = Path.Combine(config.ExportedDbPath, "locale_en.json");

        var itemDb = SliceLoader.LoadItemDatabase(itemsPath, localePath);
        var questDb = SliceLoader.LoadQuestDatabase(questsPath);

        var modConfig = LoadModConfig(Path.Combine(config.MainConfigPath, ConfigFile), logger);

        // mainConfigPath is the mod root. config.jsonc lives at the root;
        // override files live in MissingQuestWeapons/ — same layout as the shipped mod.
        // Each entry in OtherModConfigPaths is a container folder holding multiple mod subdirectories.
        // Every immediate subdirectory of each container is treated as a mod root;
        // MissingQuestWeapons/ is appended to find the config folder.
        var allMqwDirs = new List<string> { Path.Combine(config.MainConfigPath, MqwFolder) };
        foreach (var containerPath in config.OtherModConfigPaths)
        {
            if (!Directory.Exists(containerPath))
            {
                logger.Warning($"[Inspector] OtherModConfigPaths entry not found, skipping: {containerPath}");
                continue;
            }

            foreach (var modDir in Directory.EnumerateDirectories(containerPath))
            {
                allMqwDirs.Add(Path.Combine(modDir, MqwFolder));
            }
        }

        var settings = MergeAllMqwDirs(allMqwDirs, modConfig, logger);

        return new LoadResult
        {
            Config = modConfig,
            Settings = settings,
            ItemDb = itemDb,
            QuestDb = questDb
        };
    }

    private static ModConfig LoadModConfig(string path, IModLogger logger)
    {
        if (!File.Exists(path))
        {
            logger.Warning($"[Inspector] config.jsonc not found at {path}, using defaults");
            return new ModConfig();
        }

        return ConfigLoader.LoadFromFile<ModConfig>(path, CurrentConfigVersion, ConfigMigrations).Config;
    }

    private static OverriddenSettings MergeAllMqwDirs(
        List<string> mqwDirs,
        ModConfig modConfig,
        IModLogger logger)
    {
        var excludedQuests = new List<string>();
        var questOverrides = new Dictionary<string, List<QuestOverrideEntry>>();
        var manualTypeOverrides = new Dictionary<string, string>();
        var canBeUsedAs = new Dictionary<string, HashSet<string>>();
        var aliasNameStripWords = new List<string>();
        var aliasNameExcludeWeapons = new List<string>();
        var typeRules = new List<TypeRule>();
        var manualAttachmentTypeOverrides = new Dictionary<string, string>();
        var attachmentCanBeUsedAs = new Dictionary<string, HashSet<string>>();
        var attachmentAliasNameStripWords = new List<string>();

        foreach (var mqwDir in mqwDirs)
        {
            if (!Directory.Exists(mqwDir))
            {
                logger.Warning($"[Inspector] MQW directory not found, skipping: {mqwDir}");
                continue;
            }

            ApplyQuestOverrides(mqwDir, ref excludedQuests, ref questOverrides);
            ApplyWeaponOverrides(mqwDir, ref manualTypeOverrides, ref canBeUsedAs,
                ref aliasNameStripWords, ref aliasNameExcludeWeapons, ref typeRules);
            ApplyAttachmentOverrides(mqwDir, ref manualAttachmentTypeOverrides,
                ref attachmentCanBeUsedAs, ref attachmentAliasNameStripWords);
        }

        return new OverriddenSettings
        {
            Config = modConfig,
            ExcludedQuests = [..excludedQuests],
            QuestOverrides = questOverrides,
            ManualTypeOverrides = manualTypeOverrides,
            CanBeUsedAs = canBeUsedAs,
            AliasNameStripWords = aliasNameStripWords,
            AliasNameExcludeWeapons = aliasNameExcludeWeapons,
            TypeRules = typeRules,
            ManualAttachmentTypeOverrides = manualAttachmentTypeOverrides,
            AttachmentCanBeUsedAs = attachmentCanBeUsedAs,
            AttachmentAliasNameStripWords = attachmentAliasNameStripWords
        };
    }

    private static void ApplyQuestOverrides(
        string mqwDir,
        ref List<string> excludedQuests,
        ref Dictionary<string, List<QuestOverrideEntry>> questOverrides)
    {
        var path = Path.Combine(mqwDir, QuestOverridesFile);
        var file = ConfigLoader.LoadFromFile<QuestOverridesFile>(
            path, CurrentQuestVersion, QuestMigrations).Config;
        var behaviour = file.OverrideBehaviour;

        excludedQuests = MergeHelper.MergeStringLists(excludedQuests, file.ExcludedQuests, behaviour);
        questOverrides = MergeHelper.MergeQuestEntries(questOverrides, file.Overrides, behaviour);
    }

    private static void ApplyWeaponOverrides(
        string mqwDir,
        ref Dictionary<string, string> manualTypeOverrides,
        ref Dictionary<string, HashSet<string>> canBeUsedAs,
        ref List<string> aliasNameStripWords,
        ref List<string> aliasNameExcludeWeapons,
        ref List<TypeRule> typeRules)
    {
        var path = Path.Combine(mqwDir, WeaponOverridesFile);
        var file = ConfigLoader.LoadFromFile<WeaponOverridesFile>(
            path, CurrentWeaponVersion, WeaponsMigrations).Config;
        var behaviour = file.OverrideBehaviour;

        manualTypeOverrides = MergeHelper.MergeStringDicts(
            manualTypeOverrides, file.ManualTypeOverrides, behaviour);
        canBeUsedAs = MergeHelper.MergeCanBeUsedAs(
            canBeUsedAs, file.CanBeUsedAs, behaviour);
        aliasNameStripWords = MergeHelper.MergeStringLists(
            aliasNameStripWords, file.AliasNameStripWords, behaviour);
        aliasNameExcludeWeapons = MergeHelper.MergeStringLists(
            aliasNameExcludeWeapons, file.AliasNameExcludeWeapons, behaviour);
        typeRules = MergeHelper.MergeTypeRules(typeRules, file.CustomTypeRules, behaviour);
    }

    private static void ApplyAttachmentOverrides(
        string mqwDir,
        ref Dictionary<string, string> manualAttachmentTypeOverrides,
        ref Dictionary<string, HashSet<string>> attachmentCanBeUsedAs,
        ref List<string> attachmentAliasNameStripWords)
    {
        var path = Path.Combine(mqwDir, AttachmentOverridesFile);
        var file = ConfigLoader.LoadFromFile<AttachmentOverridesFile>(
            path, CurrentAttachmentVersion, AttachmentMigrations).Config;
        var behaviour = file.OverrideBehaviour;

        manualAttachmentTypeOverrides = MergeHelper.MergeStringDicts(
            manualAttachmentTypeOverrides, file.ManualAttachmentTypeOverrides, behaviour);
        attachmentCanBeUsedAs = MergeHelper.MergeCanBeUsedAs(
            attachmentCanBeUsedAs, file.CanBeUsedAs, behaviour);
        attachmentAliasNameStripWords = MergeHelper.MergeStringLists(
            attachmentAliasNameStripWords, file.AliasNameStripWords, behaviour);
    }
}
