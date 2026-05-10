# Quest config migration fix — design

Date: 2026-05-10
Branch: public

## Problem

Patches authored against the legacy TypeScript version of AddMissingQuestRequirements
do not migrate correctly into the C# rewrite. Two concrete defects:

1. **Weapon-overrides file rename is unhandled.** The TS version shipped its file
   as `OverriddenWeapons.jsonc`. The C# loader hard-codes
   `WeaponOverrides.jsonc` only (`Pipeline/Override/OverrideReader.cs:15`). When a
   user drops a legacy patch into `user/mods/<mod>/MissingQuestWeapons/`, the
   loader silently returns a default empty config. The
   `Migrations.v0_to_v1_Weapons` chain — which converts `CustomCategories` and
   `Override` into the new `customTypeRules` / `manualTypeOverrides` — never
   fires.

2. **Multi-entry-per-quest-id collapse.** Legacy `QuestOverrides.jsonc` files use
   multiple entries with the same `id` but different `conditions[]` arrays to
   scope overrides per CounterCreator condition (e.g. the legacy
   `zz_guiltyman-addmissingquestweaponrequirements` patch ships six entries for
   `66a75b44243a6548ff5e5ff9` — Gun Connoisseur — one per caliber sub-condition).
   `MergeHelper.MergeQuestEntries` keys the result map by `entry.Id` only and
   always merges incoming entries into `existingEntries[0]`. All six per-condition
   overrides collapse into one, losing per-condition scoping. This matches the
   reported symptom "quests with conditions filled are not there".

A third defect — comments and formatting are stripped because
`System.Text.Json` cannot round-trip JSONC — is acknowledged but **deferred**;
fixing it requires either a Newtonsoft swap with all migrations re-ported or a
hand-rolled token-level JSONC editor, neither of which is in scope for this
session.

## Goals

- Legacy patches whose only weapon-overrides file is `OverriddenWeapons.jsonc`
  load and migrate.
- Legacy `QuestOverrides.jsonc` files that ship multiple entries per quest id
  (each scoped to a different condition) survive merging without collapse.
- Existing v1 / v2 behaviour unchanged: same-file same-id same-conditions
  entries continue to obey the configured `OverrideBehaviour`.
- All existing tests still pass; new tests cover the new paths.

Non-goals:
- Comment / whitespace preservation across migration.
- Rewriting migrated files back to disk.
- Any change to the migration chain itself
  (`Migrations.v0_to_v1_Weapons` / `v1_to_v2_Weapons` / `v1_to_v2_Quest`).

## Fix 1 — Legacy weapon-overrides filename

### Change

`Pipeline/Override/OverrideReader.cs`:

- Add a private constant `LegacyWeaponOverridesFile = "OverriddenWeapons.jsonc"`.
- Inside `ApplyWeaponOverrides`, replace the direct
  `Path.Combine(mqwDir, WeaponOverridesFile)` with a small resolver:

  ```csharp
  var path = Path.Combine(mqwDir, WeaponOverridesFile);
  if (!File.Exists(path))
  {
      var legacy = Path.Combine(mqwDir, LegacyWeaponOverridesFile);
      if (File.Exists(legacy))
      {
          path = legacy;
      }
  }
  ```

- Hand the resolved path to `ConfigLoader.LoadFromFile<WeaponOverridesFile>` as
  before. The migration array (`WeaponsMigrations`) does not change — a legacy
  file has no `version` key, so the migrator runs `v0_to_v1_Weapons` and
  `v1_to_v2_Weapons` automatically.

Precedence: new name always wins over legacy. We do not load both. We do not
rename or otherwise touch the user's mod folder on disk.

### Verification

- New test in `AddMissingQuestRequirements.Tests/Pipeline/Override/OverrideReaderTests.cs`
  (or the closest existing fixture):
  - Mod directory contains only `OverriddenWeapons.jsonc` with a `CustomCategories`
    entry → `OverriddenSettings.TypeRules` contains the migrated rule.
