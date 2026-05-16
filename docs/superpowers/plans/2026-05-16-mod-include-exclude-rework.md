# Mod Include/Exclude Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `includedMods`/`excludedMods` cross-field leak (Clay Pigeons / Tester-10 bug, Light's Out silent shrink) and add `includedModBundles` / `excludedModBundles` cartesian-bundle fields with a configurable per-entry cap.

**Architecture:** Three changes land together in `WeaponModsExpander`:

1. **Per-field semantics**: `IncludedMods` only appends to `weaponModsInclusive`. `ExcludedMods` only appends to `weaponModsExclusive`. Drop-on-either-field semantics removed entirely (no real-world usage).
2. **Cartesian bundle fields**: new `IncludedModBundles: List<List<string>>` (and symmetric `ExcludedModBundles`) on `QuestOverrideEntry`. Each outer entry is one AND-bundle; each inner entry is a *set* (type-name → members, bare id → singleton-set). Output = cartesian product of the inner sets → one singleton-of-tuple group per combination.
3. **Cap**: global `ModConfig.ModBundleCartesianCap` (default 500). When an entry's product would exceed cap, truncate output and emit warning.

Quest config schema bumps v2 → v3. Migration is warning-only (key names unchanged); a non-empty legacy `excludedMods` triggers a one-line "behavior changed" warning on load.

**Tech Stack:** C# / .NET 9, xUnit + FluentAssertions, `System.Text.Json` (`JsonObject` for migrations).

---

## File Structure

| File | Purpose |
|------|---------|
| `AddMissingQuestRequirements/Models/QuestOverrideEntry.cs` | Add `IncludedModBundles` / `ExcludedModBundles` fields, rewrite XML docs for `IncludedMods` / `ExcludedMods` |
| `AddMissingQuestRequirements/Models/ModConfig.cs` | Add `ModBundleCartesianCap` (int, default 500) |
| `AddMissingQuestRequirements/Util/MergeHelper.cs` | Clone + union-merge new bundle fields |
| `AddMissingQuestRequirements/Pipeline/Attachment/WeaponModsExpander.cs` | Replace symmetric `RewriteField` with per-field calls; add cartesian bundle resolver + cap |
| `AddMissingQuestRequirements/Config/Migrations.cs` | Add `v2_to_v3_Quest` (warn-only; carries semantic change notice) |
| `AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs` | Bump `CurrentVersion` 2 → 3; append new migration |
| `AddMissingQuestRequirements/Reporting/InspectorResult.cs` | Add `OverrideIncludedModBundles` / `OverrideExcludedModBundles` |
| `AddMissingQuestRequirements/Reporting/ReportBuilder.cs` | Populate new fields |
| `AddMissingQuestRequirements/Reporting/Assets/report.js` | Render new fields in inspector |
| `AddMissingQuestRequirements/Spt/ModMetadata.cs` | Version `2.0.5` → `2.1.0` |
| `CLAUDE.md` | Rewrite §Mod-group expansion section to match new semantics |
| `AddMissingQuestRequirements.Tests/Pipeline/Attachment/WeaponModsExpanderTests.cs` | Add per-field semantics + cartesian + cap tests; remove drop-from-inclusive tests |
| `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs` | Add bundle-field merge tests |
| `AddMissingQuestRequirements.Tests/Config/MigrationTests.cs` (or existing file) | Add `v2_to_v3_Quest` warning test |

---

### Task 0: Pin the bug — failing reproductions in WeaponModsExpanderTests

**Goal:** Lock current bugs into red tests before any production code changes. These three tests fail today and must pass after Task 2.

**Files:**
- Modify: `AddMissingQuestRequirements.Tests/Pipeline/Attachment/WeaponModsExpanderTests.cs`

**Acceptance Criteria:**
- [ ] Three new tests added at end of file (under a `// ── Bug repros (per-field semantics) ──` region marker).
- [ ] All three FAIL on current `master`.
- [ ] Build is green: `dotnet build -c Release` succeeds.

**Verify:** `dotnet test --filter "FullyQualifiedName~WeaponModsExpanderTests" -c Release` → 3 NEW failures, all existing tests still pass.

**Steps:**

- [ ] **Step 1: Add the three repro tests at end of `WeaponModsExpanderTests.cs` (before final closing brace)**

```csharp
    // ── Bug repros (per-field semantics) ──────────────────────────────────────

    [Fact]
    public void included_mods_must_not_leak_into_exclusive_field()
    {
        // Clay Pigeons / Tester-10 reproduction. IncludedMods appended only to
        // weaponModsInclusive; weaponModsExclusive untouched when ModsExclusiveBefore
        // was empty.
        var expander = MakeExpander();
        var condition = MakeCondition(
            inclusive: [["scope_a"]],
            exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id                = "q1",
            ModsExpansionMode = ExpansionMode.NoExpansion,
            IncludedMods      = ["scope_b"],
        };

        RunExpand(expander, condition, ov);

        condition.WeaponModsExclusive.Should().BeEmpty(
            "IncludedMods must not append to the exclusive field");
        condition.WeaponModsInclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["scope_a"], ["scope_b"] });
    }

    [Fact]
    public void excluded_mods_must_append_to_exclusive_not_drop_from_inclusive()
    {
        // Light's Out reproduction. ExcludedMods appended only to weaponModsExclusive;
        // weaponModsInclusive untouched.
        var expander = MakeExpander();
        var condition = MakeCondition(
            inclusive: [["scope_a"]],
            exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id                = "q1",
            ModsExpansionMode = ExpansionMode.NoExpansion,
            ExcludedMods      = ["Stock"],
        };

        RunExpand(expander, condition, ov);

        condition.WeaponModsInclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["scope_a"] },
            "ExcludedMods must not affect the inclusive field");
        condition.WeaponModsExclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["stock_a"], ["stock_b"], ["stock_c"] });
    }

    [Fact]
    public void excluded_mods_appends_to_existing_exclusive_field()
    {
        // Light's Out variant with pre-populated exclusive. ExcludedMods appended;
        // pre-existing groups preserved; original inclusive untouched.
        var expander = MakeExpander();
        var condition = MakeCondition(
            inclusive: [["scope_a"]],
            exclusive: [["supp_a"]]);
        var ov = new QuestOverrideEntry
        {
            Id                = "q1",
            ModsExpansionMode = ExpansionMode.NoExpansion,
            ExcludedMods      = ["stock_a"],
        };

        RunExpand(expander, condition, ov);

        condition.WeaponModsInclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["scope_a"] });
        condition.WeaponModsExclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["supp_a"], ["stock_a"] });
    }
```

