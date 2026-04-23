using System.Text.Json.Serialization;

namespace SptDbExporter;

public sealed class ExporterConfig
{
    /// <summary>Absolute path to the folder where exported JSON files will be written.</summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>Set to false to skip export entirely (useful to disable without uninstalling).</summary>
    [JsonIgnore]
    public bool Enabled { get; init; } = true;
}
