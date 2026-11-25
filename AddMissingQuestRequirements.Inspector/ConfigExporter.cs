using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Override;

namespace AddMissingQuestRequirements.Inspector;

/// <summary>
/// Exports the fully-merged in-memory config back to JSONC files on disk, stamped with
/// the versions the loaders expect. Useful for migrating old configs to the current format.
/// </summary>
public static class ConfigExporter
{
    private const string MqwFolder = "MissingQuestWeapons";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Export(LoadResult loaded, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var mqwDir = Path.Combine(outputDir, MqwFolder);
        Directory.CreateDirectory(mqwDir);

        WriteJson(Path.Combine(outputDir, ModConfigFile.FileName),    BuildConfig(loaded.Config));
        WriteJson(Path.Combine(mqwDir, "QuestOverrides.jsonc"),        BuildQuestOverrides(loaded.Settings));
        WriteJson(Path.Combine(mqwDir, "WeaponOverrides.jsonc"),       BuildWeaponOverrides(loaded.Settings));
        WriteJson(Path.Combine(mqwDir, "AttachmentOverrides.jsonc"),   BuildAttachmentOverrides(loaded.Settings));
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private static JsonObject BuildConfig(ModConfig src)
    {
        // Serialize every ModConfig field, then put the current version first.
        var fields = JsonSerializer.SerializeToNode(src, WriteOptions)!.AsObject();
        fields.Remove("version");
        var config = new JsonObject { ["version"] = ModConfigFile.CurrentVersion };
        foreach (var (key, value) in fields.ToList())
        {
            fields.Remove(key);
            config[key] = value;
        }
        return config;
    }

    private static QuestOverridesFile BuildQuestOverrides(OverriddenSettings s)
    {
        // QuestOverrides is keyed by quest ID; each entry already carries its Id field.
        var overrides = s.QuestOverrides
            .SelectMany(kvp => kvp.Value)
            .ToList();

        return new QuestOverridesFile
        {
            Version = OverrideReader.CurrentQuestVersion,
            // Every exported entry carries its effective behaviour, so on reload this
            // file default only governs how excludedQuests merges. MERGE into the
            // empty starting list reproduces it unchanged.
            OverrideBehaviour = OverrideBehaviour.MERGE,
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
            Version = OverrideReader.CurrentWeaponVersion,
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
            Version = OverrideReader.CurrentAttachmentVersion,
            ManualAttachmentTypeOverrides = new Dictionary<string, string>(s.ManualAttachmentTypeOverrides),
            CanBeUsedAs = canBeUsedAs,
            AliasNameStripWords = [..s.AttachmentAliasNameStripWords],
            CustomTypeRules = [..s.AttachmentTypeRules],
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void WriteJson<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, WriteOptions);
        File.WriteAllText(path, json);
    }
}