- [ ] **Step 2: Run new tests and confirm they fail for the documented reason**

Run: `dotnet test --filter "FullyQualifiedName~WeaponModsExpanderTests.included_mods_must_not_leak_into_exclusive_field|FullyQualifiedName~WeaponModsExpanderTests.excluded_mods_must_append_to_exclusive_not_drop_from_inclusive|FullyQualifiedName~WeaponModsExpanderTests.excluded_mods_appends_to_existing_exclusive_field" -c Release`

Expected: 3 FAILED. First fails because `WeaponModsExclusive` will contain a singleton for `scope_b`. Second fails because `WeaponModsExclusive` will be empty (the type-name `Stock` is currently used as a drop filter). Third fails for the same reason.

- [ ] **Step 3: Confirm no existing test regressed**

Run: `dotnet test --filter "FullyQualifiedName~WeaponModsExpanderTests" -c Release`

Expected: only the 3 new tests fail; all previously-passing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add AddMissingQuestRequirements.Tests/Pipeline/Attachment/WeaponModsExpanderTests.cs
git commit -m "test(mods-expander): pin per-field include/exclude bug repros

Adds three currently-failing tests that lock in the user-reported bugs:
- IncludedMods leaking into weaponModsExclusive (Clay Pigeons, Tester-10)
- ExcludedMods dropping from inclusive instead of appending to exclusive (Light's Out)

Will go green in the WeaponModsExpander rework task."
```

---

### Task 1: Model + config + merge plumbing for bundles + cap

**Goal:** Land schema changes (no behavior yet): `IncludedModBundles` / `ExcludedModBundles` on `QuestOverrideEntry`, `ModBundleCartesianCap` on `ModConfig`, merge support in `MergeHelper`. No `WeaponModsExpander` change yet.

**Files:**
- Modify: `AddMissingQuestRequirements/Models/QuestOverrideEntry.cs`
- Modify: `AddMissingQuestRequirements/Models/ModConfig.cs`
- Modify: `AddMissingQuestRequirements/Util/MergeHelper.cs`
- Modify: `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs`

**Acceptance Criteria:**
- [ ] `QuestOverrideEntry` has `IncludedModBundles: List<List<string>>` and `ExcludedModBundles: List<List<string>>` (both `init`, default `[]`, JSON names `includedModBundles` / `excludedModBundles`).
- [ ] XML docs on `IncludedMods` and `ExcludedMods` rewritten to state per-field semantics (no "whichever is being processed").
- [ ] `ModConfig.ModBundleCartesianCap` (int, default `500`, `[JsonPropertyName("modBundleCartesianCap")]`).
- [ ] `MergeHelper.CloneEntry` and `MergeEntries` copy/union both new bundle fields. Union uses sorted-list deep equality per bundle (existing `IncludedMods` pattern uses `.Union(...)` on a flat list, which won't dedupe nested lists — must implement bundle-aware dedupe).
- [ ] `MergeHelperTests` has a new test verifying bundle merge dedupes by structural equality (same inner ids, any order, treated as duplicate).
- [ ] Solution builds.
- [ ] All existing tests still pass.

**Verify:** `dotnet build -c Release && dotnet test --filter "Category!=Integration" -c Release` → no regressions.

**Steps:**

- [ ] **Step 1: Update `QuestOverrideEntry.cs` — replace `IncludedMods` and `ExcludedMods` XML doc; append new fields**

Replace lines 40-63 with:

```csharp
    /// <summary>
    /// Attachment IDs or type names appended as new <b>singleton</b> groups to
    /// <c>weaponModsInclusive</c> only. Each type-name entry expands to one singleton
    /// per type member. Applied in <see cref="ExpansionMode.Auto"/> and
    /// <see cref="ExpansionMode.NoExpansion"/>; under <see cref="ExpansionMode.WhitelistOnly"/>
    /// the inclusive field is rebuilt from these alone.
    ///
    /// <para>For multi-attachment AND bundles (e.g. <c>barrel + scope</c>) use
    /// <see cref="IncludedModBundles"/> instead.</para>
    /// </summary>
    [JsonPropertyName("includedMods")]
    public List<string> IncludedMods { get; init; } = [];

    /// <summary>
    /// Attachment IDs or type names appended as new <b>singleton</b> groups to
    /// <c>weaponModsExclusive</c> only. Each type-name entry expands to one singleton
    /// per type member. Forbids weapons carrying any listed attachment.
    ///
    /// <para><b>Behavior changed in config v3 (mod 2.1.0):</b> previously this list
    /// dropped groups from both fields. Now it only appends to the exclusive field.</para>
    /// </summary>
    [JsonPropertyName("excludedMods")]
    public List<string> ExcludedMods { get; init; } = [];

    /// <summary>
    /// Cartesian AND-bundles appended to <c>weaponModsInclusive</c>. Each outer entry
    /// is a list of <i>sets</i>: a type-name expands to its member set, a bare id is
    /// a singleton-set. The output is the cartesian product of the sets, emitted as
    /// one group per combination (each group an AND-bundle of one id per set).
    ///
    /// <para>Example: <c>[["m60_barrels", "aimpoint_scopes"]]</c> with 2 barrels and
    /// 5 scopes emits 10 bundles of shape <c>[barrel, scope]</c>.</para>
    ///
    /// <para>Per-entry product is capped by <see cref="ModConfig.ModBundleCartesianCap"/>
    /// (default 500). Exceeding entries are truncated and logged.</para>
    /// </summary>
    [JsonPropertyName("includedModBundles")]
    public List<List<string>> IncludedModBundles { get; init; } = [];

    /// <summary>
    /// Cartesian AND-bundles appended to <c>weaponModsExclusive</c>. Same shape and
    /// expansion rules as <see cref="IncludedModBundles"/>.
    /// </summary>
    [JsonPropertyName("excludedModBundles")]
    public List<List<string>> ExcludedModBundles { get; init; } = [];
```

- [ ] **Step 2: Add `ModBundleCartesianCap` to `ModConfig.cs` (insert after `WeaponLikeAncestors`)**

```csharp
    /// <summary>
    /// Per-entry cap on the cartesian product produced by
    /// <see cref="QuestOverrideEntry.IncludedModBundles"/> /
    /// <see cref="QuestOverrideEntry.ExcludedModBundles"/>. When a single entry's
    /// product would exceed this many groups, output is truncated and the patcher
    /// logs a warning naming the quest/condition. Default 500.
    /// </summary>
    [JsonPropertyName("modBundleCartesianCap")]
    public int ModBundleCartesianCap { get; init; } = 500;
```

- [ ] **Step 3: Update `MergeHelper.cs` — bundle-aware union**

Add helper method at bottom of class (above the closing brace of `MergeHelper`):

```csharp
    /// <summary>
    /// Unions two lists of bundles by structural equality. A bundle's key is the
    /// sorted, distinct, ordinal-joined member-id list (same convention as
    /// WeaponModsExpander's dedupe key).
    /// </summary>
    private static List<List<string>> UnionBundles(List<List<string>> a, List<List<string>> b)
    {
        var seen   = new HashSet<string>();
        var result = new List<List<string>>(a.Count + b.Count);

        foreach (var bundle in a.Concat(b))
        {
            var key = string.Join("\0", bundle.Distinct().OrderBy(x => x, StringComparer.Ordinal));
            if (seen.Add(key))
            {
                result.Add([..bundle]);
            }
        }

        return result;
    }
```

Update `CloneEntry` to include the new fields (replace lines 296-307):

```csharp
    private static QuestOverrideEntry CloneEntry(QuestOverrideEntry e) => new()
    {
        Id                  = e.Id,
        Behaviour           = e.Behaviour,
        ExpansionMode       = e.ExpansionMode,
        Conditions          = [..e.Conditions],
        IncludedWeapons     = [..e.IncludedWeapons],
        ExcludedWeapons     = [..e.ExcludedWeapons],
        ModsExpansionMode   = e.ModsExpansionMode,
        IncludedMods        = [..e.IncludedMods],
        ExcludedMods        = [..e.ExcludedMods],
        IncludedModBundles  = e.IncludedModBundles.Select(b => (List<string>)[..b]).ToList(),
        ExcludedModBundles  = e.ExcludedModBundles.Select(b => (List<string>)[..b]).ToList(),
    };
```

Update `MergeEntries` to include the new fields (replace lines 309-323):

```csharp
    private static QuestOverrideEntry MergeEntries(QuestOverrideEntry a, QuestOverrideEntry b) => new()
    {
        Id                  = a.Id,
        Behaviour           = a.Behaviour,
        ExpansionMode       = MoreRestrictive(a.ExpansionMode, b.ExpansionMode),
        Conditions          = [..a.Conditions.Union(b.Conditions)],
        IncludedWeapons     = [..a.IncludedWeapons.Union(b.IncludedWeapons)],
        ExcludedWeapons     = [..a.ExcludedWeapons.Union(b.ExcludedWeapons)],
        ModsExpansionMode   = MoreRestrictive(a.ModsExpansionMode, b.ModsExpansionMode),
        IncludedMods        = [..a.IncludedMods.Union(b.IncludedMods)],
        ExcludedMods        = [..a.ExcludedMods.Union(b.ExcludedMods)],
        IncludedModBundles  = UnionBundles(a.IncludedModBundles, b.IncludedModBundles),
        ExcludedModBundles  = UnionBundles(a.ExcludedModBundles, b.ExcludedModBundles),
    };
```

- [ ] **Step 4: Add merge test in `MergeHelperTests.cs`**

Insert after the existing `IncludedMods`/`ExcludedMods` merge test (read the file first to find the exact location near line 607):

```csharp
    [Fact]
    public void merge_unions_included_and_excluded_mod_bundles_structurally()
    {
        var a = new QuestOverrideEntry
        {
            Id                 = "q1",
            IncludedModBundles = [["barrel_a", "scope_a"], ["barrel_b"]],
            ExcludedModBundles = [["mod_x"]],
        };
        var b = new QuestOverrideEntry
        {
            Id                 = "q1",
            // duplicate of first bundle in reverse order — should dedupe
            IncludedModBundles = [["scope_a", "barrel_a"], ["barrel_c"]],
            ExcludedModBundles = [["mod_x"], ["mod_y"]],
        };

        var merged = MergeHelper.MergeQuestEntries(
            new Dictionary<string, List<QuestOverrideEntry>> { ["q1"] = [a] },
            [b],
            OverrideBehaviour.MERGE)["q1"].Single();

        merged.IncludedModBundles.Should().HaveCount(3);
        merged.IncludedModBundles[0].Should().BeEquivalentTo(["barrel_a", "scope_a"]);
        merged.IncludedModBundles[1].Should().BeEquivalentTo(["barrel_b"]);
        merged.IncludedModBundles[2].Should().BeEquivalentTo(["barrel_c"]);
        merged.ExcludedModBundles.Should().HaveCount(2);
    }
```

Note: confirm the exact signature of `MergeHelper.MergeQuestEntries` and adjust the call shape before running. If the existing tests use a different invocation, mirror them.

- [ ] **Step 5: Build and run all unit tests**

Run: `dotnet build -c Release && dotnet test --filter "Category!=Integration" -c Release`

Expected: build green; only the 3 Task 0 bug repros fail; the new merge test passes; all other tests pass.

- [ ] **Step 6: Commit**

```bash
git add AddMissingQuestRequirements/Models/QuestOverrideEntry.cs AddMissingQuestRequirements/Models/ModConfig.cs AddMissingQuestRequirements/Util/MergeHelper.cs AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs
git commit -m "feat(model): add includedModBundles, excludedModBundles, modBundleCartesianCap

Schema plumbing only — WeaponModsExpander still uses the old code path.
MergeHelper unions the new bundle fields by structural equality so
cross-file merges dedupe equivalent bundles regardless of order."
```

---

### Task 2: WeaponModsExpander rework — per-field semantics + cartesian bundles + cap

**Goal:** Replace the symmetric `RewriteField` call site with two field-specific calls. `IncludedMods` + `IncludedModBundles` append only to inclusive; `ExcludedMods` + `ExcludedModBundles` append only to exclusive. Drop-on-either-field semantics removed. Cap enforced per entry.

**Files:**
- Modify: `AddMissingQuestRequirements/Pipeline/Attachment/WeaponModsExpander.cs`
- Modify: `AddMissingQuestRequirements.Tests/Pipeline/Attachment/WeaponModsExpanderTests.cs`

**Acceptance Criteria:**
- [ ] All three Task 0 bug repros now pass.
- [ ] Existing `ExcludedMods drops…` tests removed (they encoded the broken semantic). Replaced by `ExcludedMods appends…` tests covering the new semantic.
- [ ] New tests for `IncludedModBundles` cartesian expansion (with type-names and bare ids mixed).
- [ ] New tests for cap behavior: under-cap produces full product; over-cap truncates output and emits warning via `CapturingModLogger`.
- [ ] New test for `ExcludedModBundles` cartesian on exclusive field.
- [ ] All existing tests not specifically about drop-on-inclusive still pass.

**Verify:** `dotnet test --filter "FullyQualifiedName~WeaponModsExpanderTests" -c Release` → all pass.

**Steps:**

- [ ] **Step 1: Rewrite `WeaponModsExpander.cs`**

Replace the entire file with:

```csharp
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Shared;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Pipeline.Attachment;

/// <summary>
/// Rewrites <c>weaponModsInclusive</c> and <c>weaponModsExclusive</c> under
/// intra-group AND, cross-group OR semantics.
///
/// <para><b>Per-field overrides (since 2.1.0):</b>
/// <c>includedMods</c> + <c>includedModBundles</c> append only to
/// <c>weaponModsInclusive</c>. <c>excludedMods</c> + <c>excludedModBundles</c>
/// append only to <c>weaponModsExclusive</c>. Neither set drops groups from
/// either field.</para>
///
/// <para><b>Singleton consensus expansion (Auto):</b> when a field has ≥2
/// singletons sharing a single minimal covering type, expansion emits one
/// singleton per type member (plus <c>canBeUsedAs</c> aliases). Multi-item
/// groups are AND bundles and pass through verbatim.</para>
///
/// <para><b>Bundle fields:</b> each bundle is a list of <i>sets</i>
/// (type-name = members, bare id = singleton-set). Output = cartesian product,
/// one AND-bundle per combination. Truncated to
/// <see cref="ModConfig.ModBundleCartesianCap"/>.</para>
/// </summary>
public sealed class WeaponModsExpander : IConditionExpander
{
    private readonly AttachmentCategorizationResult _attachmentCategorization;
    private readonly TypeSelector _typeSelector;
    private readonly INameResolver _nameResolver;

    public WeaponModsExpander(AttachmentCategorizationResult attachmentCategorization, INameResolver nameResolver)
    {
        _attachmentCategorization = attachmentCategorization;
        _typeSelector             = new TypeSelector();
        _nameResolver             = nameResolver;
    }

    public void Expand(
        ConditionNode condition,
        QuestOverrideEntry? overrideEntry,
        CategorizationResult categorization,
        ModConfig config,
        IModLogger logger)
    {
        var hasOverrideWork =
            overrideEntry is not null
            && (overrideEntry.IncludedMods.Count > 0
                || overrideEntry.ExcludedMods.Count > 0
                || overrideEntry.IncludedModBundles.Count > 0
                || overrideEntry.ExcludedModBundles.Count > 0
                || overrideEntry.ModsExpansionMode != ExpansionMode.Auto);

        if (condition.WeaponModsInclusive.Count == 0
            && condition.WeaponModsExclusive.Count == 0
            && !hasOverrideWork)
        {
            return;
        }

        var newInclusive = RewriteField(
            condition.WeaponModsInclusive,
            condition.Id,
            "weaponModsInclusive",
            overrideEntry?.ModsExpansionMode ?? ExpansionMode.Auto,
            overrideEntry?.IncludedMods ?? [],
            overrideEntry?.IncludedModBundles ?? [],
            config,
            logger);

        var newExclusive = RewriteField(
            condition.WeaponModsExclusive,
            condition.Id,
            "weaponModsExclusive",
            overrideEntry?.ModsExpansionMode ?? ExpansionMode.Auto,
            overrideEntry?.ExcludedMods ?? [],
            overrideEntry?.ExcludedModBundles ?? [],
            config,
            logger);

        condition.WeaponModsInclusive.Clear();
        condition.WeaponModsInclusive.AddRange(newInclusive);

        condition.WeaponModsExclusive.Clear();
        condition.WeaponModsExclusive.AddRange(newExclusive);
    }

    // ── Core rewrite ─────────────────────────────────────────────────────────

    /// <summary>
    /// Rewrites a single field. <paramref name="appendEntries"/> are appended as
    /// singletons (post-expansion of type names). <paramref name="appendBundles"/>
    /// are appended as cartesian AND-bundles, capped by config.
    /// </summary>
    private List<List<string>> RewriteField(
        List<List<string>> original,
        string conditionId,
        string fieldName,
        ExpansionMode mode,
        IReadOnlyList<string> appendEntries,
        IReadOnlyList<List<string>> appendBundles,
        ModConfig config,
        IModLogger logger)
    {
        var output = new List<List<string>>();

        if (mode == ExpansionMode.WhitelistOnly)
        {
            // Originals discarded; field rebuilt from append-only sources.
            foreach (var id in ResolveEntries(appendEntries))
            {
                output.Add([id]);
            }
        }
        else if (mode == ExpansionMode.NoExpansion)
        {
            foreach (var group in original)
            {
                var kept = FilterByUnknownHandling(group, config, logger, conditionId, fieldName);
                if (kept.Count == 0)
                {
                    continue;
                }
                output.Add(kept);
            }

            foreach (var id in ResolveEntries(appendEntries))
            {
                output.Add([id]);
            }
        }
        else
        {
            // Auto: partition into singletons / multis after unknown-handling filter.
            var singletons = new List<List<string>>();
            var multis     = new List<List<string>>();

            foreach (var group in original)
            {
                var kept = FilterByUnknownHandling(group, config, logger, conditionId, fieldName);
                if (kept.Count == 0)
                {
                    continue;
                }
                if (kept.Count == 1)
                {
                    singletons.Add(kept);
                }
                else
                {
                    multis.Add(kept);
                }
            }

            // Field-level strict type-consensus expansion (≥2 singletons sharing
            // a single minimal covering type).
            var willExpand = false;
            IReadOnlySet<string>? typeMembers = null;

            if (singletons.Count >= 2)
            {
                string? sharedType = null;
                var matched = true;

                foreach (var group in singletons)
                {
                    var selection = _typeSelector.Select(
                        [group[0]],
                        _attachmentCategorization.AttachmentToType,
                        _attachmentCategorization.AttachmentTypes,
                        new Dictionary<string, string>());

                    if (selection.BestType is null)
                    {
                        matched = false;
                        break;
                    }

                    sharedType ??= selection.BestType;
                    if (!string.Equals(sharedType, selection.BestType, StringComparison.Ordinal))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched
                    && sharedType is not null
                    && _attachmentCategorization.AttachmentTypes.TryGetValue(sharedType, out var members))
                {
                    willExpand = true;
                    typeMembers = members;
                }
            }

            foreach (var g in multis)
            {
                output.Add(g);
            }

            if (willExpand && typeMembers is not null)
            {
                AppendFieldExpansion(output, typeMembers);
            }
            else
            {
                foreach (var s in singletons)
                {
                    output.Add(s);
                }
            }

            foreach (var id in ResolveEntries(appendEntries))
            {
                output.Add([id]);
            }
        }

        // Cartesian bundles appended in all modes (including WhitelistOnly:
        // they extend the rebuilt list, they don't get discarded).
        AppendCartesianBundles(output, appendBundles, config, logger, conditionId, fieldName);

        return DedupeGroupsInOrder(output);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves entries to ids: type-name entries expand to all members of that
    /// attachment type; bare ids are returned as-is.
    /// </summary>
    private IEnumerable<string> ResolveEntries(IEnumerable<string> entries)
    {
        foreach (var entry in entries)
        {
            if (_attachmentCategorization.AttachmentTypes.TryGetValue(entry, out var members))
            {
                foreach (var id in members)
                {
                    yield return id;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    /// Resolves a bundle's inner entries to per-position sets:
    /// type-name → member set; bare id → singleton-set.
    /// </summary>
    private List<IReadOnlyList<string>> ResolveBundleSets(IReadOnlyList<string> bundle)
    {
        var sets = new List<IReadOnlyList<string>>(bundle.Count);
        foreach (var entry in bundle)
        {
            if (_attachmentCategorization.AttachmentTypes.TryGetValue(entry, out var members))
            {
                sets.Add(members.ToList());
            }
            else
            {
                sets.Add([entry]);
            }
        }
        return sets;
    }

    /// <summary>
    /// Emits the cartesian product of each bundle's resolved sets as AND-bundles.
    /// Truncates per entry to <see cref="ModConfig.ModBundleCartesianCap"/> and
    /// warns on truncation.
    /// </summary>
    private void AppendCartesianBundles(
        List<List<string>> output,
        IReadOnlyList<List<string>> bundles,
        ModConfig config,
        IModLogger logger,
        string conditionId,
        string fieldName)
    {
        if (bundles.Count == 0)
        {
            return;
        }

        var cap = Math.Max(1, config.ModBundleCartesianCap);

        for (var bIx = 0; bIx < bundles.Count; bIx++)
        {
            var bundle = bundles[bIx];
            if (bundle.Count == 0)
            {
                continue;
            }

            var sets = ResolveBundleSets(bundle);
            if (sets.Any(s => s.Count == 0))
            {
                logger.Warning(
                    $"[mods-expander] condition '{conditionId}' field '{fieldName}': " +
                    $"bundle #{bIx} contains an empty set; bundle dropped.");
                continue;
            }

            long fullProduct = 1;
            foreach (var s in sets)
            {
                fullProduct *= s.Count;
                if (fullProduct > int.MaxValue)
                {
                    fullProduct = int.MaxValue;
                    break;
                }
            }

            var truncated = fullProduct > cap;
            var emit = truncated ? cap : (int)fullProduct;

            // Iterate cartesian indexes 0..emit-1.
            var idx = new int[sets.Count];
            for (var k = 0; k < emit; k++)
            {
                var combo = new List<string>(sets.Count);
                for (var p = 0; p < sets.Count; p++)
                {
                    combo.Add(sets[p][idx[p]]);
                }
                output.Add(combo);

                // Increment mixed-radix counter.
                for (var p = sets.Count - 1; p >= 0; p--)
                {
                    idx[p]++;
                    if (idx[p] < sets[p].Count)
                    {
                        break;
                    }
                    idx[p] = 0;
                }
            }

            if (truncated)
            {
                logger.Warning(
                    $"[mods-expander] condition '{conditionId}' field '{fieldName}': " +
                    $"bundle #{bIx} cartesian product {fullProduct} exceeds cap {cap}; " +
                    $"output truncated to {cap} groups.");
            }
        }
    }

    private List<string> FilterByUnknownHandling(
        IReadOnlyList<string> group,
        ModConfig config,
        IModLogger logger,
        string conditionId,
        string fieldName)
    {
        var buckets = GroupExpander.BucketAndLog(
            group,
            _attachmentCategorization,
            config.UnknownWeaponHandling,
            logger,
            conditionId,
            fieldName,
            _nameResolver);

        var categorizedSet       = buckets.Categorized.ToHashSet();
        var uncategorizedInDbSet = buckets.UncategorizedInDb.ToHashSet();
        var notInDbSet           = buckets.NotInDb.ToHashSet();

        var kept = new List<string>(group.Count);
        var seen = new HashSet<string>();

        foreach (var id in group)
        {
            var keep =
                categorizedSet.Contains(id)
                || (buckets.KeepUncategorizedInDb && uncategorizedInDbSet.Contains(id))
                || (buckets.KeepNotInDb && notInDbSet.Contains(id));

            if (keep && seen.Add(id))
            {
                kept.Add(id);
            }
        }

        return kept;
    }

    private void AppendFieldExpansion(List<List<string>> output, IReadOnlySet<string> typeMembers)
    {
        var emittedIds = new List<string>(typeMembers.Count);

        foreach (var member in typeMembers)
        {
            output.Add([member]);
            emittedIds.Add(member);
        }

        foreach (var emitted in emittedIds)
        {
            if (!_attachmentCategorization.CanBeUsedAs.TryGetValue(emitted, out var aliases))
            {
                continue;
            }

            foreach (var alias in aliases)
            {
                if (_attachmentCategorization.AttachmentTypes.TryGetValue(alias, out var members))
                {
                    foreach (var member in members)
                    {
                        output.Add([member]);
                    }
                }
                else
                {
                    output.Add([alias]);
                }
            }
        }
    }

    private static List<List<string>> DedupeGroupsInOrder(List<List<string>> groups)
    {
        var seenKeys = new HashSet<string>();
        var result   = new List<List<string>>(groups.Count);

        foreach (var group in groups)
        {
            var key = string.Join("\0", group.Distinct().OrderBy(x => x, StringComparer.Ordinal));
            if (seenKeys.Add(key))
            {
                result.Add(group);
            }
        }

        return result;
    }
}
```

- [ ] **Step 2: Remove or rewrite drop-semantic tests in `WeaponModsExpanderTests.cs`**

The following tests encoded the now-deleted drop-on-inclusive semantic. Read `WeaponModsExpanderTests.cs` first, then delete (or convert to the new semantic) these methods by name:
- `excluded_mods_drops_multi_item_group_when_any_member_excluded`
- `excluded_mods_type_name_drops_every_group_whose_members_all_in_type`
- `excluded_mods_drops_expanded_singletons_that_land_on_excluded_id`

Replace them with these new tests (inserted at the same location):

```csharp
    // ── ExcludedMods appends singletons to weaponModsExclusive only ──────────

    [Fact]
    public void excluded_mods_under_auto_appends_to_exclusive_only()
    {
        var expander = MakeExpander();
        var condition = MakeCondition(
            inclusive: [["scope_a", "supp_a"]],
            exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id           = "q1",
            ExcludedMods = ["scope_a"],
        };

        RunExpand(expander, condition, ov);

        condition.WeaponModsInclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["scope_a", "supp_a"] },
            "inclusive must be untouched by ExcludedMods");
        condition.WeaponModsExclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["scope_a"] });
    }

    [Fact]
    public void excluded_mods_type_name_appends_one_singleton_per_member_to_exclusive()
    {
        var expander = MakeExpander();
        var condition = MakeCondition(
            inclusive: [["scope_a"]],
            exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id           = "q1",
            ExcludedMods = ["Stock"],
        };

        RunExpand(expander, condition, ov);

        condition.WeaponModsInclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["scope_a"] });
        condition.WeaponModsExclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["stock_a"], ["stock_b"], ["stock_c"] });
    }
