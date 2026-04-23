using System.IO;
using AddMissingQuestRequirements.Inspector;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Tests.Inspector;

public sealed class InspectorFixture : IDisposable
{
    public string RootDir { get; }
    public string DbPath { get; }
    public string ConfigPath { get; }
    public InspectorConfig Config { get; }
    public IModLogger Logger { get; } = NullModLogger.Instance;

    public InspectorFixture()
    {
        RootDir = Path.Combine(Path.GetTempPath(), $"mqw-fx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootDir);
        DbPath = Path.Combine(RootDir, "db");
        ConfigPath = Path.Combine(RootDir, "config");
        Directory.CreateDirectory(DbPath);
        Directory.CreateDirectory(Path.Combine(ConfigPath, "MissingQuestWeapons"));

        File.WriteAllText(Path.Combine(DbPath, "items.json"), "{}");
        File.WriteAllText(Path.Combine(DbPath, "quests.json"), "{}");
        File.WriteAllText(Path.Combine(DbPath, "locale_en.json"),
            "{\"templates\":{},\"quest\":{}}");

        File.WriteAllText(Path.Combine(ConfigPath, "config.jsonc"),
            "{ \"version\": 2 }");
        File.WriteAllText(Path.Combine(ConfigPath, "MissingQuestWeapons", "QuestOverrides.jsonc"),
            "{ \"version\": 2, \"overrides\": [] }");
        File.WriteAllText(Path.Combine(ConfigPath, "MissingQuestWeapons", "WeaponOverrides.jsonc"),
            "{ \"version\": 2, \"customTypeRules\": [] }");
        File.WriteAllText(Path.Combine(ConfigPath, "MissingQuestWeapons", "AttachmentOverrides.jsonc"),
            "{ \"version\": 1 }");

        Config = new InspectorConfig
        {
            ExportedDbPath = DbPath,
            MainConfigPath = ConfigPath,
            OutputReportPath = Path.Combine(RootDir, "report.html")
        };
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootDir, recursive: true);
        }
        catch { }
    }
}
