using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Attachment;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Override;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Shared;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Reporting;
using AddMissingQuestRequirements.Util;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;

namespace AddMissingQuestRequirements.Spt;

/// <summary>
/// SPT <see cref="IOnLoad"/> entry point. Runs once at server startup after the
/// database is loaded but before SPT processes it, executing the three-phase
/// pipeline and writing the expanded condition arrays back into SPT's in-memory
/// quest tables. Failures are logged and swallowed — the server must finish
/// startup regardless of this mod's success.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 9999)]
public sealed class AddMissingQuestRequirementsLoader : IOnLoad
{
    private readonly TemplateTable _templateTable;
    private readonly LocaleService _localeService;
    private readonly ModHelper _modHelper;
    private readonly ISptLogger<AddMissingQuestRequirementsLoader> _sptLogger;
    private readonly ISptLogger<SptItemDatabase> _itemDbLogger;
    private readonly ISptLogger<SptQuestDatabase> _questDbLogger;

    public AddMissingQuestRequirementsLoader(
        TemplateTable templateTable,
        LocaleService localeService,
        ModHelper modHelper,
        ISptLogger<AddMissingQuestRequirementsLoader> sptLogger,
        ISptLogger<SptItemDatabase> itemDbLogger,
        ISptLogger<SptQuestDatabase> questDbLogger)
    {
        _templateTable = templateTable;
        _localeService = localeService;
        _modHelper = modHelper;
        _sptLogger = sptLogger;
        _itemDbLogger = itemDbLogger;
        _questDbLogger = questDbLogger;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var ownAssembly = Assembly.GetExecutingAssembly();
            var ownModPath = _modHelper.GetAbsolutePathToModFolder(ownAssembly);

            var config = LoadConfig(ownModPath);

            var consoleSink = new SptModLogger<AddMissingQuestRequirementsLoader>(_sptLogger);
            var fileSink = new DebugFilteringModLogger(
                new FileModLogger(Path.Combine(ownModPath, "AddMissingQuestRequirements.log")),
                config.Debug);
            var logger = new TeeModLogger(consoleSink, fileSink);

            if (!config.Enabled)
            {
                logger.Success("AddMissingQuestRequirements disabled — set `enabled: true` in config.jsonc to re-enable.");
                return Task.CompletedTask;
            }

            var itemDb = new SptItemDatabase(_templateTable, _localeService, _itemDbLogger);
            var questDb = new SptQuestDatabase(_templateTable, _questDbLogger);
            var modDirs = ModDirectoryProvider.ForInstalledMod(ownModPath);
            var nameResolver = new ItemDbNameResolver(itemDb);

            var settings = new OverrideReader(modDirs, logger).Read(config);

            // Fold any miscased override item IDs (e.g. uppercase Massivesoft IDs)
            // to the DB's canonical casing — fixes both lookups and write-back.
            OverrideIdCanonicalizer.Normalize(settings, itemDb);

            var questOverrideCount = settings.QuestOverrides.Values.Sum(list => list.Count);
            logger.Info(
                $"Overrides loaded: {questOverrideCount} quest entries, "
                + $"{settings.ExcludedQuests.Count} excluded quests, "
                + $"{settings.TypeRules.Count} user weapon rules, "
                + $"{settings.AttachmentTypeRules.Count} user attachment rules, "
                + $"{settings.ManualTypeOverrides.Count} manual weapon overrides, "
                + $"{settings.ManualAttachmentTypeOverrides.Count} manual attachment overrides.");

            if (config.ValidateOverrideIds)
            {
                ValidateOverrideIds(settings, itemDb, nameResolver, logger);
            }

            // User rules, then the per-ancestor defaults built from
            // WeaponLikeAncestors. Each ancestor gets {directChildOf:A} when a
            // subtree exists (Weapon → AssaultRifle…) or literal type=A when items
            // are direct children (Knife, ThrowWeap). The engine unions every
            // matching rule. WeaponCategorizer re-appends settings.TypeRules
            // internally, so user rules run twice; the union makes that harmless.
            var defaultWeaponRules = DefaultWeaponRuleFactory.Build(itemDb, config.WeaponLikeAncestors);
            var weaponCat = new WeaponCategorizer(settings.TypeRules.Concat(defaultWeaponRules))
                .Categorize(itemDb, settings, config);
            logger.Info(
                $"Weapons categorized: {weaponCat.WeaponToType.Count} items across "
                + $"{weaponCat.WeaponTypes.Count} types.");

            var attachmentCat = new AttachmentCategorizer(DefaultAttachmentRules.Rules)
                .Categorize(itemDb, settings);
            logger.Info(
                $"Attachments categorized: {attachmentCat.AttachmentToType.Count} items across "
                + $"{attachmentCat.AttachmentTypes.Count} types.");

            var patcher = new QuestPatcher(
                [
                    new WeaponArrayExpander(new TypeSelector(), nameResolver),
                    new WeaponModsExpander(attachmentCat, nameResolver),
                ],
                nameResolver);

            // Capture pre-patch state for (a) the always-on change count in the
            // startup summary, and (b) the debug report's per-condition diffs
            // when config.Debug is on. Only the three fields QuestPatcher can
            // mutate are snapshotted.
            var prePatchWeapons = new Dictionary<ConditionNode, List<string>>(
                ReferenceEqualityComparer.Instance);
            var prePatchMods = new Dictionary<ConditionNode, (List<List<string>> Incl, List<List<string>> Excl)>(
                ReferenceEqualityComparer.Instance);

            foreach (var quest in questDb.Quests.Values)
            {
                foreach (var node in quest.Conditions)
                {
                    prePatchWeapons[node] = [..node.Weapon];
                    prePatchMods[node] = (
                        [..node.WeaponModsInclusive.Select(g => new List<string>(g))],
                        [..node.WeaponModsExclusive.Select(g => new List<string>(g))]
                    );
                }
            }

            patcher.Patch(questDb, settings, weaponCat, logger);

            QuestWriteback.WriteBack(
                questDb.Sources.Select(kvp => (kvp.Key, kvp.Value)));

            sw.Stop();

            // Count quests + conditions with a real semantic change vs the
            // pre-patch snapshot. Reorderings, duplicate folds, and empty-
            // bundle cleanup do NOT count (see ConditionDiff).
            var weaponConditionQuests = questDb.Quests.Count(q => q.Value.Conditions.Any(c =>
                c.Weapon.Count > 0
                || c.WeaponModsInclusive.Count > 0
                || c.WeaponModsExclusive.Count > 0));
            var totalConditions = questDb.Sources.Count;

            var changedConditions = 0;
            var changedQuests = 0;
            foreach (var quest in questDb.Quests.Values)
            {
                var questChanged = false;
                foreach (var node in quest.Conditions)
                {
                    var before = prePatchWeapons[node];
                    var (beforeIncl, beforeExcl) = prePatchMods[node];

                    var condChanged =
                        ConditionDiff.WeaponsChanged(before, node.Weapon)
                        || ConditionDiff.GroupsChanged(beforeIncl, node.WeaponModsInclusive)
                        || ConditionDiff.GroupsChanged(beforeExcl, node.WeaponModsExclusive);

                    if (condChanged)
                    {
                        changedConditions++;
                        questChanged = true;
                    }
                }
                if (questChanged)
                {
                    changedQuests++;
                }
            }

            var summary =
                $"AddMissingQuestRequirements ready in {sw.ElapsedMilliseconds}ms — "
                + $"{changedQuests} of {weaponConditionQuests} weapon-quests changed "
                + $"({changedConditions} of {totalConditions} conditions). "
                + $"Weapons: {weaponCat.WeaponToType.Count} items across "
                + $"{weaponCat.WeaponTypes.Count} types. "
                + $"Attachments: {attachmentCat.AttachmentToType.Count} items across "
                + $"{attachmentCat.AttachmentTypes.Count} types.";

            logger.Success(summary);

            if (config.Debug)
            {
                WriteDebugReport(
                    ownModPath,
                    settings,
                    config,
                    itemDb,
                    questDb,
                    prePatchWeapons,
                    prePatchMods,
                    weaponCat,
                    attachmentCat,
                    logger);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _sptLogger.Error(
                $"AddMissingQuestRequirements aborted after {sw.ElapsedMilliseconds}ms: "
                + $"{ex.GetType().Name} — {ex.Message}");
        }

        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private ModConfig LoadConfig(string ownModPath)
    {
        var configPath = Path.Combine(ownModPath, "config", ModConfigFile.FileName);
        if (!File.Exists(configPath))
        {
            _sptLogger.Warning(
                $"AddMissingQuestRequirements: {configPath} missing — using defaults.");
            return new ModConfig();
        }

        try
        {
            return ModConfigFile.Load(configPath);
        }
        catch (Exception ex)
        {
            _sptLogger.Error(
                $"AddMissingQuestRequirements: config.jsonc is malformed "
                + $"({ex.Message}) — using defaults.");
            return new ModConfig();
        }
    }

    private static void ValidateOverrideIds(
        OverriddenSettings settings,
        IItemDatabase itemDb,
        INameResolver nameResolver,
        IModLogger logger)
    {
        foreach (var (questId, entries) in settings.QuestOverrides)
        {
            // DELETE entries only clear a condition; their weapon lists are never applied.
            foreach (var entry in entries.Where(e => e.Behaviour != OverrideBehaviour.DELETE))
            {
                foreach (var id in entry.IncludedWeapons.Concat(entry.ExcludedWeapons))
                {
                    if (!itemDb.Items.ContainsKey(id))
                    {
                        logger.Warning(
                            $"QuestOverride '[{nameResolver.GetQuestName(questId)} ({questId})]' "
                            + $"→ entry '{entry.Id}' references unknown weapon id "
                            + $"'{nameResolver.FormatItem(id)}'.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Build the rich <see cref="InspectorResult"/> via <see cref="ReportBuilder"/>,
    /// then serialise it to
    /// <c>AddMissingQuestRequirements-debug-report.json</c> and render the same
    /// data via <see cref="HtmlReportWriter"/> to
    /// <c>AddMissingQuestRequirements-debug-report.html</c> in the mod folder.
    /// Any I/O or serialisation failure is caught and logged as a warning —
    /// the report is diagnostic and must never block the pipeline.
    /// </summary>
    private static void WriteDebugReport(
        string ownModPath,
        OverriddenSettings settings,
        ModConfig config,
        IItemDatabase itemDb,
        IQuestDatabase questDb,
        IReadOnlyDictionary<ConditionNode, List<string>> prePatchWeapons,
        IReadOnlyDictionary<ConditionNode, (List<List<string>> Incl, List<List<string>> Excl)> prePatchMods,
        CategorizationResult weaponCat,
        AttachmentCategorizationResult attachmentCat,
        IModLogger logger)
    {
        try
        {
            var result = ReportBuilder.Build(
                settings,
                config,
                itemDb,
                questDb,
                prePatchWeapons,
                prePatchMods,
                weaponCat,
                attachmentCat);

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            });

            var jsonPath = Path.Combine(ownModPath, "AddMissingQuestRequirements-debug-report.json");
            File.WriteAllText(jsonPath, json);

            var htmlPath = Path.Combine(ownModPath, "AddMissingQuestRequirements-debug-report.html");
            HtmlReportWriter.Write(result, htmlPath);
        }
        catch (Exception ex)
        {
            logger.Warning($"Debug report write failed: {ex.Message}");
        }
    }
}
