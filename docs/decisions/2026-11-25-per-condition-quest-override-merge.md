# Per-condition merge for quest override entries

Date: 2026-11-25. Status: accepted. Ships in 3.0.0.

## Context

Quest override entries from every mod's `QuestOverrides.jsonc` are merged in load order:
own folder first, then the rest alphabetically. Until 3.0.0, `MergeHelper.MergeQuestEntries`
matched an incoming entry against an earlier one only when both listed the identical set of
conditions. With no match, `IGNORE` and `DELETE` dropped the entry and `REPLACE` and `MERGE`
appended it. `QuestOverrideResolver` then returned the first specific entry naming the
condition, else the first quest-wide entry.

Consequences:

- One entry per condition, the natural way to author overrides, lost every entry after
  the first for that quest under `IGNORE`, even within one file. With the installed mod set
  at the time, 37 entries were dropped this way.
- `REPLACE` or `MERGE` for one condition of an earlier multi-condition entry was appended
  but never resolved, because the earlier entry matched first.
- Disjoint condition sets were dropped under `IGNORE`, contradicting "don't overwrite, still
  add what's new".

## Decision

The unit of merging is one quest condition.

- `MergeQuestEntries` keeps every entry whole, stamping its effective behaviour
  (`entry.behaviour ?? file overrideBehaviour`) and its file's load-order `Layer`.
- `QuestOverrideResolver.Resolve` folds per condition, file by file. Each file contributes
  its entries targeting the condition specifically, or, if none, its quest-wide entries.
  Each contributed entry is applied by its behaviour: `IGNORE` keeps an earlier result,
  `REPLACE` takes the entry, `MERGE` field-merges it, `DELETE` clears the result.
- An entry targets a condition by sub-condition id or CounterCreator parent id.
  A quest-wide entry (`conditions: []`) covers every condition.

## Alternatives considered

- **Split multi-condition entries at merge time**, keyed by (quest, condition id). Rejected:
  merge time sees only id strings, so it cannot tell that a parent id in one file and a
  sub-condition id in another target the same condition. It would also change the shape of
  the Inspector's config export.
- **Quest-wide entries as a separate fallback (G1)**: any file's condition-specific entry beats
  every quest-wide entry. Rejected: this mod loads first and ships about 40 quest-wide entries,
  so a later `IGNORE` file could override them condition by condition, which contradicts
  `IGNORE`. The chosen rule (G3) treats a quest-wide entry as covering every condition across
  files, and keeps "specific beats quest-wide" only within one file.

## Consequences

- Stored entries include shadowed ones; `ValidateOverrideIds` still warns about unknown ids in
  them (except `DELETE` entries, whose weapon lists are never applied), and the loader's entry
  count includes them.
- `DELETE` entries are always stored, even for a quest with no earlier entries. Dropping them
  there would let a file's quest-wide entry win over the same file's condition-specific `DELETE`
  depending on entry order, and on whether an earlier mod touched an unrelated condition.
- The Inspector's config export flattens all layers into one file; each exported entry carries
  its effective `behaviour`. Re-importing that file applies within-file precedence where
  cross-file precedence applied before, for every behaviour, not only `IGNORE`. Example: an
  earlier file's condition-specific `REPLACE` entry and a later file's quest-wide `DELETE`
  resolve to nothing; after re-import the specific entry shadows the quest-wide one and wins.
- Measured with the Inspector on the shipped config alone: no condition changes. With the
  installed mod set (7 override folders), 10 conditions in 7 quests change, all because a
  later `MERGE` file's entries now merge per condition with this mod's entries.
