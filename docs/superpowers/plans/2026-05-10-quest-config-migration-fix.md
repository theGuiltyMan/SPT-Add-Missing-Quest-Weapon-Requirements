# Quest Config Migration Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two defects that prevent legacy AMQR patches (TS era) from migrating into the C# rewrite — `OverriddenWeapons.jsonc` filename change and multi-entry-per-quest-id collapse during merge.

**Architecture:** Two surgical changes. (1) `OverrideReader` falls back to the legacy filename when the new one is absent; the existing migration chain handles the rest. (2) `MergeHelper.MergeQuestEntries` keys merging by `(id, conditions-set)` instead of `id` alone, so per-condition scoped overrides survive. Both changes are additive — no existing valid config changes meaning.

**Tech Stack:** .NET 9, xUnit, FluentAssertions. Mutates `Pipeline/Override/OverrideReader.cs` and `Util/MergeHelper.cs`. New tests in the existing `OverrideReaderTests` and `MergeHelperTests` classes.

**Spec:** `docs/superpowers/specs/2026-05-10-quest-config-migration-fix-design.md`

---

### Task 1: Legacy weapon-overrides filename fallback

**Goal:** `OverrideReader` loads `OverriddenWeapons.jsonc` when `WeaponOverrides.jsonc` is absent so the existing v0→v1→v2 weapon-overrides migration chain runs against legacy patches.

**Files:**
- Modify: `AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs:13-16, 99-128`
- Test: `AddMissingQuestRequirements.Tests/Pipeline/Override/OverrideReaderTests.cs` (extend existing class, append two new `[Fact]` tests at end)

**Acceptance Criteria:**
- [ ] New constant `LegacyWeaponOverridesFile = "OverriddenWeapons.jsonc"` declared in `OverrideReader`.
- [ ] `ApplyWeaponOverrides` resolves the path: prefer `WeaponOverrides.jsonc`; fall back to `OverriddenWeapons.jsonc` if and only if the new name is absent.
- [ ] When only the legacy file exists, `Read()` returns `OverriddenSettings` populated as if the file had been loaded under the new name.
- [ ] When both files exist, the new file is loaded and the legacy file is ignored.
- [ ] When neither exists, behaviour is unchanged (default-empty config).
- [ ] All previously passing tests still pass.

**Verify:** `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~OverrideReaderTests" -c Release` → all green, two new tests included.

**Steps:**

- [ ] **Step 1: Add the failing test for legacy-only file.**

Append at the end of `AddMissingQuestRequirements.Tests/Pipeline/Override/OverrideReaderTests.cs` (inside the existing class, before the closing brace):

```csharp
    // ── Legacy filename fallback ─────────────────────────────────────────────

    [Fact]
    public void Legacy_OverriddenWeapons_filename_is_loaded_when_new_name_absent()
    {
        // v0 TS-era file: ships under the old name and uses the old "Override" key.
        // The v0_to_v1 + v1_to_v2 weapon migrations should run on it.
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modDir);
        _tempDirs.Add(modDir);

        var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
        Directory.CreateDirectory(mqwDir);

        File.WriteAllText(Path.Combine(mqwDir, "OverriddenWeapons.jsonc"), """
        {
            "OverrideBehaviour": "MERGE",
            "Override": {
                "weapon_legacy_id": "BoltActionSniperRifle"
            }
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.ManualTypeOverrides.Should().ContainKey("weapon_legacy_id");
        result.ManualTypeOverrides["weapon_legacy_id"].Should().Be("BoltActionSniperRifle");
    }

    [Fact]
    public void New_WeaponOverrides_filename_wins_over_legacy_when_both_present()
    {
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modDir);
        _tempDirs.Add(modDir);

        var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
        Directory.CreateDirectory(mqwDir);

        File.WriteAllText(Path.Combine(mqwDir, "WeaponOverrides.jsonc"), """
        {
            "manualTypeOverrides": { "weapon_new_id": "AssaultRifle" }
        }
        """);
        File.WriteAllText(Path.Combine(mqwDir, "OverriddenWeapons.jsonc"), """
        {
            "Override": { "weapon_legacy_id": "BoltActionSniperRifle" }
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.ManualTypeOverrides.Should().ContainKey("weapon_new_id");
        result.ManualTypeOverrides.Should().NotContainKey("weapon_legacy_id");
    }
```

- [ ] **Step 2: Run the new tests; confirm they fail.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~OverrideReaderTests.Legacy_OverriddenWeapons_filename_is_loaded_when_new_name_absent|FullyQualifiedName~OverrideReaderTests.New_WeaponOverrides_filename_wins_over_legacy_when_both_present" -c Release`

Expected: both tests fail. The legacy-only test fails because `ManualTypeOverrides` is empty (file never loaded). The both-present test should pass already (new name is what the loader looks at) but is included to lock the precedence in.

- [ ] **Step 3: Implement the fallback.**

In `AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs`, add the legacy constant alongside the existing constants (around line 16):

```csharp
    private const string LegacyWeaponOverridesFile = "OverriddenWeapons.jsonc";
```

Replace the path resolution at the top of `ApplyWeaponOverrides` (around line 107). The current line is:

