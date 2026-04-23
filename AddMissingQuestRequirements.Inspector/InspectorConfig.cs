namespace AddMissingQuestRequirements.Inspector;

public sealed class InspectorConfig
{
    /// <summary>Directory containing items.json, quests.json, locale_en.json written by SptDbExporter.
    /// Defaults to <c>SptDbExporter/export</c> relative to the repo root.</summary>
    public string ExportedDbPath { get; init; } = "SptDbExporter/export";

    /// <summary>
    /// Path to the mod root folder containing the main mod's config.
    /// Must contain <c>config.jsonc</c> at the root and a <c>MissingQuestWeapons/</c> subfolder
    /// with <c>QuestOverrides.jsonc</c>, <c>WeaponOverrides.jsonc</c>, <c>AttachmentOverrides.jsonc</c>.
    /// Defaults to <c>config</c> relative to the repo root.
    /// </summary>
    public string MainConfigPath { get; init; } = "config";

    /// <summary>
    /// Folders that each contain one or more mod subdirectories.
    /// Every immediate subdirectory is scanned for a MissingQuestWeapons/ subfolder.
    /// Example: "/mods/extras" → scans /mods/extras/ModA/, /mods/extras/ModB/, etc.
    /// Relative entries are resolved against the repo root.
    /// </summary>
    public List<string> OtherModConfigPaths { get; init; } = [];

    /// <summary>Full path for the generated HTML report file.
    /// Defaults to <c>inspector-report.html</c> in the repo root.</summary>
    public string OutputReportPath { get; init; } = "inspector-report.html";

    /// <summary>
    /// Optional. When set, the merged config is exported as v2 JSONC files after loading.
    /// Writes <c>config.jsonc</c> to the root and <c>QuestOverrides.jsonc</c>,
    /// <c>WeaponOverrides.jsonc</c>, <c>AttachmentOverrides.jsonc</c> inside a
    /// <c>MissingQuestWeapons/</c> subfolder. Useful for migrating old config to the new format.
    /// Relative paths are resolved against the repo root.
    /// </summary>
    public string? ExportConfigPath { get; init; }

    /// <summary>
    /// Returns a new <see cref="InspectorConfig"/> with every path resolved to an absolute path.
    /// Relative paths are joined to <paramref name="baseDir"/>; absolute paths pass through.
    /// Empty strings fall back to this type's property defaults before resolution.
    /// </summary>
    public InspectorConfig Resolve(string baseDir)
    {
        return new InspectorConfig
        {
            ExportedDbPath = ResolvePath(ExportedDbPath, baseDir, "SptDbExporter/export"),
            MainConfigPath = ResolvePath(MainConfigPath, baseDir, "config"),
            OutputReportPath = ResolvePath(OutputReportPath, baseDir, "inspector-report.html"),
            OtherModConfigPaths = [..OtherModConfigPaths.Select(p => ResolvePath(p, baseDir, p))],
            ExportConfigPath = ExportConfigPath is null ? null : ResolvePath(ExportConfigPath, baseDir, ExportConfigPath),
        };
    }

    private static string ResolvePath(string path, string baseDir, string fallback)
    {
        var effective = string.IsNullOrWhiteSpace(path) ? fallback : path;
        return Path.IsPathRooted(effective) ? effective : Path.GetFullPath(Path.Combine(baseDir, effective));
    }
}
