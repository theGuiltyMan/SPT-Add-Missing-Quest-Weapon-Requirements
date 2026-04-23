using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Override;

/// <summary>
/// Scans all mod directories for MissingQuestWeapons config files,
/// loads them, and merges them into a single <see cref="OverriddenSettings"/>.
/// </summary>
public sealed class OverrideReader(IModDirectoryProvider modDirProvider)
{
    private const string MqwFolder = "MissingQuestWeapons";
    private const string QuestOverridesFile = "QuestOverrides.jsonc";
    private const string WeaponOverridesFile = "WeaponOverrides.jsonc";
    private const string AttachmentOverridesFileName = "AttachmentOverrides.jsonc";

    // Current config version the mod expects (increment on breaking changes)
    private const int CurrentVersion = 2;
    private const int CurrentAttachmentVersion = 1;

    private static readonly Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>[]
        QuestMigrations = [Migrations.v0_to_v1, Migrations.v1_to_v2_Quest];

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

        foreach (var modDir in modDirProvider.GetModDirectories())
        {
            var mqwDir = Path.Combine(modDir, MqwFolder);
            if (!Directory.Exists(mqwDir))
            {
                continue;
            }

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

    private static void ApplyQuestOverrides(
        string mqwDir,
        ref List<string> excludedQuests,
        ref Dictionary<string, List<QuestOverrideEntry>> questOverrides)
    {
        var path = Path.Combine(mqwDir, QuestOverridesFile);
        var loaded = ConfigLoader.LoadFromFile<Models.QuestOverridesFile>(
            path, CurrentVersion, QuestMigrations);
        var file = loaded.Config;

        var fileBehaviour = file.OverrideBehaviour;

        excludedQuests = MergeHelper.MergeStringLists(
            excludedQuests, file.ExcludedQuests, fileBehaviour);

        questOverrides = MergeHelper.MergeQuestEntries(
            questOverrides, file.Overrides, fileBehaviour);
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
        var loaded = ConfigLoader.LoadFromFile<Models.WeaponOverridesFile>(
            path, CurrentVersion, WeaponsMigrations);
        var file = loaded.Config;

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
    }

    private static void ApplyAttachmentOverrides(
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