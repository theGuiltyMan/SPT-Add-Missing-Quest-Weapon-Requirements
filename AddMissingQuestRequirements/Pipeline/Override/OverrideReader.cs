using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Override;

/// <summary>
/// Scans all mod directories for MissingQuestWeapons config files,
/// loads them, and merges them into a single <see cref="OverriddenSettings"/>.
/// </summary>
public sealed class OverrideReader(IModDirectoryProvider modDirProvider, IModLogger? logger = null)
{
    private void Trace(string message)
    {
        logger?.Debug($"[trace] OverrideReader: {message}");
    }

    private const string MqwFolder = "MissingQuestWeapons";
    private const string QuestOverridesFile = "QuestOverrides.jsonc";
    private const string WeaponOverridesFile = "WeaponOverrides.jsonc";
    private const string LegacyWeaponOverridesFile = "OverriddenWeapons.jsonc";
    private const string AttachmentOverridesFileName = "AttachmentOverrides.jsonc";

    // Current config version the mod expects (increment on breaking changes)
    private const int CurrentQuestVersion = 3;
    private const int CurrentWeaponVersion = 2;
    private const int CurrentAttachmentVersion = 1;

    private static readonly Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>[]
        QuestMigrations = [Migrations.v0_to_v1, Migrations.v1_to_v2_Quest, Migrations.v2_to_v3_Quest];

    private static readonly Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>[]
        WeaponsMigrations = [Migrations.v0_to_v1_Weapons, Migrations.v1_to_v2_Weapons];

    private static readonly Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>[]
        AttachmentMigrations = [];

    public OverriddenSettings Read()
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
        var attachmentTypeRules = new List<TypeRule>();

        var scannedDirs = 0;
        var matchedDirs = 0;
        foreach (var modDir in modDirProvider.GetModDirectories())
        {
            scannedDirs++;
            var mqwDir = Path.Combine(modDir, MqwFolder);
            if (!Directory.Exists(mqwDir))
            {
                continue;
            }

            matchedDirs++;
            Trace($"scanning '{modDir}' (has MissingQuestWeapons folder)");

            ApplyQuestOverrides(mqwDir, ref excludedQuests, ref questOverrides);
            ApplyWeaponOverrides(mqwDir, ref manualTypeOverrides, ref canBeUsedAs, ref aliasNameStripWords,
                ref aliasNameExcludeWeapons, ref typeRules);
            ApplyAttachmentOverrides(
                mqwDir,
                ref manualAttachmentTypeOverrides,
                ref attachmentCanBeUsedAs,
                ref attachmentAliasNameStripWords,
                ref attachmentTypeRules);
        }
        Trace($"scanned {scannedDirs} dirs, {matchedDirs} had MissingQuestWeapons. final counts: " +
            $"questOverrides={questOverrides.Values.Sum(l => l.Count)} (across {questOverrides.Count} ids), " +
            $"manualTypeOverrides={manualTypeOverrides.Count}, typeRules={typeRules.Count}, " +
            $"manualAttachmentTypeOverrides={manualAttachmentTypeOverrides.Count}");