```csharp
        var path = Path.Combine(mqwDir, WeaponOverridesFile);
```

Replace it with:

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

Leave the rest of `ApplyWeaponOverrides` untouched. The migration array (`WeaponsMigrations`) is unchanged: a v0 file has no `version` key, so `ConfigMigrator.Migrate` runs `v0_to_v1_Weapons` then `v1_to_v2_Weapons` automatically.

- [ ] **Step 4: Re-run the new tests; confirm they pass.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~OverrideReaderTests.Legacy_OverriddenWeapons_filename_is_loaded_when_new_name_absent|FullyQualifiedName~OverrideReaderTests.New_WeaponOverrides_filename_wins_over_legacy_when_both_present" -c Release`

Expected: both tests PASS.

- [ ] **Step 5: Run the full OverrideReaderTests suite to confirm no regressions.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~OverrideReaderTests" -c Release`

Expected: all green.

- [ ] **Step 6: Commit.**

```bash
git add AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs \
        AddMissingQuestRequirements.Tests/Pipeline/Override/OverrideReaderTests.cs
git commit -m "fix: load legacy OverriddenWeapons.jsonc when new name absent"
```

---

### Task 2: Stop collapsing same-id different-conditions quest entries

**Goal:** `MergeHelper.MergeQuestEntries` matches incoming entries against existing ones by both `Id` and the set of `Conditions`. Entries with the same id but different `conditions[]` arrays survive as separate list members instead of collapsing into entry `[0]`.

**Files:**
- Modify: `AddMissingQuestRequirements/Util/MergeHelper.cs:166-210, 280-307` (rewrite the `MergeQuestEntries` body and add a `SameConditionSet` helper)
- Test: `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs` (append three new `[Fact]` tests)

**Acceptance Criteria:**
- [ ] `MergeHelper.MergeQuestEntries` no longer routes every same-id entry into `existingEntries[0]`. Entries are matched by `(Id, Conditions-as-set)`.
- [ ] Entry with empty `Conditions` and entry with non-empty `Conditions` for the same id are *not* matched and coexist in the result list.
- [ ] When a match exists, the configured behaviour (`IGNORE` / `REPLACE` / `MERGE` / `DELETE`) is applied to the matching entry only, not to all entries under the id.
- [ ] When no match exists, the incoming entry is appended (cloned) to the id's list. `DELETE` with no match is a no-op.
- [ ] All existing `MergeHelperTests` and `OverrideReaderTests` still pass.

**Verify:** `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~MergeHelperTests|FullyQualifiedName~OverrideReaderTests" -c Release` → all green.

**Steps:**

- [ ] **Step 1: Write the failing tests.**