```

- [ ] **Step 3: Add cartesian-bundle tests**

Append at end of class:

```csharp
    // ── IncludedModBundles cartesian expansion ────────────────────────────────

    [Fact]
    public void included_mod_bundles_cartesian_with_type_names_expands_to_product_groups()
    {
        var expander = MakeExpander();
        var condition = MakeCondition(inclusive: [], exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id                 = "q1",
            IncludedModBundles = [["Stock", "Scope"]],
        };

        RunExpand(expander, condition, ov);

        // 3 stocks × 2 scopes = 6 bundles
        condition.WeaponModsInclusive.Should().HaveCount(6);
        condition.WeaponModsInclusive.Should().AllSatisfy(g =>
            g.Should().HaveCount(2,
                "each emitted bundle is one stock id + one scope id"));
        condition.WeaponModsExclusive.Should().BeEmpty(
            "IncludedModBundles must not leak into exclusive");
    }

    [Fact]
    public void included_mod_bundles_with_bare_id_treats_it_as_singleton_set()
    {
        var expander = MakeExpander();
        var condition = MakeCondition(inclusive: [], exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id                 = "q1",
            IncludedModBundles = [["stock_a", "Scope"]],
        };

        RunExpand(expander, condition, ov);

        // bare stock_a × 2 scopes = 2 bundles
        condition.WeaponModsInclusive.Should().BeEquivalentTo(new List<List<string>>
        {
            ["stock_a", "scope_a"],
            ["stock_a", "scope_b"],
        });
    }

    [Fact]
    public void excluded_mod_bundles_appends_cartesian_to_exclusive_only()
    {
        var expander = MakeExpander();
        var condition = MakeCondition(inclusive: [["scope_a"]], exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id                 = "q1",
            ExcludedModBundles = [["Suppressor", "scope_b"]],
        };

        RunExpand(expander, condition, ov);

        condition.WeaponModsInclusive.Should().BeEquivalentTo(
            new List<List<string>> { ["scope_a"] });
        condition.WeaponModsExclusive.Should().BeEquivalentTo(new List<List<string>>
        {
            ["supp_a", "scope_b"],
            ["supp_b", "scope_b"],
        });
    }

    [Fact]
    public void mod_bundle_cap_truncates_product_and_warns()
    {
        var expander = MakeExpander();
        var condition = MakeCondition(inclusive: [], exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id                 = "q1",
            IncludedModBundles = [["Stock", "Scope", "Suppressor"]], // 3×2×2 = 12
        };
        var config = new ModConfig { ModBundleCartesianCap = 5 };
        var logger = new CapturingModLogger();

        expander.Expand(condition, ov, CategorizationResult.Empty, config, logger);

        condition.WeaponModsInclusive.Should().HaveCount(5);
        logger.Warnings.Should().Contain(w => w.Contains("cap 5") && w.Contains("12"));
    }

    [Fact]
    public void mod_bundle_under_cap_emits_full_product()
    {
        var expander = MakeExpander();
        var condition = MakeCondition(inclusive: [], exclusive: []);
        var ov = new QuestOverrideEntry
        {
            Id                 = "q1",
            IncludedModBundles = [["Stock", "Scope"]], // 3×2 = 6
        };
        var config = new ModConfig { ModBundleCartesianCap = 500 };
        var logger = new CapturingModLogger();

        expander.Expand(condition, ov, CategorizationResult.Empty, config, logger);

        condition.WeaponModsInclusive.Should().HaveCount(6);
        logger.Warnings.Should().BeEmpty();
    }
