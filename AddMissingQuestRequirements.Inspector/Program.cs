using System.Text.Json;
using AddMissingQuestRequirements.Inspector;
using AddMissingQuestRequirements.Reporting;
using AddMissingQuestRequirements.Util;

if (args.Length > 0 && args[0] == "serve")
{
    return await AddMissingQuestRequirements.Inspector.Serve.ServeCommand.RunAsync(args[1..]);
}

var baseDir = FindGitRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
var configPath = args.Length > 0 ? args[0] : FindConfigPath(baseDir);

InspectorConfig rawConfig;
if (configPath is null)
{
    // No config file — use defaults. All paths resolve relative to the repo root.
    rawConfig = new InspectorConfig();
    Console.WriteLine("No inspector-config.json found; using defaults (SptDbExporter/export, config, inspector-report.html).");
}
else if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"Config not found: {configPath}");
    return 1;
}
else
{
    rawConfig = JsonSerializer.Deserialize<InspectorConfig>(
        await File.ReadAllTextAsync(configPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}

var config = rawConfig.Resolve(baseDir);

var logger = new CapturingModLogger();
var loaded = InspectorLoader.Load(config, logger);

Console.WriteLine($"Loaded {loaded.ItemDb.Items.Count} items, {loaded.QuestDb.Quests.Count} quests");

var result = PipelineRunner.Run(loaded, logger);

Console.WriteLine($"Categorized {result.Weapons.Count} weapons in {result.Types.Count} types");
Console.WriteLine($"Processed {result.Quests.Count} quests, " +
    $"{result.Quests.Count(q => !q.Noop)} with weapon changes");

Console.WriteLine($"Writing report to: {config.OutputReportPath}");
HtmlReportWriter.Write(result, config.OutputReportPath);

var jsonOutputPath = Path.ChangeExtension(config.OutputReportPath, ".json");
Console.WriteLine($"Writing JSON export to: {jsonOutputPath}");
await File.WriteAllTextAsync(jsonOutputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

if (config.ExportConfigPath is { } exportPath)
{
    Console.WriteLine($"Exporting merged config to: {exportPath}");
    ConfigExporter.Export(loaded, exportPath);
}

return 0;

static string? FindConfigPath(string baseDir)
{
    const string FileName = "inspector-config.json";

    var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), FileName);
    if (File.Exists(cwdPath))
    {
        return cwdPath;
    }

    var rootPath = Path.Combine(baseDir, FileName);
    if (File.Exists(rootPath))
    {
        return rootPath;
    }

    return null;
}

static string? FindGitRoot(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}
