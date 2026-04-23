using System.Text.Json;

namespace AddMissingQuestRequirements.Pipeline.Database;

/// <summary>
/// Flat DTO representing one entry from the SPT item database (items.json).
/// Abstract category nodes have <see cref="NodeType"/> != "Item".
/// </summary>
public sealed class ItemNode
{
    /// <summary>The item's unique ID (_id).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The item's internal name (_name), e.g. "AKS74U", "AssaultRifle".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The parent item's ID (_parent). Null for the root node.</summary>
    public string? ParentId { get; init; }

    /// <summary>"Item" for concrete items; "Node" (or similar) for abstract category nodes.</summary>
    public string NodeType { get; init; } = string.Empty;

    /// <summary>The item's _props fields (BoltAction, ammoCaliber, etc.).</summary>
    public Dictionary<string, JsonElement> Props { get; init; } = [];
}