```

Notes for the implementer:
- Confirm `CategorizationResult.Empty` exists or use whatever empty/default the existing tests use (`MakeCondition`/`RunExpand` already abstract this — look at the existing fixtures and mirror them).
- Confirm `CapturingModLogger.Warnings` is the public collection (read `AddMissingQuestRequirements/Util/CapturingModLogger.cs`).
- `RunExpand` is the existing helper in this test file — it uses `NullModLogger` by default; the cap test calls `expander.Expand(...)` directly to supply `CapturingModLogger`.

- [ ] **Step 4: Verify the three Task 0 bug repros now pass**

Run: `dotnet test --filter "FullyQualifiedName~WeaponModsExpanderTests" -c Release`

Expected: 0 failures. The three bug repros plus the new cartesian/cap tests all pass.

- [ ] **Step 5: Verify full unit suite still green**

Run: `dotnet test --filter "Category!=Integration" -c Release`

Expected: 0 failures.

- [ ] **Step 6: Commit**

```bash
git add AddMissingQuestRequirements/Pipeline/Attachment/WeaponModsExpander.cs AddMissingQuestRequirements.Tests/Pipeline/Attachment/WeaponModsExpanderTests.cs
git commit -m "fix(mods-expander): per-field include/exclude + cartesian bundles + cap

