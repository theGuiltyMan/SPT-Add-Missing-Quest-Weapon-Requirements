# Quest status enum — design

## Problem

Inspector report's quest list mixes four operationally distinct buckets behind one boolean (`QuestResult.Noop`):

1. Quests the user explicitly blacklisted via `config.MissingQuestWeapons/QuestOverrides.jsonc:excludedQuests`.
2. Quests that have no CounterCreator condition with weapon/mods data at all (skill checks, find-item, level requirements, etc.) — never had anything to expand.
3. Quests with eligible conditions that the pipeline left unchanged.
4. Quests where at least one condition was expanded.

Today (1) and (2) both render as NOOP, so users post-processing their config manually cannot tell at a glance which quests are intentionally skipped versus which are unactionable versus which were processed and produced nothing. Source: `Reporting/InspectorResult.cs:72` (`QuestResult.Noop`), `Reporting/ReportBuilder.cs:169-176` (per-condition filter, no quest-level skip), `Reporting/Assets/report.js:370-372` (single NOOP/expanded badge).

## Goal

Replace the implicit boolean with a mutually-exclusive enum so each quest carries one clear status, and let the HTML report filter by any combination of statuses.

## Non-goals

- Status changes on the SPT runtime summary path (the runtime logger reports per-condition; quest-level status is inspector-only for now).
- Surfacing orthogonal markers (`OverrideMatched`, `NextBestType`) as new statuses — those stay as side tags on the condition row.
- Backwards compatibility with consumers of the existing `noop` JSON field. The inspector HTML and JSON are co-versioned.

## Design

### Enum

`Reporting/QuestStatus.cs`:

```csharp
public enum QuestStatus
{
    Blacklisted,
    NoEligibleConditions,
    Noop,
    Expanded,
}
```

Serialized as a string via `JsonStringEnumConverter` on the existing inspector serializer options. JSON ships `"status": "Expanded"`.

### Classification rules

Computed once in `ReportBuilder.BuildQuestList` per quest. Precedence (top wins, mutually exclusive):

| Precedence | Status | Predicate |
|-----------:|--------|-----------|
| 1 | `Blacklisted` | `settings.ExcludedQuests.Contains(quest.Id)` |
| 2 | `NoEligibleConditions` | not blacklisted **and** the per-condition filter (`weapon \| mods \| pre-patch snapshot non-empty`) yields zero conditions |
| 3 | `Noop` | not blacklisted, has eligible conditions, every `ConditionResult.Noop == true` |
| 4 | `Expanded` | not blacklisted, has eligible conditions, at least one `ConditionResult.Noop == false` |

Notes:

- Blacklisted is checked first regardless of conditions. Patcher already skips blacklisted quests (`Pipeline/Quest/QuestPatcher.cs:34-37`), so their conditions remain at their pre-patch values; status is independent of that.
- `NoEligibleConditions` quests still ship to the report with an empty `Conditions` list (today they are silently dropped because `BuildQuestList` returns the projection unfiltered but the empty-condition QuestResult still propagates — actually verified: the projection is `.Select(...).ToList()` and emits the QuestResult with `Conditions=[]`, so they already ship; the bug is purely that `Noop=true` masks them). The status fix makes them distinguishable.

### Reporting changes

`Reporting/InspectorResult.cs`:

- Add `public required QuestStatus Status { get; init; }` on `QuestResult`.
- Remove the computed `bool Noop` getter on `QuestResult`. Consumers use `Status == QuestStatus.Noop`.
- Keep `ConditionResult.Noop` unchanged — it remains the per-condition primitive that quest-level classification consumes.

`Reporting/ReportBuilder.cs`:

- After computing the per-quest `conditions` list, compute `Status` using the precedence table.
- Do not change which quests are emitted. `NoEligibleConditions` quests already flow through; they now carry a distinguishing tag.

### HTML report changes

`Reporting/Assets/report.html` (or the equivalent template file producing the filter bar):

- Replace single `<input id="hide-noop" type="checkbox">` with four status checkboxes inside a `<fieldset class="status-filter">`:
  - `Blacklisted` — unchecked by default
  - `No eligible` — unchecked by default
  - `Noop` — unchecked by default
  - `Expanded` — **checked** by default

`Reporting/Assets/report.js`:

- `renderQuests` writes `div.dataset.status = q.status` (in addition to the existing `dataset.search`).
- Badge selection in the quest header switches on `q.status`:
  - `Blacklisted` → red badge `BLACKLISTED`
  - `NoEligibleConditions` → gray badge `NO ELIGIBLE`
  - `Noop` → existing `badge-noop`
  - `Expanded` → existing `+N` count badge
- `filterQuests` now intersects the search text with the set of checked statuses:
  ```js
  const allowed = new Set([...statusCheckboxes].filter(c => c.checked).map(c => c.value));
  row.classList.toggle('hidden', !matchSearch || !allowed.has(row.dataset.status));
  ```
- Session-state persistence: replace `hideNoop: bool` with `statusFilter: string[]`. Restore by checking the named boxes on load; default to `["Expanded"]` when absent.

### CSS

Two new badge classes in the existing report stylesheet:

- `badge-blacklisted` — red background, white text, same shape as existing badges.
- `badge-empty` — neutral gray.

### Tests

New `Reporting/QuestStatusTests.cs`:

- `Blacklisted_BeatsConditions` — blacklisted quest with expanded conditions still reports `Blacklisted`.
- `NoEligible_WhenAllConditionsFilteredOut` — quest whose only conditions are SkillCheck/FindItem types reports `NoEligibleConditions`.
- `Noop_WhenAllConditionsNoop` — eligible conditions, none changed.
- `Expanded_WhenAnyConditionExpanded` — one expanded condition is enough.

Existing snapshot or builder tests that assert on `quest.Noop`: update to assert on `quest.Status`.

## Migration

- No on-disk config migration. Status is a computed report field.
- Inspector reports generated by older binaries do not have `status` — the JS sets a fallback `q.status ??= (q.noop ? 'Noop' : 'Expanded')` so old reports still render.

## Open questions

None.
