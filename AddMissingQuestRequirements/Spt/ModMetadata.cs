using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace AddMissingQuestRequirements.Spt;

/// <summary>
/// SPT mod metadata record.
/// Every abstract property on <see cref="AbstractModMetadata"/> must be overridden.
/// Nullable properties we do not use are set to <c>null</c>.
/// </summary>
public sealed record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.guiltyman.addmissingquestrequirements";
    public override string Name { get; init; } = "AddMissingQuestRequirements";
    public override string Author { get; init; } = "guiltyman";
    public override List<string>? Contributors { get; init; } = null;
    public override Version Version { get; init; } = new("1.0.0");
    public override Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; } = null;
    public override Dictionary<string, Range>? ModDependencies { get; init; } = null;
    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
