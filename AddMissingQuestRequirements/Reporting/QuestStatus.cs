namespace AddMissingQuestRequirements.Reporting;

/// <summary>
/// Mutually-exclusive operational status of a quest as it appears in the inspector report.
/// Precedence when classifying: Blacklisted > NoEligibleConditions > Noop > Expanded.
/// </summary>
public enum QuestStatus
{
    /// <summary>Quest is listed in <c>settings.ExcludedQuests</c>; the patcher skipped it.</summary>
    Blacklisted,

    /// <summary>Quest has no CounterCreator condition with weapon or mod-group data.</summary>
    NoEligibleConditions,

    /// <summary>Has eligible conditions, but the pipeline made no semantic changes.</summary>
    Noop,

    /// <summary>At least one eligible condition was expanded (weapons added or mod groups grew).</summary>
    Expanded,
}
