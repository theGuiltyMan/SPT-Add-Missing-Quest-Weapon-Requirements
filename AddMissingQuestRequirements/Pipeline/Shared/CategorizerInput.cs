using AddMissingQuestRequirements.Pipeline.Rules;

namespace AddMissingQuestRequirements.Pipeline.Shared;

/// <summary>
/// Domain-specific knobs passed to <see cref="CategorizerCore.Categorize"/>.
/// Weapon and attachment categorizers build their own instance; the core is
/// otherwise domain-agnostic.
/// <para>
/// <paramref name="ExpandManualType"/> is invoked on each type listed in a
/// <c>manualTypeOverrides</c> entry. Weapons use it to walk the
/// <c>parentTypes</c> chain so a manual <c>GrenadeLauncher</c> also pulls in
/// <c>explosive</c>. Attachments have no parent chain and leave the hook
/// unset (identity).
/// </para>
/// </summary>
public sealed record CategorizerInput(
    Dictionary<string, string> ManualOverrides,
    Dictionary<string, HashSet<string>> CanBeUsedAsSeeds,
    Func<RuleMatch, IEnumerable<string>> GetTypes,
    IReadOnlyList<string> AliasStripWords,
    IReadOnlyList<string> AliasExcludeIds,
    Func<string, IEnumerable<string>>? ExpandManualType = null);
