namespace AddMissingQuestRequirements.Pipeline.Shared;

/// <summary>
/// Output of <see cref="GroupExpander.BucketAndLog"/>. Carries the three buckets
/// plus the two boolean flags derived from <see cref="Models.UnknownWeaponHandling"/>
/// so subsequent steps do not re-compute them.
/// </summary>
public sealed class ExpansionBuckets
{
    /// <summary>IDs matched by the categorizer (present in ItemToType).</summary>
    public required IReadOnlyList<string> Categorized { get; init; }

    /// <summary>IDs in the database but not matched by the categorizer.</summary>
    public required IReadOnlyList<string> UncategorizedInDb { get; init; }

    /// <summary>IDs not found in the database at all.</summary>
    public required IReadOnlyList<string> NotInDb { get; init; }

    /// <summary>Whether <see cref="UncategorizedInDb"/> should be preserved in the final result.</summary>
    public required bool KeepUncategorizedInDb { get; init; }

    /// <summary>Whether <see cref="NotInDb"/> should be preserved in the final result.</summary>
    public required bool KeepNotInDb { get; init; }
}
