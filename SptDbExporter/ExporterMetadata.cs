using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace SptDbExporter;

public record ExporterMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.theGuiltyMan.sptDbExporter";
    public string Name { get; init; } = "SptDbExporter";
    public string Author { get; init; } = "theGuiltyMan";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.0.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}