Append to `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs` inside the class (before the existing `MergeTypeRules*` block, or at the end — order doesn't matter):

```csharp
    // ── Conditions-set scoping ───────────────────────────────────────────────

    [Fact]
    public void MergeQuestEntries_same_id_distinct_conditions_coexist_under_MERGE()
    {
        var existing = new Dictionary<string, List<QuestOverrideEntry>>();
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "q1", Conditions = ["c1"], IncludedWeapons = ["a"] },
            new() { Id = "q1", Conditions = ["c2"], IncludedWeapons = ["b"] },
            new() { Id = "q1", Conditions = ["c3"], IncludedWeapons = ["c"] },
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.MERGE);

        result["q1"].Should().HaveCount(3);
        result["q1"].Select(e => e.Conditions.Single())
            .Should().BeEquivalentTo(["c1", "c2", "c3"]);
    }

    [Fact]
    public void MergeQuestEntries_empty_and_scoped_conditions_coexist()
    {
        var existing = new Dictionary<string, List<QuestOverrideEntry>>
        {
            ["q1"] = [new() { Id = "q1", Conditions = [], IncludedWeapons = ["broad"] }],
        };
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "q1", Conditions = ["c1"], IncludedWeapons = ["scoped"] },
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.MERGE);

        result["q1"].Should().HaveCount(2);
        result["q1"].Single(e => e.Conditions.Count == 0).IncludedWeapons
            .Should().BeEquivalentTo(["broad"]);
        result["q1"].Single(e => e.Conditions.SequenceEqual(new[] { "c1" })).IncludedWeapons
            .Should().BeEquivalentTo(["scoped"]);
    }

    [Fact]
    public void MergeQuestEntries_same_id_same_conditions_still_merge_under_MERGE()
    {
        var existing = new Dictionary<string, List<QuestOverrideEntry>>
        {
            ["q1"] = [new() { Id = "q1", Conditions = ["c1"], IncludedWeapons = ["a"] }],
        };
        var incoming = new List<QuestOverrideEntry>
        {
            new() { Id = "q1", Conditions = ["c1"], IncludedWeapons = ["b"] },
        };

        var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.MERGE);

        result["q1"].Should().HaveCount(1);
        result["q1"][0].IncludedWeapons.Should().BeEquivalentTo(["a", "b"]);
    }
```

- [ ] **Step 2: Run new tests; confirm they fail.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~MergeHelperTests.MergeQuestEntries_same_id_distinct_conditions_coexist_under_MERGE|FullyQualifiedName~MergeHelperTests.MergeQuestEntries_empty_and_scoped_conditions_coexist|FullyQualifiedName~MergeHelperTests.MergeQuestEntries_same_id_same_conditions_still_merge_under_MERGE" -c Release`

Expected: the first two FAIL (the current implementation collapses every same-id entry into `[0]`). The third PASSES (single existing, single incoming, same conditions — current code already handles this).

- [ ] **Step 3: Rewrite `MergeQuestEntries` to match by (Id, conditions-set).**

In `AddMissingQuestRequirements/Util/MergeHelper.cs`, replace the entire `MergeQuestEntries` method body (lines roughly 166-210). The new body:

```csharp
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

            if (!result.TryGetValue(entry.Id, out var existingEntries))
            {
                if (effective == OverrideBehaviour.DELETE)
                {
                    // Nothing to delete for an unknown id.
                    continue;
                }

                result[entry.Id] = [CloneEntry(entry)];
                continue;
            }

            var matchIndex = existingEntries.FindIndex(e => SameConditionSet(e.Conditions, entry.Conditions));

            if (matchIndex < 0)
            {
                if (effective == OverrideBehaviour.DELETE)
                {
                    // No matching entry to delete — leave existing list untouched.
                    continue;
                }

                existingEntries.Add(CloneEntry(entry));
                continue;
            }

            switch (effective)
            {
                case OverrideBehaviour.IGNORE:
                    break; // keep matching existing entry, skip incoming
                case OverrideBehaviour.REPLACE:
                    existingEntries[matchIndex] = CloneEntry(entry);
                    break;
                case OverrideBehaviour.MERGE:
                    existingEntries[matchIndex] = MergeEntries(existingEntries[matchIndex], entry);
                    break;
                case OverrideBehaviour.DELETE:
                    existingEntries.RemoveAt(matchIndex);
                    if (existingEntries.Count == 0)
                    {
                        result.Remove(entry.Id);
                    }
                    break;
            }
        }

        return result;
    }

    private static bool SameConditionSet(
        IReadOnlyCollection<string> a,
        IReadOnlyCollection<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        var set = new HashSet<string>(a, StringComparer.Ordinal);
        foreach (var item in b)
        {
            if (!set.Contains(item))
            {
                return false;
            }
        }
        return true;
    }
```

The `CloneEntry` and `MergeEntries` private helpers at the bottom of the file are unchanged.

- [ ] **Step 4: Re-run the three new tests; confirm they pass.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~MergeHelperTests.MergeQuestEntries_same_id_distinct_conditions_coexist_under_MERGE|FullyQualifiedName~MergeHelperTests.MergeQuestEntries_empty_and_scoped_conditions_coexist|FullyQualifiedName~MergeHelperTests.MergeQuestEntries_same_id_same_conditions_still_merge_under_MERGE" -c Release`

Expected: all three PASS.

- [ ] **Step 5: Run the full MergeHelperTests + OverrideReaderTests suites; confirm no regressions.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~MergeHelperTests|FullyQualifiedName~OverrideReaderTests" -c Release`

Expected: all green. Pay particular attention to:
- `MergeQuestEntries_DELETE_removes_quest_entirely` — the previous semantics "DELETE on the file-default removes the entire id list" must still hold when the existing list has exactly one entry and that entry's conditions match the incoming entry. Read the test before running; if the existing test relies on DELETE-without-condition-match removing the id, the behaviour has changed and the test must be updated to reflect the new spec (delete only matching entries; remove id when list is empty). Update the test if needed and document the change in the commit message.

- [ ] **Step 6: Run the full unit suite to confirm nothing else regressed.**

Run: `dotnet test --filter "FullyQualifiedName!~Integration" -c Release`

Expected: all green.

- [ ] **Step 7: Commit.**

```bash
git add AddMissingQuestRequirements/Util/MergeHelper.cs \
        AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs
git commit -m "fix: keep same-id different-conditions quest entries distinct on merge"
```

If `MergeQuestEntries_DELETE_removes_quest_entirely` was modified in Step 5, include that file in the same commit and call out the semantic change in the body of the commit message.

---

## Self-review

- **Spec coverage.** Spec section "Fix 1" → Task 1 (filename fallback test + impl). Spec section "Fix 2" → Task 2 (conditions-set merge test + impl + helper). Spec section "Out of scope" (comment preservation) intentionally has no task — recorded only.
- **Placeholders.** Steps contain literal code, exact paths, and exact commands. No "TODO" / "TBD" / "appropriate error handling" left in.
- **Type consistency.** `QuestOverrideEntry` shape matches `Models/QuestOverrideEntry.cs` (verified). `Conditions` typed as `List<string>`, compared via the `IReadOnlyCollection<string>` `SameConditionSet` helper. `OverrideBehaviour` enum values used in tests (`IGNORE` / `MERGE` / `REPLACE` / `DELETE`) match `Models/OverrideBehaviour.cs`. `CloneEntry` / `MergeEntries` referenced by the rewrite exist at lines 281-307 of `MergeHelper.cs`.