        return new OverriddenSettings
        {
            ExcludedQuests = [..excludedQuests],
            QuestOverrides = questOverrides,
            ManualTypeOverrides = manualTypeOverrides,
            CanBeUsedAs = canBeUsedAs,
            AliasNameStripWords = aliasNameStripWords,
            AliasNameExcludeWeapons = aliasNameExcludeWeapons,
            TypeRules = typeRules,
            ManualAttachmentTypeOverrides = manualAttachmentTypeOverrides,
            AttachmentCanBeUsedAs = attachmentCanBeUsedAs,
            AttachmentAliasNameStripWords = attachmentAliasNameStripWords,
            AttachmentTypeRules = attachmentTypeRules,
        };
    }

    private void ApplyQuestOverrides(
        string mqwDir,
        ref List<string> excludedQuests,
        ref Dictionary<string, List<QuestOverrideEntry>> questOverrides)
    {
        var path = Path.Combine(mqwDir, QuestOverridesFile);
        var exists = File.Exists(path);
        Trace($"QuestOverrides path='{path}' exists={exists}");
        var loaded = ConfigLoader.LoadFromFile<Models.QuestOverridesFile>(
            path, CurrentQuestVersion, QuestMigrations);
        var file = loaded.Config;
        Trace($"QuestOverrides loaded behaviour={file.OverrideBehaviour}, " +
            $"overrides={file.Overrides.Count}, excludedQuests={file.ExcludedQuests.Count}");
        foreach (var w in loaded.Warnings)
        {
            Trace($"QuestOverrides warning: {w}");
        }

        // v2→v3 semantic-flip notice. The migration plants a sentinel when any
        // entry has a non-empty excludedMods; surface it once per file so authors
        // notice the new per-field semantics.
        if (loaded.MigratedJson[Migrations.V2ToV3ExcludedModsSemanticChangedKey] is JsonValue sentinelValue
            && sentinelValue.TryGetValue<bool>(out var sentinelBool)
            && sentinelBool)
        {
            logger?.Warning(
                $"[migration] '{path}': excludedMods semantics changed in config v3 — " +
                $"these IDs now append to weaponModsExclusive instead of dropping groups. " +
                $"Review your overrides.");
        }

        var fileBehaviour = file.OverrideBehaviour;

        var beforeEntries = questOverrides.Values.Sum(l => l.Count);
        excludedQuests = MergeHelper.MergeStringLists(
            excludedQuests, file.ExcludedQuests, fileBehaviour);

        questOverrides = MergeHelper.MergeQuestEntries(
            questOverrides, file.Overrides, fileBehaviour);
        var afterEntries = questOverrides.Values.Sum(l => l.Count);
        Trace($"QuestOverrides post-merge entries: {beforeEntries} -> {afterEntries}");

        if (loaded.WasMigrated)
        {
            MigrationWriter.Persist(
                sourcePath: path,
                canonicalPath: path,
                originalVersion: loaded.OriginalVersion,
                migratedJson: loaded.MigratedJson,
                logger: logger);
        }
    }

    private void ApplyWeaponOverrides(
        string mqwDir,
        ref Dictionary<string, string> manualTypeOverrides,
        ref Dictionary<string, HashSet<string>> canBeUsedAs,
        ref List<string> aliasNameStripWords,
        ref List<string> aliasNameExcludeWeapons,
        ref List<TypeRule> typeRules)
    {
        var canonicalPath = Path.Combine(mqwDir, WeaponOverridesFile);
        var path = canonicalPath;
        var newExists = File.Exists(path);
        var pickedLegacy = false;
        if (!newExists)
        {
            var legacy = Path.Combine(mqwDir, LegacyWeaponOverridesFile);
            if (File.Exists(legacy))
            {
                path = legacy;
                pickedLegacy = true;
            }
        }
        Trace($"WeaponOverrides newExists={newExists} pickedLegacy={pickedLegacy} path='{path}'");

        var loaded = ConfigLoader.LoadFromFile<Models.WeaponOverridesFile>(
            path, CurrentWeaponVersion, WeaponsMigrations);
        var file = loaded.Config;
        Trace($"WeaponOverrides loaded behaviour={file.OverrideBehaviour}, " +
            $"manualTypeOverrides={file.ManualTypeOverrides.Count}, " +
            $"customTypeRules={file.CustomTypeRules.Count}, " +
            $"canBeUsedAs={file.CanBeUsedAs.Count}");
        foreach (var w in loaded.Warnings)
        {
            Trace($"WeaponOverrides warning: {w}");
        }

        var fileBehaviour = file.OverrideBehaviour;

        manualTypeOverrides = MergeHelper.MergeStringDicts(
            manualTypeOverrides, file.ManualTypeOverrides, fileBehaviour);

        canBeUsedAs = MergeHelper.MergeCanBeUsedAs(
            canBeUsedAs, file.CanBeUsedAs, fileBehaviour);

        aliasNameStripWords = MergeHelper.MergeStringLists(
            aliasNameStripWords, file.AliasNameStripWords, fileBehaviour);

        aliasNameExcludeWeapons = MergeHelper.MergeStringLists(
            aliasNameExcludeWeapons, file.AliasNameExcludeWeapons, fileBehaviour);

        typeRules = MergeHelper.MergeTypeRules(
            typeRules, file.CustomTypeRules, fileBehaviour);

        if (loaded.WasMigrated)
        {
            MigrationWriter.Persist(
                sourcePath: path,
                canonicalPath: canonicalPath,
                originalVersion: loaded.OriginalVersion,
                migratedJson: loaded.MigratedJson,
                logger: logger);
        }
    }

    private void ApplyAttachmentOverrides(
        string mqwDir,
        ref Dictionary<string, string> manualAttachmentTypeOverrides,
        ref Dictionary<string, HashSet<string>> attachmentCanBeUsedAs,
        ref List<string> attachmentAliasNameStripWords,
        ref List<TypeRule> attachmentTypeRules)
    {
        var path = Path.Combine(mqwDir, AttachmentOverridesFileName);
        var loaded = ConfigLoader.LoadFromFile<Models.AttachmentOverridesFile>(
            path, CurrentAttachmentVersion, AttachmentMigrations);
        var file = loaded.Config;

        var fileBehaviour = file.OverrideBehaviour;

        manualAttachmentTypeOverrides = MergeHelper.MergeStringDicts(
            manualAttachmentTypeOverrides, file.ManualAttachmentTypeOverrides, fileBehaviour);

        attachmentCanBeUsedAs = MergeHelper.MergeCanBeUsedAs(
            attachmentCanBeUsedAs, file.CanBeUsedAs, fileBehaviour);

        attachmentAliasNameStripWords = MergeHelper.MergeStringLists(
            attachmentAliasNameStripWords, file.AliasNameStripWords, fileBehaviour);

        attachmentTypeRules = MergeHelper.MergeTypeRules(
            attachmentTypeRules, file.CustomTypeRules, fileBehaviour);
    }
}