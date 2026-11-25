using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Override;
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
/// <see cref="LoadResult"/> ready for the inspector pipeline. Config and overrides go
/// through the same <see cref="ModConfigFile"/> and <see cref="OverrideReader"/> the
/// SPT loader uses, with migrations kept in memory so the Inspector never writes to disk.
/// </summary>
public static class InspectorLoader
{
    private const string MqwFolder = "MissingQuestWeapons";

    public static LoadResult Load(InspectorConfig config, IModLogger logger)
    {
        var itemsPath = Path.Combine(config.ExportedDbPath, "items.json");
        var questsPath = Path.Combine(config.ExportedDbPath, "quests.json");
        var localePath = Path.Combine(config.ExportedDbPath, "locale_en.json");

        var itemDb = SliceLoader.LoadItemDatabase(itemsPath, localePath);
        var questDb = SliceLoader.LoadQuestDatabase(questsPath);

        var modConfig = LoadModConfig(Path.Combine(config.MainConfigPath, ModConfigFile.FileName), logger);

        // MainConfigPath is the mod root: config.jsonc at the root, overrides in
        // MissingQuestWeapons/. Each OtherModConfigPaths entry is a container whose
        // immediate subdirectories are mod roots, like SPT's user/mods/.
        if (!Directory.Exists(Path.Combine(config.MainConfigPath, MqwFolder)))
        {
            logger.Warning($"[Inspector] No {MqwFolder}/ folder under MainConfigPath: {config.MainConfigPath}");
        }

        List<string> containers = [];
        foreach (var containerPath in config.OtherModConfigPaths)
        {
            if (Directory.Exists(containerPath))
            {
                containers.Add(containerPath);
            }
            else
            {
                logger.Warning($"[Inspector] OtherModConfigPaths entry not found, skipping: {containerPath}");
            }
        }

        var modDirs = new ModDirectoryProvider(config.MainConfigPath, containers);
        var settings = new OverrideReader(modDirs, logger, persistMigrations: false).Read(modConfig);

        // The reader skips unreadable files so a stranger's broken file cannot stop the run, but
        // a broken file in the config being edited must fail the load: serve mode relies on that
        // to show the error and keep the last good state instead of reloading with the file missing.
        ThrowIfMainConfigBroken(config.MainConfigPath, settings);

        return new LoadResult
        {
            Config = modConfig,
            Settings = settings,
            ItemDb = itemDb,
            QuestDb = questDb
        };
    }

    private static void ThrowIfMainConfigBroken(string mainConfigPath, OverriddenSettings settings)
    {
        var mainRoot = Path.GetFullPath(mainConfigPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var broken = settings.FileErrors
            .Where(e => Path.GetFullPath(e.Path).StartsWith(mainRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (broken.Count == 0)
        {
            return;
        }

        var details = string.Join("; ", broken.Select(e => $"{e.Path}: {e.Message}"));
        throw new InvalidDataException($"Invalid override file(s) in MainConfigPath: {details}");
    }

    private static ModConfig LoadModConfig(string path, IModLogger logger)
    {
        if (!File.Exists(path))
        {
            logger.Warning($"[Inspector] config.jsonc not found at {path}, using defaults");
            return new ModConfig();
        }

        return ModConfigFile.Load(path);
    }
}
