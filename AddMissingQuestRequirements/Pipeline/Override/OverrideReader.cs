using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Override;

/// <summary>
/// Scans all mod directories for MissingQuestWeapons config files,
/// loads them, and merges them into a single <see cref="OverriddenSettings"/>.
/// <para>
/// <paramref name="persistMigrations"/> controls whether quest and weapon files loaded
/// at an older version are rewritten on disk (attachment files never are). The SPT loader persists; the Inspector is
/// read-only and migrates in memory only.
/// </para>
/// </summary>
public sealed class OverrideReader(
    IModDirectoryProvider modDirProvider,
    IModLogger? logger = null,
    bool persistMigrations = true)
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

    // Versions the loader expects (increment on breaking changes). Public so the
    // Inspector's ConfigExporter stamps exported files with the same numbers.
    public const int CurrentQuestVersion = 3;
    public const int CurrentWeaponVersion = 2;

    // v1 -> v2 changed no shape; files written by the 2.x exporter already say 2.
    public const int CurrentAttachmentVersion = 2;

    private static readonly Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>[]
        QuestMigrations = [Migrations.v0_to_v1, Migrations.v1_to_v2_Quest, Migrations.v2_to_v3_Quest];

    private static readonly Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>[]
        WeaponsMigrations = [Migrations.v0_to_v1_Weapons, Migrations.v1_to_v2_Weapons];

    private static readonly Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>[]
        AttachmentMigrations = [Migrations.NoOp, Migrations.NoOp];

    /// <summary>
    /// Loads one override file. A file that cannot be read, parsed or migrated is logged as a
    /// warning, recorded in <paramref name="fileErrors"/> and skipped (returns null) so one
    /// bad file never takes down the rest of the merge.
    /// </summary>
    private LoadResult<T>? TryLoad<T>(
        string path,
        int currentVersion,
        Func<JsonObject, JsonObject>[] migrations,
        List<OverrideFileError> fileErrors)
        where T : IVersionedConfig, new()
    {
        try
        {
            return ConfigLoader.LoadFromFile<T>(path, currentVersion, migrations);
        }
        catch (Exception ex)
        {
            logger?.Warning($"Skipping override file '{path}': {ex.Message}");
            fileErrors.Add(new OverrideFileError(path, ex.Message));
            return null;
        }
    }

    /// <param name="config">Bound to <see cref="OverriddenSettings.Config"/>; defaults when null.</param>
    public OverriddenSettings Read(ModConfig? config = null)
    {
        var fileErrors = new List<OverrideFileError>();
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

            ApplyQuestOverrides(mqwDir, fileErrors, ref excludedQuests, ref questOverrides);
            ApplyWeaponOverrides(mqwDir, fileErrors, ref manualTypeOverrides, ref canBeUsedAs, ref aliasNameStripWords,
                ref aliasNameExcludeWeapons, ref typeRules);
            ApplyAttachmentOverrides(
                mqwDir,
                fileErrors,
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
            Config = config ?? new ModConfig(),
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
            FileErrors = fileErrors,
        };
    }

    private void ApplyQuestOverrides(
        string mqwDir,
        List<OverrideFileError> fileErrors,
        ref List<string> excludedQuests,
        ref Dictionary<string, List<QuestOverrideEntry>> questOverrides)
    {
        var path = Path.Combine(mqwDir, QuestOverridesFile);
        var exists = File.Exists(path);
        Trace($"QuestOverrides path='{path}' exists={exists}");
        var loaded = TryLoad<Models.QuestOverridesFile>(
            path, CurrentQuestVersion, QuestMigrations, fileErrors);
        if (loaded is null)
        {
            return;
        }

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

        if (persistMigrations && loaded.WasMigrated)
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
        List<OverrideFileError> fileErrors,
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

        var loaded = TryLoad<Models.WeaponOverridesFile>(
            path, CurrentWeaponVersion, WeaponsMigrations, fileErrors);
        if (loaded is null)
        {
            return;
        }

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

        if (persistMigrations && loaded.WasMigrated)
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
        List<OverrideFileError> fileErrors,
        ref Dictionary<string, string> manualAttachmentTypeOverrides,
        ref Dictionary<string, HashSet<string>> attachmentCanBeUsedAs,
        ref List<string> attachmentAliasNameStripWords,
        ref List<TypeRule> attachmentTypeRules)
    {
        var path = Path.Combine(mqwDir, AttachmentOverridesFileName);
        var loaded = TryLoad<Models.AttachmentOverridesFile>(
            path, CurrentAttachmentVersion, AttachmentMigrations, fileErrors);
        if (loaded is null)
        {
            return;
        }

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