- New test: directory contains both files → contents of new file win, legacy is
  ignored.
- Existing tests (where only the new filename is present) continue to pass.

## Fix 2 — Same-id different-conditions entries must not collapse

### Change

`Util/MergeHelper.cs`, method `MergeQuestEntries`:

The result map is already `Dictionary<string, List<QuestOverrideEntry>>` —
multiple entries per id is a supported shape. Replace the
"merge into `existingEntries[0]`" logic with conditions-set matching:

- For each incoming `entry`:
  1. Compute effective behaviour as today (`entry.Behaviour ?? fileDefaultBehaviour`).
  2. If id is not in `result`, add a new list with one cloned entry. (No change.)
  3. Else search `existingEntries` for an entry whose `Conditions` is set-equal
     to `entry.Conditions`.
  4. If a match exists, apply the effective behaviour against that one
     specific entry (IGNORE → keep existing, REPLACE → swap with clone, MERGE →
     `MergeEntries(match, entry)` and write back, DELETE → remove match from
     list and drop list if empty).
  5. If no match exists, behaviour determines the action:
     - DELETE: no-op (nothing to delete — matches today's behaviour for the
       missing-id case).
     - IGNORE: no-op (incoming does not modify state for this id; preserves
       the pre-rewrite "IGNORE skips incoming" semantic).
     - REPLACE / MERGE / null: append `CloneEntry(entry)` to the list. Under
       (id, conditions-set) granularity, REPLACE acts at the per-entry level
       — appending a new condition-scoped entry rather than wiping the
       whole id list.

- Add a static helper:

  ```csharp
  private static bool SameConditionSet(
      IReadOnlyCollection<string> a,
      IReadOnlyCollection<string> b)
  {
      if (a.Count != b.Count) return false;
      var set = new HashSet<string>(a, StringComparer.Ordinal);
      return b.All(set.Contains);
  }
  ```

  Empty `Conditions` lists compare equal — both represent "applies to all
  conditions for this quest", so they should still merge.

### Edge cases

- An entry with empty `Conditions` and an entry with non-empty `Conditions` for
  the same id are *not* matched. They will coexist in the result list. This
  preserves authored intent: the empty-list one applies broadly, the scoped
  one applies narrowly. The downstream consumer of `OverriddenSettings`
  already iterates the full list per id, so both apply.
- DELETE on a non-matching entry is a no-op (today it would erase the entire
  id; the new behaviour is strictly safer).

### Verification

- New test: incoming list has 6 entries with the same id but pairwise-distinct
  `Conditions` arrays → result list under that id has 6 entries.
- New test: incoming list has two entries with id X — one with
  `Conditions = []`, one with `Conditions = ["c1"]` — result has both.
- New test: existing entry for (id X, conditions = [c1]); incoming entry for
  same (id X, conditions = [c1]) under MERGE → entries merge (today's
  behaviour preserved).
- Existing `MergeHelperTests` continue to pass — none of them rely on
  collapsing same-id different-condition entries.

## Out of scope this session — comment preservation

Recorded for follow-up. No code change here. Two paths the next iteration
should weigh:

- **Newtonsoft swap.** `JObject.Parse` with `CommentHandling = Load` keeps
  comments as `JValue` siblings. All migration helpers in `Migrations.cs` would
  need to be ported (`JsonObject` / `JsonArray` → `JObject` / `JArray`).
  Re-serialisation drops some whitespace.
- **Token-level JSONC editor.** Read raw text + emit targeted rename / remove /
  insert edits driven by an AST diff. Comments and most whitespace survive.
  More code, more tests.

Either is multi-day work. Not blocking the patch-loading fix.

## Risk and rollback

- Both fixes are additive (Fix 1) or strictly more permissive (Fix 2). No
  existing valid config changes meaning.
- Each fix lives in one file plus tests. Easy to revert in isolation.
- No new dependencies, no on-disk side effects.