- IncludedMods / IncludedModBundles append only to weaponModsInclusive
- ExcludedMods / ExcludedModBundles append only to weaponModsExclusive
- Drop-on-either-field semantics removed (no real-world usage)
- ModBundleCartesianCap (default 500) caps per-entry cartesian output

Fixes Clay Pigeons, Tester-10, Light's Out feedback reports."
```

---

### Task 3: Quest config schema v2 → v3 migration

**Goal:** Bump the quest-overrides schema version. The migration is warning-only: keys stay the same, but a non-empty `excludedMods` triggers a one-time log telling the user the semantic flipped. Authors must re-read their `excludedMods` entries.

**Files:**
- Modify: `AddMissingQuestRequirements/Config/Migrations.cs`
- Modify: `AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs`
- Create: `AddMissingQuestRequirements.Tests/Config/MigrationsTests.cs` (or extend existing migration test file — check first)

**Acceptance Criteria:**
- [ ] `Migrations.v2_to_v3_Quest(JsonObject) -> JsonObject` exists. Reads the `overrides` array; for any entry where `excludedMods` is a non-empty array, attaches a synthetic `_warnExcludedModsSemanticChange = true` marker (or alternatively just leaves the object unchanged and emits the warning at load time via `MigrationWriter`). Pick the simpler path that matches the existing migration style.
- [ ] `OverrideReader.CurrentVersion` bumped 2 → 3.
- [ ] `OverrideReader.QuestMigrations` includes the new migration.
- [ ] A unit test confirms an old v2 file with non-empty `excludedMods` loads, the migration runs, and the loader emits a warning visible via `CapturingModLogger`.
- [ ] A v1 → v3 chain still works (read existing migration tests for the pattern).

**Verify:** `dotnet test --filter "Category!=Integration&FullyQualifiedName~Migration" -c Release`

**Steps:**

- [ ] **Step 1: Read existing migration tests for the canonical pattern**

```bash
find AddMissingQuestRequirements.Tests -name "*Migration*"
```

Read whatever comes back. Mirror its structure.

- [ ] **Step 2: Add `v2_to_v3_Quest` to `Migrations.cs`**

Append after `v1_to_v2_Quest`:

```csharp
    /// <summary>
    /// Migrates QuestOverrides from version 2 to version 3 (semantic flip).
    /// In v2 <c>excludedMods</c> dropped groups from both inclusive AND exclusive
    /// fields. In v3 it appends singletons to the exclusive field only.
    /// Schema keys unchanged. The loader emits a one-line warning if the migrated
    /// file contains any non-empty <c>excludedMods</c> so authors can review.
    /// </summary>
    public static JsonObject v2_to_v3_Quest(JsonObject obj)
    {
        if (obj["overrides"] is not JsonArray overrides)
        {
            return obj;
        }

        var anyNonEmpty = false;
        foreach (var item in overrides)
        {
            if (item is JsonObject entry
                && entry["excludedMods"] is JsonArray arr
                && arr.Count > 0)
            {
                anyNonEmpty = true;
                break;
            }
        }

        if (anyNonEmpty)
        {
            // Sentinel surfaced as a load-time warning by ConfigLoader (see Step 4).
            obj["_v2_to_v3_excludedMods_semantic_changed"] = true;
        }

        return obj;
    }
