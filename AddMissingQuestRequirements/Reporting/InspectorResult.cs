using AddMissingQuestRequirements.Pipeline.Shared;

namespace AddMissingQuestRequirements.Reporting;

public sealed class InspectorResult
{
    public required SettingsSnapshot Settings { get; init; }
    public required List<WeaponResult> Weapons { get; init; }
    public required Dictionary<string, List<WeaponResult>> Types { get; init; }
    public required List<AttachmentResult> Attachments { get; init; }
    public required Dictionary<string, List<AttachmentResult>> AttachmentTypes { get; init; }
    public required List<QuestResult> Quests { get; init; }
}

public sealed class SettingsSnapshot
{
    public required int ExcludedQuestCount { get; init; }
    public required List<string> ExcludedQuests { get; init; }
    public required Dictionary<string, string> ManualTypeOverrides { get; init; }
    public required List<RuleSnapshot> Rules { get; init; }
    public required List<RuleSnapshot> AttachmentRules { get; init; }
    public bool IncludeParentCategories { get; init; }
    public bool BestCandidateExpansion { get; init; }
}

public sealed class RuleSnapshot
{
    public required string Type { get; init; }
    public required RuleConditionNode Conditions { get; init; }
    public required List<string> AlsoAs { get; init; }
    public string? Priority { get; init; }
}

/// <summary>
/// Recursive node for rendering rule conditions in the HTML report.
/// Leaf node: Op and Children are null; Key and Value are set.
/// Meta node: Key and Value are null; Op is "and"|"or"|"not"; Children is set.
/// Discriminator: Op != null → meta node; else → leaf node.
/// </summary>
public sealed class RuleConditionNode
{
    public string? Key { get; init; }
    public string? Value { get; init; }
    public string? Op { get; init; }
    public List<RuleConditionNode>? Children { get; init; }
}

public sealed class WeaponResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required List<string> Types { get; init; }
    public string? Caliber { get; init; }
}

public sealed class AttachmentResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required List<string> Types { get; init; }
}

public sealed class QuestResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Trader { get; init; }
    public string? Location { get; init; }
    public string? QuestType { get; init; }
    public required List<ConditionResult> Conditions { get; init; }
    public bool Noop => Conditions.All(c => c.Noop);
}

public sealed class ConditionResult
{
    public required string Id { get; init; }
    public string? Description { get; init; }
    public required List<WeaponRef> Before { get; init; }
    public required List<WeaponRef> After { get; init; }
    public string? MatchedType { get; init; }
    public string? NextBestType { get; init; }
    public int NextBestTypeCount { get; init; }
    public string ExpansionMode { get; init; } = "Auto";
    public bool OverrideMatched { get; init; }
    public List<string> OverrideIncludedWeapons { get; init; } = [];
    public List<string> OverrideExcludedWeapons { get; init; } = [];
    public int? KillCount { get; init; }
    public List<string> EnemyTypes { get; init; } = [];
    public List<string> ConditionLocation { get; init; } = [];
    public string? CaliberFilter { get; init; }
    public string? Distance { get; init; }
    public required List<List<WeaponRef>> ModsInclusiveBefore { get; init; }
    public required List<List<WeaponRef>> ModsInclusiveAfter { get; init; }
    public required List<List<WeaponRef>> ModsExclusiveBefore { get; init; }
    public required List<List<WeaponRef>> ModsExclusiveAfter { get; init; }
    public string ModsExpansionMode { get; init; } = "Auto";
    public List<string> OverrideIncludedMods { get; init; } = [];
    public List<string> OverrideExcludedMods { get; init; } = [];

    public bool Expanded =>
        After.Count > Before.Count
        || GroupsGrew(ModsInclusiveBefore, ModsInclusiveAfter)
        || GroupsGrew(ModsExclusiveBefore, ModsExclusiveAfter);

    // Semantic no-op check. Delegates to ConditionDiff so runtime summary
    // counts and the inspector report agree on what "changed" means.
    public bool Noop =>
        !ConditionDiff.WeaponsChanged(Ids(Before), Ids(After))
        && !ConditionDiff.GroupsChanged(GroupIds(ModsInclusiveBefore), GroupIds(ModsInclusiveAfter))
        && !ConditionDiff.GroupsChanged(GroupIds(ModsExclusiveBefore), GroupIds(ModsExclusiveAfter));

    private static bool GroupsGrew(
        List<List<WeaponRef>> before, List<List<WeaponRef>> after)
    {
        var count = Math.Max(before.Count, after.Count);
        for (var i = 0; i < count; i++)
        {
            var b = i < before.Count ? before[i].Count : 0;
            var a = i < after.Count ? after[i].Count : 0;
            if (a > b)
            {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<string> Ids(List<WeaponRef> list)
    {
        return list.Select(w => w.Id).ToList();
    }

    private static IReadOnlyList<IReadOnlyList<string>> GroupIds(List<List<WeaponRef>> groups)
    {
        return groups.Select(g => (IReadOnlyList<string>)g.Select(w => w.Id).ToList()).ToList();
    }
}

public sealed class WeaponRef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class WeaponRefComparer : IEqualityComparer<WeaponRef>
{
    public static readonly WeaponRefComparer Instance = new();
    public bool Equals(WeaponRef? x, WeaponRef? y) => x?.Id == y?.Id;
    public int GetHashCode(WeaponRef obj) => obj.Id.GetHashCode();
}
