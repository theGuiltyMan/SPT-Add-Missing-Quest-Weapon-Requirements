using AddMissingQuestRequirements.Models;

namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Pure utility functions for merging config collections using OverrideBehaviour semantics.
/// </summary>
public static class MergeHelper
{
    // ── Plain string lists ───────────────────────────────────────────────────

    /// <summary>
    /// Merges two plain string lists according to <paramref name="behaviour"/>.
    /// IGNORE: keep existing, skip all incoming.
    /// MERGE: union (deduplicated).
    /// REPLACE: use incoming only.
    /// DELETE: remove incoming items from existing.
    /// </summary>
    public static List<string> MergeStringLists(
        List<string> existing, List<string> incoming, OverrideBehaviour behaviour) =>
        behaviour switch
        {
            OverrideBehaviour.IGNORE  => existing.Count > 0 ? [..existing] : [..incoming],
            OverrideBehaviour.MERGE   => [..existing.Union(incoming)],
            OverrideBehaviour.REPLACE => [..incoming],
            OverrideBehaviour.DELETE  => existing.Where(x => !incoming.Contains(x)).ToList(),
            _                         => [..existing]
        };

    // ── Overridable<string> lists ────────────────────────────────────────────

    /// <summary>
    /// Applies an incoming <see cref="Overridable{T}"/> list on top of an existing plain list.
    /// Items with <c>behaviour: DELETE</c> are removed from existing.
    /// Remaining incoming items are added (subject to file-level <paramref name="behaviour"/>).
    /// </summary>
    public static List<string> ApplyOverridableList(
        List<string> existing, List<Overridable<string>> incoming, OverrideBehaviour behaviour)
    {
        if (behaviour == OverrideBehaviour.REPLACE)
            return incoming
                .Where(i => i.Behaviour != OverrideBehaviour.DELETE)
                .Select(i => i.Value)
                .ToList();

        var result = new List<string>(existing);
        var seen   = new HashSet<string>(existing);

        foreach (var item in incoming)
        {
            if (item.Behaviour == OverrideBehaviour.DELETE)
            {
                result.Remove(item.Value);
                seen.Remove(item.Value);
            }
            else if (seen.Add(item.Value))
            {
                result.Add(item.Value);
            }
        }

        return result;
    }

    // ── String dictionaries (e.g. Override: weaponId → types) ───────────────

    /// <summary>
    /// Merges two <c>string → string</c> dictionaries.
    /// IGNORE: keep existing value for known keys; add new keys.
    /// MERGE: for known keys whose values are comma-separated type lists, union the types; add new keys.
    /// REPLACE: overwrite existing keys with incoming values.
    /// DELETE: remove keys present in incoming from existing.
    /// </summary>
    public static Dictionary<string, string> MergeStringDicts(
        Dictionary<string, string> existing, Dictionary<string, string> incoming,
        OverrideBehaviour behaviour)
    {
        var result = new Dictionary<string, string>(existing);

        foreach (var (key, value) in incoming)
        {
            if (behaviour == OverrideBehaviour.DELETE)
            {
                result.Remove(key);
                continue;
            }

            if (!result.ContainsKey(key))
            {
                result[key] = value; // new key — always add
                continue;
            }

            if (behaviour == OverrideBehaviour.IGNORE)
            {
                continue; // keep existing
            }

            if (behaviour == OverrideBehaviour.MERGE)
            {
                // Union comma-separated type lists (e.g. "AssaultRifle" + "CustomCarbine")
                var existingTypes = result[key]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var incomingTypes = value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                result[key] = string.Join(',', existingTypes.Union(incomingTypes));
                continue;
            }

            result[key] = value; // REPLACE
        }

        return result;
    }

    // ── CanBeUsedAs: weaponId → alias set ────────────────────────────────────

    /// <summary>
    /// Merges an incoming <c>CanBeUsedAs</c> dict (with Overridable per-value DELETE support)
    /// into the existing resolved alias map.
    /// </summary>
    public static Dictionary<string, HashSet<string>> MergeCanBeUsedAs(
        Dictionary<string, HashSet<string>> existing,
        Dictionary<string, List<Overridable<string>>> incoming,
        OverrideBehaviour behaviour)
    {
        var result = existing.ToDictionary(
            kv => kv.Key,
            kv => new HashSet<string>(kv.Value));

        foreach (var (weaponId, aliases) in incoming)
        {
            if (!result.TryGetValue(weaponId, out var existingAliases))
            {
                // New key — always add
                existingAliases = [];
                result[weaponId] = existingAliases;
            }
            else if (behaviour == OverrideBehaviour.IGNORE)
            {
                continue; // key exists, skip
            }
            else if (behaviour == OverrideBehaviour.REPLACE)
            {
                existingAliases.Clear();
            }

            foreach (var alias in aliases)
            {
                if (alias.Behaviour == OverrideBehaviour.DELETE)
                    existingAliases.Remove(alias.Value);
                else
                    existingAliases.Add(alias.Value);
            }
        }

        return result;
    }

    // ── Quest override entries (keyed by quest ID) ───────────────────────────

