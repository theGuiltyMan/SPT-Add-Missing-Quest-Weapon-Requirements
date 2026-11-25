using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace AddMissingQuestRequirements.Spt;

/// <summary>
/// SPT mod metadata record.
/// Every property on <see cref="IModMetadata"/> must be implemented.
/// Nullable properties we do not use are set to <c>null</c>.
/// </summary>
public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.guiltyman.addmissingquestrequirements";
    public string Name { get; init; } = "AddMissingQuestRequirements";
    public string Author { get; init; } = "guiltyman";
    public List<string>? Contributors { get; init; } = null;
    public Version Version { get; init; } = new("3.0.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; } = null;
    public Dictionary<string, Range>? ModDependencies { get; init; } = null;
    public string? Url { get; init; } = null;
    public string License { get; init; } = "MIT";
}