```

- [ ] **Step 3: Update `OverrideReader.cs`**

Replace lines 25 and 29:

```csharp
    private const int CurrentVersion = 3;
    private const int CurrentAttachmentVersion = 1;

    private static readonly Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>[]
        QuestMigrations = [Migrations.v0_to_v1, Migrations.v1_to_v2_Quest, Migrations.v2_to_v3_Quest];
```

- [ ] **Step 4: Surface the sentinel as a warning**

Find where `ConfigLoader.LoadFromFile<Models.QuestOverridesFile>` results are consumed in `OverrideReader.ApplyQuestOverrides`. Right after the existing `foreach (var w in loaded.Warnings)` loop, add:

```csharp
        // v2→v3 semantic-flip notice. The migration plants a sentinel; surface it once.
        if (loaded.RawMigratedRoot is { } raw
            && raw["_v2_to_v3_excludedMods_semantic_changed"]?.GetValue<bool>() == true)
        {
            logger?.Warning(
                $"[migration] '{path}': excludedMods semantics changed in config v3 — " +
                $"these IDs now append to weaponModsExclusive instead of dropping groups. " +
                $"Review your overrides.");
        }
```

Note: `RawMigratedRoot` may not exist today. If `ConfigLoader.LoadFromFile` doesn't expose the migrated `JsonObject`, the implementer should add a minimal pass-through: either expose it on the loader's return type, or move the warning into `ConfigLoader` itself. Pick whichever is one-line cheaper. Re-read `AddMissingQuestRequirements/Config/ConfigLoader.cs` before deciding.

After surfacing, strip the sentinel out of the JsonObject in `v2_to_v3_Quest` itself (so it never tries to deserialize onto `QuestOverridesFile`) — or strip it in `ConfigLoader` before deserialization. The implementer picks the cleaner of the two.

- [ ] **Step 5: Migration test**

In the migration tests file (file path determined in Step 1), add:

```csharp
[Fact]
public void v2_to_v3_quest_flags_non_empty_excluded_mods()
{
    var json = JsonNode.Parse("""
    {
      "version": 2,
      "overrides": [
        { "id": "q1", "excludedMods": ["all_flashlights"] },
        { "id": "q2", "excludedMods": [] }
      ]
    }
    """)!.AsObject();

    var result = Migrations.v2_to_v3_Quest(json);

    result["_v2_to_v3_excludedMods_semantic_changed"]?.GetValue<bool>()
        .Should().BeTrue();
}

