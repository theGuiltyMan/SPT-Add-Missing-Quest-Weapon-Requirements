using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Models;

namespace AddMissingQuestRequirements.Inspector;

/// <summary>
/// Exports the fully-merged in-memory config back to v2 JSONC files on disk.
/// Useful for migrating old (v0/v1) configs to the current format.
/// </summary>
public static class ConfigExporter
{
    private const string MqwFolder = "MissingQuestWeapons";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Export(LoadResult loaded, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var mqwDir = Path.Combine(outputDir, MqwFolder);
        Directory.CreateDirectory(mqwDir);

        WriteJson(Path.Combine(outputDir, "config.jsonc"),             BuildConfig(loaded.Config));
        WriteJson(Path.Combine(mqwDir, "QuestOverrides.jsonc"),        BuildQuestOverrides(loaded.Settings));
        WriteJson(Path.Combine(mqwDir, "WeaponOverrides.jsonc"),       BuildWeaponOverrides(loaded.Settings));
        WriteJson(Path.Combine(mqwDir, "AttachmentOverrides.jsonc"),   BuildAttachmentOverrides(loaded.Settings));
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private static object BuildConfig(ModConfig src)
    {
        // Serialize via an anonymous object so we can force version = 2 without
        // needing ModConfig to be a record type.
        return new
        {
            version = 2,
            parentTypes = src.ParentTypes,
            excludedItems = src.ExcludedItems,
            excludedWeaponTypes = src.ExcludedWeaponTypes,
            includeParentCategories = src.IncludeParentCategories,
            bestCandidateExpansion = src.BestCandidateExpansion,
            validateOverrideIds = src.ValidateOverrideIds,
            debug = src.Debug,
        };
    }

    private static QuestOverridesFile BuildQuestOverrides(OverriddenSettings s)
    {
        // QuestOverrides is keyed by quest ID; each entry already carries its Id field.
        var overrides = s.QuestOverrides
            .SelectMany(kvp => kvp.Value)
            .ToList();

        return new QuestOverridesFile
        {
            Version = 2,
            ExcludedQuests = [..s.ExcludedQuests.OrderBy(x => x)],
            Overrides = overrides,
        };
    }

    private static WeaponOverridesFile BuildWeaponOverrides(OverriddenSettings s)
    {
        var canBeUsedAs = s.CanBeUsedAs.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(id => new Overridable<string>(id)).ToList());

        return new WeaponOverridesFile
        {
            Version = 2,
            ManualTypeOverrides = new Dictionary<string, string>(s.ManualTypeOverrides),
            CanBeUsedAs = canBeUsedAs,
            AliasNameStripWords = [..s.AliasNameStripWords],
            AliasNameExcludeWeapons = [..s.AliasNameExcludeWeapons],
            CustomTypeRules = [..s.TypeRules],
        };
    }

    private static AttachmentOverridesFile BuildAttachmentOverrides(OverriddenSettings s)
    {
        var canBeUsedAs = s.AttachmentCanBeUsedAs.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(id => new Overridable<string>(id)).ToList());

        return new AttachmentOverridesFile
        {
            Version = 2,
            ManualAttachmentTypeOverrides = new Dictionary<string, string>(s.ManualAttachmentTypeOverrides),
            CanBeUsedAs = canBeUsedAs,
            AliasNameStripWords = [..s.AttachmentAliasNameStripWords],
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void WriteJson<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, WriteOptions);
        File.WriteAllText(path, json);
    }
}
