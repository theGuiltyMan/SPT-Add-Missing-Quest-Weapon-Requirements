namespace AddMissingQuestRequirements.Config;

/// <summary>
/// Implemented by all config file models that carry a version field.
/// A null Version is treated as version 0 (pre-versioning TS format).
/// </summary>
public interface IVersionedConfig
{
    int? Version { get; }
}
