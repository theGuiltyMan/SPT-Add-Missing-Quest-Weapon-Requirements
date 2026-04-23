using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace SptDbExporter;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public sealed class DbExporterLoader(
    DatabaseServer databaseServer,
    ModHelper modHelper,
    ISptLogger<DbExporterLoader> logger) : IOnLoad
{
    /// <summary>
    /// Allows System.Text.Json to serialize MongoId both as a regular JSON string value
    /// and as a dictionary key (property name). The built-in SPT converter does not
    /// implement ReadAsPropertyName / WriteAsPropertyName, which causes a runtime error
    /// when serializing Dictionary&lt;MongoId, T&gt;.
    /// </summary>
    private sealed class MongoIdConverter : JsonConverter<MongoId>
    {
        public override MongoId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new MongoId(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, MongoId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }

        public override MongoId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new MongoId(reader.GetString());
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, MongoId value, JsonSerializerOptions options)
        {
            writer.WritePropertyName(value.ToString());
        }
    }

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = false,
        Converters = { new MongoIdConverter() }
    };

    public async Task OnLoad()
    {
        var modDir = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var configPath = Path.Combine(modDir, "exporter-config.json");

        if (!File.Exists(configPath))
        {
            logger.Warning($"[SptDbExporter] Config not found at {configPath} — skipping export");
            return;
        }

        var config = JsonSerializer.Deserialize<ExporterConfig>(
            await File.ReadAllTextAsync(configPath)) ?? new ExporterConfig();

        if (!config.Enabled)
        {
            logger.Info("[SptDbExporter] Export disabled in config — skipping");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.OutputPath))
        {
            logger.Warning("[SptDbExporter] OutputPath is empty in config — skipping export");
            return;
        }

        try
        {
            Directory.CreateDirectory(config.OutputPath);

            var tables = databaseServer.GetTables();

            // ── Items ────────────────────────────────────────────────────────────
            var itemsPath = Path.Combine(config.OutputPath, "items.json");
            await using (var itemsStream = File.Create(itemsPath))
            {
                await JsonSerializer.SerializeAsync(itemsStream, tables.Templates.Items, _writeOptions);
            }

            logger.Info($"[SptDbExporter] Wrote {tables.Templates.Items.Count} items → {itemsPath}");

            // ── Quests ───────────────────────────────────────────────────────────
            var questsPath = Path.Combine(config.OutputPath, "quests.json");
            await using (var questsStream = File.Create(questsPath))
            {
                await JsonSerializer.SerializeAsync(questsStream, tables.Templates.Quests, _writeOptions);
            }

            logger.Info($"[SptDbExporter] Wrote {tables.Templates.Quests.Count} quests → {questsPath}");

            // ── Locale (English) ─────────────────────────────────────────────────
            var localePath = Path.Combine(config.OutputPath, "locale_en.json");
            var enLocale = tables.Locales.Global.TryGetValue("en", out var lazyEn)
                ? lazyEn.Value ?? []
                : [];

            await using (var localeStream = File.Create(localePath))
            {
                await JsonSerializer.SerializeAsync(localeStream, enLocale, _writeOptions);
            }

            logger.Info($"[SptDbExporter] Wrote {enLocale.Count} locale entries → {localePath}");
        }
        catch (Exception ex)
        {
            logger.Warning($"[SptDbExporter] Failed to write exported files: {ex.Message}");
        }
    }
}