[Fact]
public void v2_to_v3_quest_no_flag_when_all_excluded_mods_empty()
{
    var json = JsonNode.Parse("""
    {
      "version": 2,
      "overrides": [ { "id": "q1", "excludedMods": [] } ]
    }
    """)!.AsObject();

    var result = Migrations.v2_to_v3_Quest(json);

    result.ContainsKey("_v2_to_v3_excludedMods_semantic_changed").Should().BeFalse();
}
```

- [ ] **Step 6: Run all tests**

Run: `dotnet test --filter "Category!=Integration" -c Release`

Expected: green.

- [ ] **Step 7: Commit**

```bash
git add AddMissingQuestRequirements/Config/Migrations.cs AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs AddMissingQuestRequirements.Tests
git commit -m "feat(migrations): bump QuestOverrides schema v2->v3 with semantic-flip notice

v3 changes excludedMods from drop-filter to exclusive-append. Migration
plants a sentinel that the override reader surfaces as a one-line load
warning so authors notice and review."
```

---

### Task 4: Inspector report — surface new bundle fields

**Goal:** Inspector HTML/JSON reports include `overrideIncludedModBundles` / `overrideExcludedModBundles` so users can see what their bundle config produced.

**Files:**
- Modify: `AddMissingQuestRequirements/Reporting/InspectorResult.cs`
- Modify: `AddMissingQuestRequirements/Reporting/ReportBuilder.cs`
- Modify: `AddMissingQuestRequirements/Reporting/Assets/report.js`

**Acceptance Criteria:**
- [ ] `ConditionEntry` (or whichever class holds `OverrideIncludedMods`) gains `OverrideIncludedModBundles: List<List<string>>` and `OverrideExcludedModBundles: List<List<string>>`.
- [ ] `ReportBuilder` populates them from `overrideEntry?.IncludedModBundles` / `ExcludedModBundles`.
- [ ] `report.js` renders the bundles when non-empty (one row per bundle, joined with " + " between members), under the same UI block as the existing override include/exclude pills.
- [ ] Inspector serve-mode still renders correctly for an unaffected quest (regression smoke).

**Verify:**
1. `dotnet build -c Release` — green.
2. Manually trigger inspector via the CLI (`dotnet run --project AddMissingQuestRequirements.Inspector -- <existing-inspector-config.json>`) and inspect a quest with a non-empty `includedModBundles` — confirm both JSON and HTML carry the field.

**Steps:**

- [ ] **Step 1: Add the two fields to `InspectorResult.cs` (after line 99)**

```csharp
    public List<List<string>> OverrideIncludedModBundles { get; init; } = [];
    public List<List<string>> OverrideExcludedModBundles { get; init; } = [];