    /// <summary>
    /// Merges incoming <see cref="QuestOverrideEntry"/> list into the existing map.
    /// The effective behaviour per entry is: entry.Behaviour ?? fileDefaultBehaviour.
    /// </summary>
    public static Dictionary<string, List<QuestOverrideEntry>> MergeQuestEntries(
        Dictionary<string, List<QuestOverrideEntry>> existing,
        List<QuestOverrideEntry> incoming,
        OverrideBehaviour fileDefaultBehaviour)
    {
        var result = existing.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(CloneEntry).ToList());

        foreach (var entry in incoming)
        {
            var effective = entry.Behaviour ?? fileDefaultBehaviour;

            if (effective == OverrideBehaviour.DELETE)
            {
                result.Remove(entry.Id);
                continue;
            }

            if (!result.TryGetValue(entry.Id, out var existingEntries))
            {
                // New quest ID — always add
                result[entry.Id] = [CloneEntry(entry)];
                continue;
            }

            switch (effective)
            {
                case OverrideBehaviour.IGNORE:
                    break; // keep existing, skip incoming

                case OverrideBehaviour.REPLACE:
                    result[entry.Id] = [CloneEntry(entry)];
                    break;

                case OverrideBehaviour.MERGE:
                    // Merge lists into the first existing entry for this quest
                    var target = existingEntries[0];
                    result[entry.Id][0] = MergeEntries(target, entry);
                    break;
            }
        }

        return result;
    }

    // ── TypeRule lists ───────────────────────────────────────────────────────

    /// <summary>
    /// Merges an incoming <see cref="TypeRule"/> list into an existing one using two-level semantics:
    /// <para>
    /// File-level <paramref name="fileBehaviour"/>:
    /// IGNORE: return existing unchanged if non-empty, else process incoming normally.
    /// MERGE: process each incoming rule (default).
    /// REPLACE: discard existing entirely, then process each incoming rule.
    /// DELETE: return empty list immediately.
    /// </para>
    /// <para>
    /// Per-rule <see cref="TypeRule.Behaviour"/> (applied for each incoming rule, keyed on Type):
    /// null: always append.
    /// IGNORE: skip if any rule with the same Type already exists in accumulated list.
    /// REPLACE: remove all accumulated rules with the same Type, then append.
    /// DELETE: remove all accumulated rules with the same Type; do not append.
    /// MERGE: undefined — treat as default (append); merging condition dicts has no defined semantics.
    /// </para>
    /// </summary>
    public static List<TypeRule> MergeTypeRules(
        List<TypeRule> existing, List<TypeRule> incoming, OverrideBehaviour fileBehaviour)
    {
        if (fileBehaviour == OverrideBehaviour.DELETE)
        {
            return [];
        }

        if (fileBehaviour == OverrideBehaviour.IGNORE && existing.Count > 0)
        {
            return [..existing];
        }

        var result = fileBehaviour == OverrideBehaviour.REPLACE
            ? new List<TypeRule>()
            : new List<TypeRule>(existing);

        foreach (var rule in incoming)
        {
            switch (rule.Behaviour)
            {
                case OverrideBehaviour.DELETE:
                    result.RemoveAll(r => r.Type == rule.Type);
                    break;

                case OverrideBehaviour.REPLACE:
                    result.RemoveAll(r => r.Type == rule.Type);
                    result.Add(rule);
                    break;

                case OverrideBehaviour.IGNORE:
                    if (result.All(r => r.Type != rule.Type))
                    {
                        result.Add(rule);
                    }
                    break;

                default:
                    // null or MERGE (MERGE has no defined semantics at rule-level — treat as append)
                    result.Add(rule);
                    break;
            }
        }

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static QuestOverrideEntry CloneEntry(QuestOverrideEntry e) => new()
    {
        Id                = e.Id,
        Behaviour         = e.Behaviour,
        ExpansionMode     = e.ExpansionMode,
        Conditions        = [..e.Conditions],
        IncludedWeapons   = [..e.IncludedWeapons],
        ExcludedWeapons   = [..e.ExcludedWeapons],
        ModsExpansionMode = e.ModsExpansionMode,
        IncludedMods      = [..e.IncludedMods],
        ExcludedMods      = [..e.ExcludedMods],
    };

    private static QuestOverrideEntry MergeEntries(QuestOverrideEntry a, QuestOverrideEntry b) => new()
    {
        Id                = a.Id,
        Behaviour         = a.Behaviour,
        // When merging, prefer the more restrictive mode (NoExpansion > WhitelistOnly > Auto)
        ExpansionMode     = (ExpansionMode)Math.Max((int)a.ExpansionMode, (int)b.ExpansionMode),
        Conditions        = [..a.Conditions.Union(b.Conditions)],
        IncludedWeapons   = [..a.IncludedWeapons.Union(b.IncludedWeapons)],
        ExcludedWeapons   = [..a.ExcludedWeapons.Union(b.ExcludedWeapons)],
        ModsExpansionMode = (ExpansionMode)Math.Max((int)a.ModsExpansionMode, (int)b.ModsExpansionMode),
        IncludedMods      = [..a.IncludedMods.Union(b.IncludedMods)],
        ExcludedMods      = [..a.ExcludedMods.Union(b.ExcludedMods)],
    };
}
