namespace AddMissingQuestRequirements.Pipeline.Override;

/// <summary>
/// Yields <paramref name="ownModPath"/> followed by every immediate subdirectory of each
/// <paramref name="containerDirs"/> entry. <see cref="OverrideReader"/> silently skips
/// directories without a <c>MissingQuestWeapons/</c> child, so no filtering happens here.
/// <para>
/// Order is part of the merge contract: our own folder first, so the shipped baseline is
/// what other mods' overrides apply to, then the rest sorted case-insensitively by folder
/// name so the result doesn't depend on filesystem enumeration order. Every folder is
/// yielded once, including the own folder when it also sits inside a container (the SPT
/// case: its parent is <c>user/mods/</c>) and folders reached through duplicate containers.
/// </para>
/// </summary>
public sealed class ModDirectoryProvider(string ownModPath, IEnumerable<string> containerDirs)
    : IModDirectoryProvider
{
    private readonly string _ownModPath = Normalize(ownModPath);

    /// <summary>The SPT layout: our own folder plus its siblings under the parent mods folder.</summary>
    public static ModDirectoryProvider ForInstalledMod(string ownModPath)
    {
        var modsDir = Path.GetDirectoryName(Normalize(ownModPath))
            ?? throw new InvalidOperationException($"Cannot resolve parent of mod folder '{ownModPath}'.");
        return new ModDirectoryProvider(ownModPath, [modsDir]);
    }

    public IEnumerable<string> GetModDirectories()
    {
        var others = containerDirs
            .SelectMany(Directory.GetDirectories)
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(dir => !string.Equals(dir, _ownModPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(Path.GetFileName, StringComparer.Ordinal);
        return [_ownModPath, ..others];
    }

    private static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