```

- [ ] **Step 2: Populate in `ReportBuilder.cs`**

Read the block around line 225 first. After the existing `OverrideExcludedMods = overrideEntry?.ExcludedMods.ToList() ?? []` assignment, add:

```csharp
                            OverrideIncludedModBundles = overrideEntry?.IncludedModBundles
                                .Select(b => b.ToList()).ToList() ?? [],
                            OverrideExcludedModBundles = overrideEntry?.ExcludedModBundles
                                .Select(b => b.ToList()).ToList() ?? [],
```

- [ ] **Step 3: Render in `report.js`**

Read the block around lines 407-414 for the existing override-mods rendering pattern, then extend it to also iterate `c.overrideIncludedModBundles` and `c.overrideExcludedModBundles`. For each bundle render the inner members joined by " + ". Mirror the existing visual style (color/badge for include vs exclude).

- [ ] **Step 4: Smoke-test the inspector**

```bash
dotnet run --project AddMissingQuestRequirements.Inspector --configuration Release -- <path-to-inspector-config.json>
```

Open the resulting HTML; pick a quest in your config that uses `includedModBundles`; confirm the bundles render visibly. Also `cat inspector-report.json | jq '.[].Conditions[].OverrideIncludedModBundles' | head` to confirm JSON shape.

- [ ] **Step 5: Commit**

```bash
git add AddMissingQuestRequirements/Reporting
git commit -m "feat(report): surface includedModBundles / excludedModBundles in inspector"
```

---

### Task 5: Version bump + CLAUDE.md docs

**Goal:** Final polish — bump mod version and update CLAUDE.md so future contributors read the new semantics.

**Files:**
- Modify: `AddMissingQuestRequirements/Spt/ModMetadata.cs`
- Modify: `CLAUDE.md`

**Acceptance Criteria:**
- [ ] `ModMetadata.Version` is `new("2.1.0")`.
- [ ] CLAUDE.md §"Mod-group expansion" rewritten to describe per-field semantics, `includedModBundles` / `excludedModBundles`, and the cap.
- [ ] CLAUDE.md §"Improvements over the TS version" gains a numbered entry for the per-field rework + cartesian bundles.
- [ ] Solution builds and all tests pass.

**Verify:** `dotnet build -c Release && dotnet test --filter "Category!=Integration" -c Release`

**Steps:**

- [ ] **Step 1: Bump version in `ModMetadata.cs` line 18**

```csharp
    public override Version Version { get; init; } = new("2.1.0");
```

- [ ] **Step 2: Update CLAUDE.md §"Mod-group expansion"**

Open `CLAUDE.md`, find the `### Mod-group expansion` heading. Replace the section body with text describing:
1. The base singleton/multi partitioning under Auto (unchanged behavior; can be lifted near-verbatim from the existing section).
2. **Per-field overrides** (new): `includedMods` + `includedModBundles` → only `weaponModsInclusive`; `excludedMods` + `excludedModBundles` → only `weaponModsExclusive`. Drop-on-either-field semantics is gone.
3. **Cartesian bundle resolution**: bundle inner entries are sets; output is the cartesian product.
4. **Cap**: `ModConfig.ModBundleCartesianCap` (default 500); over-cap entries are truncated with a warning.

The implementer should keep the writing style consistent with the rest of CLAUDE.md (terse, technical, code references). Don't add emojis.

Also under §"Improvements over the TS version", insert a new numbered item between the existing items #1 and #2:

> **Per-field mod overrides + cartesian bundles** — `includedMods` / `excludedMods` only touch their respective `weaponModsInclusive` / `weaponModsExclusive` fields. New `includedModBundles` / `excludedModBundles` produce AND-bundles via cartesian product of type-sets, capped by `modBundleCartesianCap`. Replaces the v2-era symmetric drop semantic.

- [ ] **Step 3: Final verification**

```bash
dotnet build -c Release
dotnet test --filter "Category!=Integration" -c Release
```

Expected: green.

- [ ] **Step 4: Commit**

```bash
git add AddMissingQuestRequirements/Spt/ModMetadata.cs CLAUDE.md
git commit -m "chore(release): 2.1.0 — per-field mod overrides + cartesian bundles

Mod version bumped per CLAUDE.md release rule (minor: new feature +
behavior change on excludedMods). CLAUDE.md sections updated."
```

---

## Self-Review

**Spec coverage** — each of the three user requirements maps to a task:
- Per-field include/exclude semantics (both feedbacks) → Task 2 (with Task 0 pinning, Task 1 plumbing, Task 3 migration warning).
- Cartesian bundles for `barrel + scope` authoring (feedback_2) → Task 1 (model) + Task 2 (expander) + Task 4 (report).
- Configurable cap with truncate+warn → Task 1 (model) + Task 2 (expander).

**Placeholder scan** — every step contains either real code or a concrete "read existing pattern X then mirror" instruction. The migration step has a known-unknown (`RawMigratedRoot` may not exist on `ConfigLoader`'s return type) and explicitly tells the implementer to check and pick the cheapest one-line route. That's a real decision left to the implementer rather than a placeholder.

**Type consistency** — `IncludedModBundles` / `ExcludedModBundles` consistent everywhere (model, merge, expander, report). `ModBundleCartesianCap` consistent. Method names `AppendCartesianBundles` / `ResolveBundleSets` introduced in Task 2 and never referenced elsewhere by a different name.
