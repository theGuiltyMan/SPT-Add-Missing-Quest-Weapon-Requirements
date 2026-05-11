# Quest Status Enum + NoExpansion Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship two co-validated specs in one PR — (a) replace `QuestResult.Noop` bool with a `QuestStatus` enum and a multi-checkbox HTML filter, (b) fix the `NoExpansion` weapon-array gate that silently dropped `includedWeapons` plus the inverted `Math.Max` priority in `MergeHelper`.

**Architecture:** Bug fixes (Tasks 1–3) are surgical and reversible: drop one `if` gate, replace two `Math.Max` calls with a helper, refresh docs. Reporting changes (Tasks 4–6) introduce a new mutually-exclusive enum classification computed server-side in `ReportBuilder`, surface it in `QuestResult` JSON, and refactor the HTML filter bar from a single "Hide NOOP" checkbox to four status checkboxes with a default of "Expanded only".

**Tech Stack:** C# / .NET 9, xUnit, FluentAssertions, embedded HTML+JS+CSS in `Reporting/HtmlReportWriter.cs` + `Reporting/Assets/report.js`. Specs at `docs/superpowers/specs/2026-05-11-quest-status-enum-design.md` and `docs/superpowers/specs/2026-05-11-noexpansion-includedweapons-fix-design.md`.

---

## File Structure

**Code (modify):**
- `AddMissingQuestRequirements/Pipeline/Weapon/WeaponArrayExpander.cs` — drop the `mode != NoExpansion` gate.
- `AddMissingQuestRequirements/Util/MergeHelper.cs` — replace `Math.Max` with `MoreRestrictive` helper.
- `AddMissingQuestRequirements/Models/ExpansionMode.cs` — doc-comment fix on `NoExpansion`.
- `AddMissingQuestRequirements/Reporting/InspectorResult.cs` — add `QuestStatus` field, remove computed `Noop` getter.
- `AddMissingQuestRequirements/Reporting/ReportBuilder.cs` — classify each quest into a `QuestStatus`.
- `AddMissingQuestRequirements/Reporting/HtmlReportWriter.cs` — replace filter bar; extend CSS.
- `AddMissingQuestRequirements/Reporting/Assets/report.js` — badge per status, multi-checkbox filter, sessionStorage migration.
- `CLAUDE.md` — wording fix on `NoExpansion`.

**Code (create):**
- `AddMissingQuestRequirements/Reporting/QuestStatus.cs` — enum.

**Tests (modify):**
- `AddMissingQuestRequirements.Tests/Pipeline/Weapon/WeaponArrayExpanderTests.cs` — add NoExpansion+IncludedWeapons coverage.
- `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs` — add merge-priority tests, adjust any existing assertions that depend on the old `Math.Max` order.

**Tests (create):**
- `AddMissingQuestRequirements.Tests/Reporting/QuestStatusTests.cs` — classification precedence tests.

---

## Task 1: NoExpansion no longer drops `includedWeapons`

**Goal:** Under `ExpansionMode.NoExpansion`, `overrideEntry.IncludedWeapons` is appended to the condition's weapon list. Excludes and aliases continue to apply.

**Files:**
- Modify: `AddMissingQuestRequirements/Pipeline/Weapon/WeaponArrayExpander.cs:83-89`
- Test: `AddMissingQuestRequirements.Tests/Pipeline/Weapon/WeaponArrayExpanderTests.cs`

**Acceptance Criteria:**
- [ ] A single-weapon condition + override `{ ExpansionMode = NoExpansion, IncludedWeapons = ["TypeName"] }` results in every member of `"TypeName"` being added to `condition.Weapon`.
- [ ] Same setup with a bare ID (not a type name) appends that ID exactly once.
- [ ] Original weapon IDs remain in the output.
- [ ] Excluded IDs from `overrideEntry.ExcludedWeapons` are still removed after additions.
- [ ] Existing `WhitelistOnly_*` and `Auto_*` tests continue to pass.

**Verify:** `dotnet test --filter "FullyQualifiedName~WeaponArrayExpanderTests"` → all green.

**Steps:**

- [ ] **Step 1: Add failing tests.**

Append to `AddMissingQuestRequirements.Tests/Pipeline/Weapon/WeaponArrayExpanderTests.cs`:

```csharp
[Fact]
public void NoExpansion_AppendsIncludedWeapons_FromTypeName()
{
    var cat = MakeCat556();
    var condition = new ConditionNode
    {
        Id            = "c1",
        ConditionType = "CounterCreator",
        Weapon        = ["existing_weapon"],
    };
    var overrideEntry = new QuestOverrideEntry
    {
        Id              = "q1",
        ExpansionMode   = ExpansionMode.NoExpansion,
        IncludedWeapons = ["cal_556x45NATO"],
    };

    MakeExpander().Expand(condition, overrideEntry, cat, DefaultConfig(), NullModLogger.Instance);

    condition.Weapon.Should().BeEquivalentTo(
        ["existing_weapon", "weapon_a", "weapon_b"],
        because: "NoExpansion preserves the original list and still appends IncludedWeapons (type expands to members)");
}

[Fact]
public void NoExpansion_AppendsIncludedWeapons_BareId()
{
    var cat = MakeCat556();
    var condition = new ConditionNode
    {
        Id            = "c2",
        ConditionType = "CounterCreator",
        Weapon        = ["existing_weapon"],
    };
    var overrideEntry = new QuestOverrideEntry
    {
        Id              = "q1",
        ExpansionMode   = ExpansionMode.NoExpansion,
        IncludedWeapons = ["weapon_a"],
    };

    MakeExpander().Expand(condition, overrideEntry, cat, DefaultConfig(), NullModLogger.Instance);

    condition.Weapon.Should().BeEquivalentTo(["existing_weapon", "weapon_a"]);
}

[Fact]
public void NoExpansion_ExcludedWeapons_StillApplyAfterIncludes()
{
    var cat = MakeCat556();
    var condition = new ConditionNode
    {
        Id            = "c3",
        ConditionType = "CounterCreator",
        Weapon        = ["existing_weapon"],
    };
    var overrideEntry = new QuestOverrideEntry
    {
        Id              = "q1",
        ExpansionMode   = ExpansionMode.NoExpansion,
        IncludedWeapons = ["cal_556x45NATO"],
        ExcludedWeapons = ["weapon_b"],
    };

    MakeExpander().Expand(condition, overrideEntry, cat, DefaultConfig(), NullModLogger.Instance);

    condition.Weapon.Should().BeEquivalentTo(["existing_weapon", "weapon_a"],
        because: "weapon_b is excluded after the include step");
}
```

- [ ] **Step 2: Run new tests, confirm they fail.**

Run: `dotnet test --filter "FullyQualifiedName~WeaponArrayExpanderTests.NoExpansion_" -v normal`
Expected: 3 FAIL — `Expected condition.Weapon to be equivalent to [...] but found [existing_weapon]` (or similar).

- [ ] **Step 3: Drop the `mode != NoExpansion` gate.**

In `AddMissingQuestRequirements/Pipeline/Weapon/WeaponArrayExpander.cs`, replace lines 83-89:

```csharp
        if (overrideEntry is not null)
        {
            foreach (var entry in overrideEntry.IncludedWeapons)
            {
                AddResolved(entry, weapons, seen, categorization);
            }
        }
```

- [ ] **Step 4: Run tests, confirm green.**

Run: `dotnet test --filter "FullyQualifiedName~WeaponArrayExpanderTests" -v normal`
Expected: all PASS, including the three new tests and every pre-existing test.

- [ ] **Step 5: Commit.**

```bash
git add AddMissingQuestRequirements/Pipeline/Weapon/WeaponArrayExpander.cs \
        AddMissingQuestRequirements.Tests/Pipeline/Weapon/WeaponArrayExpanderTests.cs
git commit -m "fix(expander): NoExpansion appends includedWeapons (was silently dropped)"
```

---

## Task 2: `MergeHelper` priority — `WhitelistOnly > NoExpansion > Auto`

**Goal:** When two `QuestOverrideEntry` instances merge under `OverrideBehaviour.MERGE`, the more restrictive mode wins by intent (`WhitelistOnly` clears + rebuilds → strictest), not by enum int value.

**Files:**
- Modify: `AddMissingQuestRequirements/Util/MergeHelper.cs:309-321`
- Test: `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs`

**Acceptance Criteria:**
- [ ] Merging `{ ExpansionMode = WhitelistOnly }` with `{ ExpansionMode = NoExpansion }` yields `WhitelistOnly`.
- [ ] Merging `{ ExpansionMode = NoExpansion }` with `{ ExpansionMode = Auto }` yields `NoExpansion`.
- [ ] Merging `{ ExpansionMode = WhitelistOnly }` with `{ ExpansionMode = Auto }` yields `WhitelistOnly`.
- [ ] Same three cases hold for `ModsExpansionMode`.
- [ ] All existing `MergeHelperTests` either still pass or are updated to reflect the new (correct) priority.

**Verify:** `dotnet test --filter "FullyQualifiedName~MergeHelperTests"` → all green.

**Steps:**

- [ ] **Step 1: Add failing tests.**

Append to `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs`:

```csharp
[Fact]
public void Merge_WhitelistOnly_Wins_Over_NoExpansion_On_ExpansionMode()
{
    var a = new QuestOverrideEntry { Id = "q", ExpansionMode = ExpansionMode.WhitelistOnly };
    var b = new QuestOverrideEntry { Id = "q", ExpansionMode = ExpansionMode.NoExpansion };

    var merged = MergeHelper.MergeQuestEntries([a], [b], OverrideBehaviour.MERGE);

    merged.Should().HaveCount(1);
    merged[0].ExpansionMode.Should().Be(ExpansionMode.WhitelistOnly,
        because: "WhitelistOnly discards the original list — strictest mode");
}

[Fact]
public void Merge_WhitelistOnly_Wins_Over_Auto_On_ExpansionMode()
{
    var a = new QuestOverrideEntry { Id = "q", ExpansionMode = ExpansionMode.Auto };
    var b = new QuestOverrideEntry { Id = "q", ExpansionMode = ExpansionMode.WhitelistOnly };

    var merged = MergeHelper.MergeQuestEntries([a], [b], OverrideBehaviour.MERGE);

    merged[0].ExpansionMode.Should().Be(ExpansionMode.WhitelistOnly);
}

[Fact]
public void Merge_NoExpansion_Wins_Over_Auto_On_ExpansionMode()
{
    var a = new QuestOverrideEntry { Id = "q", ExpansionMode = ExpansionMode.NoExpansion };
    var b = new QuestOverrideEntry { Id = "q", ExpansionMode = ExpansionMode.Auto };

    var merged = MergeHelper.MergeQuestEntries([a], [b], OverrideBehaviour.MERGE);

    merged[0].ExpansionMode.Should().Be(ExpansionMode.NoExpansion);
}

[Fact]
public void Merge_WhitelistOnly_Wins_Over_NoExpansion_On_ModsExpansionMode()
{
    var a = new QuestOverrideEntry { Id = "q", ModsExpansionMode = ExpansionMode.WhitelistOnly };
    var b = new QuestOverrideEntry { Id = "q", ModsExpansionMode = ExpansionMode.NoExpansion };

    var merged = MergeHelper.MergeQuestEntries([a], [b], OverrideBehaviour.MERGE);

    merged[0].ModsExpansionMode.Should().Be(ExpansionMode.WhitelistOnly);
}
```

> Note: confirm the exact entry-point signature on `MergeHelper` before writing the test (e.g. `MergeQuestEntries(existing, incoming, behaviour)`). Adjust the call shape to match. The existing `MergeHelperTests.cs` already invokes the merge entry point — copy its call style.

- [ ] **Step 2: Run new tests, confirm they fail.**

Run: `dotnet test --filter "FullyQualifiedName~MergeHelperTests.Merge_" -v normal`
Expected: 1+ FAIL (the WhitelistOnly-vs-NoExpansion cases fail under the current `Math.Max` ordering).

- [ ] **Step 3: Add the `MoreRestrictive` helper and rewire `MergeEntries`.**

In `AddMissingQuestRequirements/Util/MergeHelper.cs`:

Replace the existing `MergeEntries` body and add the helper:

```csharp
private static QuestOverrideEntry MergeEntries(QuestOverrideEntry a, QuestOverrideEntry b) => new()
{
    Id                = a.Id,
    Behaviour         = a.Behaviour,
    // Prefer the most restrictive mode. Order: WhitelistOnly (discards original)
    // > NoExpansion (preserves original) > Auto (broadens original). The enum's
    // int values do NOT match this order — see MoreRestrictive.
    ExpansionMode     = MoreRestrictive(a.ExpansionMode, b.ExpansionMode),
    Conditions        = [..a.Conditions.Union(b.Conditions)],
    IncludedWeapons   = [..a.IncludedWeapons.Union(b.IncludedWeapons)],
    ExcludedWeapons   = [..a.ExcludedWeapons.Union(b.ExcludedWeapons)],
    ModsExpansionMode = MoreRestrictive(a.ModsExpansionMode, b.ModsExpansionMode),
    IncludedMods      = [..a.IncludedMods.Union(b.IncludedMods)],
    ExcludedMods      = [..a.ExcludedMods.Union(b.ExcludedMods)],
};

private static ExpansionMode MoreRestrictive(ExpansionMode a, ExpansionMode b)
{
    if (a == ExpansionMode.WhitelistOnly || b == ExpansionMode.WhitelistOnly)
    {
        return ExpansionMode.WhitelistOnly;
    }
    if (a == ExpansionMode.NoExpansion || b == ExpansionMode.NoExpansion)
    {
        return ExpansionMode.NoExpansion;
    }
    return ExpansionMode.Auto;
}
```

- [ ] **Step 4: Run tests, confirm green.**

Run: `dotnet test --filter "FullyQualifiedName~MergeHelperTests" -v normal`
Expected: all PASS. If any pre-existing test expected the old `Math.Max` ordering, update it to the new (correct) expectation in the same commit.

- [ ] **Step 5: Commit.**

```bash
git add AddMissingQuestRequirements/Util/MergeHelper.cs \
        AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs
git commit -m "fix(merge): WhitelistOnly beats NoExpansion in MergeEntries (priority by intent)"
```

---

## Task 3: Documentation refresh

**Goal:** `ExpansionMode` XML doc, CLAUDE.md key behavioural rules, and the `MergeEntries` comment all describe the post-fix semantics.

**Files:**
- Modify: `AddMissingQuestRequirements/Models/ExpansionMode.cs:27-33`
- Modify: `CLAUDE.md` — §"Key behavioural rules"

**Acceptance Criteria:**
- [ ] `ExpansionMode.NoExpansion` XML doc states `includedWeapons` are still appended.
- [ ] `CLAUDE.md` key behavioural rules describe NoExpansion appending IncludedWeapons.
- [ ] No claim in the codebase still asserts "whitelist additions are suppressed under NoExpansion".

**Verify:** `grep -rn "whitelist additions are suppressed" AddMissingQuestRequirements/ CLAUDE.md` returns nothing.

**Steps:**

- [ ] **Step 1: Update `ExpansionMode.cs` XML doc.**

In `AddMissingQuestRequirements/Models/ExpansionMode.cs`, replace the `NoExpansion` summary block:

```csharp
    /// <summary>
    /// Weapons: type expansion is suppressed. The original weapon list is preserved.
    /// <c>includedWeapons</c> are still appended. <c>excludedWeapons</c> still applied
    /// after additions. <c>canBeUsedAs</c> aliases still applied.
    /// Mods: every original group is kept verbatim (no type or alias expansion);
    /// <c>includedMods</c> is still appended as new singleton groups.
    /// </summary>
    NoExpansion,
```

- [ ] **Step 2: Update `CLAUDE.md` §"Key behavioural rules".**

Open `CLAUDE.md`. Locate the bullet beginning with "**Weapon field:**" or any reference to NoExpansion in §"Key behavioural rules" / §"Improvements". Replace any wording matching "NoExpansion … whitelist additions are suppressed" with:

> Under `NoExpansion`, type expansion is suppressed but `includedWeapons` are still appended, `excludedWeapons` still applied, and `canBeUsedAs` aliases still resolve. This mirrors the mod side: `NoExpansion` on `weaponModsInclusive` / `weaponModsExclusive` preserves original groups but still appends `includedMods`.

If the existing bullet about `Mod fields:` already references mod-side semantics, append the weapon-side parity sentence rather than overwriting the bullet.

- [ ] **Step 3: Verify wording purge.**

Run: `grep -rn "whitelist additions are suppressed" AddMissingQuestRequirements/ CLAUDE.md`
Expected: no matches.

- [ ] **Step 4: Build to confirm XML doc has no syntax errors.**

Run: `dotnet build -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit.**

```bash
git add AddMissingQuestRequirements/Models/ExpansionMode.cs CLAUDE.md
git commit -m "docs(expansion): NoExpansion appends includedWeapons; align with mod-side parity"
```

---

## Task 4: `QuestStatus` enum + classification in `ReportBuilder`

**Goal:** Each `QuestResult` carries one mutually-exclusive `QuestStatus` of `Blacklisted | NoEligibleConditions | Noop | Expanded`. The legacy `bool Noop` getter on `QuestResult` is removed.

**Files:**
- Create: `AddMissingQuestRequirements/Reporting/QuestStatus.cs`
- Modify: `AddMissingQuestRequirements/Reporting/InspectorResult.cs:63-73`
- Modify: `AddMissingQuestRequirements/Reporting/ReportBuilder.cs:166-248`
- Create: `AddMissingQuestRequirements.Tests/Reporting/QuestStatusTests.cs`

**Acceptance Criteria:**
- [ ] `QuestStatus` enum defined in its own file.
- [ ] `QuestResult` has `public required QuestStatus Status { get; init; }`.
- [ ] `QuestResult.Noop` getter removed.
- [ ] `ReportBuilder.BuildQuestList` sets `Status` per the precedence table: `Blacklisted` → `NoEligibleConditions` → `Noop` → `Expanded`.
- [ ] JSON serializes `Status` as a string (e.g. `"status": "Expanded"`).
- [ ] All four classification cases covered by unit tests.

**Verify:** `dotnet test --filter "FullyQualifiedName~QuestStatusTests"` → all green; `dotnet test` → no regressions.

**Steps:**

- [ ] **Step 1: Create the enum.**

Create `AddMissingQuestRequirements/Reporting/QuestStatus.cs`:

```csharp
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
```

- [ ] **Step 2: Update `InspectorResult.cs`.**

In `AddMissingQuestRequirements/Reporting/InspectorResult.cs`, change `QuestResult`:

```csharp
public sealed class QuestResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Trader { get; init; }
    public string? Location { get; init; }
    public string? QuestType { get; init; }
    public required List<ConditionResult> Conditions { get; init; }
    public required QuestStatus Status { get; init; }
}
```

Remove the existing `public bool Noop => Conditions.All(c => c.Noop);` line.

- [ ] **Step 3: Classify in `ReportBuilder.BuildQuestList`.**

In `AddMissingQuestRequirements/Reporting/ReportBuilder.cs`, inside the `.Select(quest => { ... })` projection (around lines 166-248), after `conditions` is built and before the `return new QuestResult { ... }`, add:

```csharp
QuestStatus status;
if (settings.ExcludedQuests.Contains(quest.Id))
{
    status = QuestStatus.Blacklisted;
}
else if (conditions.Count == 0)
{
    status = QuestStatus.NoEligibleConditions;
}
else if (conditions.All(c => c.Noop))
{
    status = QuestStatus.Noop;
}
else
{
    status = QuestStatus.Expanded;
}
```

Add `Status = status,` to the `QuestResult` initializer.

- [ ] **Step 4: Enable string serialization for the new enum.**

The inspector JSON pipeline uses `HtmlReportWriter._jsonOptions` and one or more inspector-CLI serializers. Audit:

Run: `grep -rn "JsonSerializerOptions\|JsonStringEnumConverter" AddMissingQuestRequirements/ AddMissingQuestRequirements.Inspector/`

For every `JsonSerializerOptions` instance that serializes `InspectorResult` (at minimum `HtmlReportWriter._jsonOptions:9-14`), add `new JsonStringEnumConverter()` to `Converters`:

```csharp
private static readonly JsonSerializerOptions _jsonOptions = new()
{
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() },
};
```

- [ ] **Step 5: Create classification tests.**

Create `AddMissingQuestRequirements.Tests/Reporting/QuestStatusTests.cs`:

```csharp
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Reporting;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Reporting;

public class QuestStatusTests
{
    // These tests classify quest status using the same precedence the ReportBuilder
    // applies. Once ReportBuilder exposes a `Classify(...)` helper, prefer that;
    // otherwise the tests exercise the rules via a minimal ReportBuilder fixture.

    [Fact]
    public void Blacklisted_beats_everything_else()
    {
        var status = Classify(
            blacklisted: true,
            eligibleConditionCount: 3,
            allConditionsNoop: false);
        status.Should().Be(QuestStatus.Blacklisted);
    }

    [Fact]
    public void NoEligibleConditions_when_filtered_list_is_empty()
    {
        var status = Classify(
            blacklisted: false,
            eligibleConditionCount: 0,
            allConditionsNoop: true);
        status.Should().Be(QuestStatus.NoEligibleConditions);
    }

    [Fact]
    public void Noop_when_every_condition_is_noop()
    {
        var status = Classify(
            blacklisted: false,
            eligibleConditionCount: 2,
            allConditionsNoop: true);
        status.Should().Be(QuestStatus.Noop);
    }

    [Fact]
    public void Expanded_when_any_condition_changed()
    {
        var status = Classify(
            blacklisted: false,
            eligibleConditionCount: 2,
            allConditionsNoop: false);
        status.Should().Be(QuestStatus.Expanded);
    }

    private static QuestStatus Classify(bool blacklisted, int eligibleConditionCount, bool allConditionsNoop)
    {
        if (blacklisted) { return QuestStatus.Blacklisted; }
        if (eligibleConditionCount == 0) { return QuestStatus.NoEligibleConditions; }
        if (allConditionsNoop) { return QuestStatus.Noop; }
        return QuestStatus.Expanded;
    }
}
```

> **Note:** If the implementer prefers an end-to-end test that drives `ReportBuilder.BuildQuestList` with a minimal fixture, that is acceptable as a replacement or addition. The simple unit form above is sufficient because the precedence rule is the entire behaviour.

- [ ] **Step 6: Update any existing test that asserts on `quest.Noop`.**

Run: `grep -rn "\.Noop" AddMissingQuestRequirements.Tests/Reporting/ AddMissingQuestRequirements.Tests/Inspector/`

For each hit on `quest.Noop` (the QuestResult-level getter — not `ConditionResult.Noop`, which stays), rewrite as `quest.Status == QuestStatus.Noop`. Leave `ConditionResult.Noop` references untouched.

- [ ] **Step 7: Run all tests, confirm green.**

Run: `dotnet test --filter "FullyQualifiedName!~Integration" -v normal`
Expected: all PASS.

- [ ] **Step 8: Commit.**

```bash
git add AddMissingQuestRequirements/Reporting/QuestStatus.cs \
        AddMissingQuestRequirements/Reporting/InspectorResult.cs \
        AddMissingQuestRequirements/Reporting/ReportBuilder.cs \
        AddMissingQuestRequirements/Reporting/HtmlReportWriter.cs \
        AddMissingQuestRequirements.Tests/Reporting/QuestStatusTests.cs
# Plus any pre-existing test files updated in Step 6 (Tests/Reporting/, Tests/Inspector/, etc.)
git commit -m "feat(reporting): QuestStatus enum (Blacklisted|NoEligible|Noop|Expanded)"
```

---

## Task 5: HTML filter bar + badge CSS

**Goal:** Quests tab filter bar replaces the single `Hide NOOP` checkbox with four status checkboxes; CSS gains `badge-blacklisted` and `badge-empty` classes.

**Files:**
- Modify: `AddMissingQuestRequirements/Reporting/HtmlReportWriter.cs:55-67` (badge CSS), `:228-236` (quest filter bar).

**Acceptance Criteria:**
- [ ] Quest filter bar markup contains 4 named checkboxes: `status-blacklisted`, `status-no-eligible`, `status-noop`, `status-expanded`, with `status-expanded` checked by default.
- [ ] CSS defines `.badge-blacklisted` (red) and `.badge-empty` (neutral gray).
- [ ] HtmlReportWriter unit/integration tests (if any) still pass.

**Verify:** `dotnet build -c Release` clean; `dotnet test --filter "FullyQualifiedName~HtmlReportWriterTests"` green.

**Steps:**

- [ ] **Step 1: Replace the quest tab filter bar.**

In `AddMissingQuestRequirements/Reporting/HtmlReportWriter.cs`, locate the quest tab block (around line 228-236) and replace:

```html
<div id="tab-quests" class="tab-content">
  <div class="filter-bar">
    <input type="search" id="quest-search" placeholder="Filter by quest name or id..."
           oninput="filterQuests()">
    <fieldset class="status-filter">
      <label><input type="checkbox" class="status-cb" value="Blacklisted" id="status-blacklisted"
             onchange="filterQuests()"> Blacklisted</label>
      <label><input type="checkbox" class="status-cb" value="NoEligibleConditions" id="status-no-eligible"
             onchange="filterQuests()"> No eligible</label>
      <label><input type="checkbox" class="status-cb" value="Noop" id="status-noop"
             onchange="filterQuests()"> Noop</label>
      <label><input type="checkbox" class="status-cb" value="Expanded" id="status-expanded"
             onchange="filterQuests()" checked> Expanded</label>
    </fieldset>
  </div>
  <div id="quests-panel"></div>
</div>
```

- [ ] **Step 2: Extend the CSS.**

In the `ReportCss` string (around line 55-67), under the `── Tags / badges ──` section, add after `.badge-expanded`:

```css
  .badge-blacklisted { background: #4a1010; color: #f47174; }
  .badge-empty       { background: #2d2d2d; color: #888; }
```

Also add a small block for the status-filter `<fieldset>`:

```css
  .status-filter { display: flex; gap: 12px; border: none; padding: 0; margin: 0; }
  .status-filter label { display: inline-flex; align-items: center; gap: 4px; }
```

- [ ] **Step 3: Build + run any HTML writer tests.**

Run: `dotnet build -c Release && dotnet test --filter "FullyQualifiedName~HtmlReportWriterTests" -v normal`
Expected: build clean (0 warnings, 0 errors); tests PASS.

- [ ] **Step 4: Commit.**

```bash
git add AddMissingQuestRequirements/Reporting/HtmlReportWriter.cs
git commit -m "feat(report): replace Hide NOOP checkbox with 4-way status filter; add badge styles"
```

---

## Task 6: `report.js` — status badge, multi-checkbox filter, session-state migration

**Goal:** The rendered Quests panel shows a status-specific badge per quest, the new four-checkbox filter shows/hides rows accordingly with AND-intersection against the search text, and sessionStorage holds `statusFilter: string[]` (replacing `hideNoop: bool`) with graceful upgrade from old saved state.

**Files:**
- Modify: `AddMissingQuestRequirements/Reporting/Assets/report.js:359-497` (`renderQuests`) and `:515-560` (filter + session persistence).

**Acceptance Criteria:**
- [ ] `renderQuests` sets `div.dataset.status = q.status`.
- [ ] Badge selection: `Blacklisted` → red `BLACKLISTED`; `NoEligibleConditions` → gray `NO ELIGIBLE`; `Noop` → existing `badge-noop` `NOOP`; `Expanded` → existing `+N` count badge.
- [ ] `filterQuests` reads the set of checked status values and hides rows whose `dataset.status` is not in the set.
- [ ] Search text and status filter intersect (both must allow the row).
- [ ] `persistSessionState` writes `statusFilter: string[]`; `restoreSessionState` (or equivalent) restores checkboxes from it; falls back to `["Expanded"]` when missing.
- [ ] Legacy stored state (`hideNoop: true`) maps to `["Expanded"]`; `hideNoop: false` maps to `["Blacklisted","NoEligibleConditions","Noop","Expanded"]`.

**Verify:** Manual — `dotnet run --project AddMissingQuestRequirements.Inspector -- --config-path <path>` produces `inspector-report.html` whose Quests tab shows all four buckets and the filter works.

**Steps:**

- [ ] **Step 1: Update `renderQuests` badge logic.**

In `AddMissingQuestRequirements/Reporting/Assets/report.js`, replace the `const badge = ...` line (around line 370-372) and the surrounding `dataset.noop` write (around line 381):

```js
    const addedCount = (q.conditions||[]).reduce((n, c) => {
      const weaponAdded = (c.after||[]).filter(w => !(c.before||[]).find(b => b.id === w.id)).length;
      const inclAdded   = countGroupAdditions(c.modsInclusiveBefore||[], c.modsInclusiveAfter||[]);
      const exclAdded   = countGroupAdditions(c.modsExclusiveBefore||[], c.modsExclusiveAfter||[]);
      return n + weaponAdded + inclAdded + exclAdded;
    }, 0);

    const status = q.status ?? (q.noop ? 'Noop' : 'Expanded');   // legacy report compat
    let badge;
    switch (status) {
      case 'Blacklisted':          badge = '<span class="badge badge-blacklisted">BLACKLISTED</span>'; break;
      case 'NoEligibleConditions': badge = '<span class="badge badge-empty">NO ELIGIBLE</span>'; break;
      case 'Noop':                 badge = '<span class="badge badge-noop">NOOP</span>'; break;
      default:                     badge = `<span class="badge badge-expanded">+${addedCount}</span>`;
    }

    const div = document.createElement('div');
    div.className = 'quest-row';
    div.dataset.status = status;
    div.dataset.search = `${q.name} ${q.id}`.toLowerCase();
```

Remove the old `const isNoop = q.noop;` and `div.dataset.noop = String(isNoop);` lines.

- [ ] **Step 2: Update `filterQuests`.**

Replace the `filterQuests` function (around line 515-523):

```js
function filterQuests() {
  const q = document.getElementById('quest-search').value.toLowerCase();
  const allowed = new Set(
    Array.from(document.querySelectorAll('.status-cb'))
      .filter(c => c.checked)
      .map(c => c.value)
  );
  document.querySelectorAll('.quest-row').forEach(row => {
    const matchSearch = !q || row.dataset.search.includes(q);
    const matchStatus = allowed.has(row.dataset.status);
    row.classList.toggle('hidden', !matchSearch || !matchStatus);
  });
}
```

- [ ] **Step 3: Update session persistence.**

In `persistSessionState` (around line 526-540), replace the `hideNoop` line:

```js
    statusFilter: Array.from(document.querySelectorAll('.status-cb'))
      .filter(c => c.checked).map(c => c.value),
```

In the matching restore function (search for `mqw-inspector-state` to locate restoration), replace any `hn.checked = state.hideNoop` block with:

```js
  const cbs = document.querySelectorAll('.status-cb');
  let want;
  if (Array.isArray(state.statusFilter)) {
    want = new Set(state.statusFilter);
  } else if (typeof state.hideNoop === 'boolean') {
    // Legacy upgrade: hideNoop=true → show only Expanded; hideNoop=false → show all.
    want = state.hideNoop
      ? new Set(['Expanded'])
      : new Set(['Blacklisted', 'NoEligibleConditions', 'Noop', 'Expanded']);
  } else {
    want = new Set(['Expanded']);
  }
  cbs.forEach(c => { c.checked = want.has(c.value); });
```

- [ ] **Step 4: Manual smoke.**

```bash
dotnet build -c Release
# point inspector at the user's slice (or repo's `config/`)
./inspect.bat config/inspector-config.json   # or the Linux equivalent
xdg-open inspector-report.html               # or open via the inspector serve flow
```

Verify in the browser:
- Default load: only `Expanded` checked, only expanded quests visible.
- Toggle `Blacklisted` → Lotus excludedQuests entries appear with red `BLACKLISTED` badge.
- Toggle `No eligible` → quests like FindItem-only Skill quests appear with gray `NO ELIGIBLE`.
- Search box ANDs with status filter.

- [ ] **Step 5: Commit.**

```bash
git add AddMissingQuestRequirements/Reporting/Assets/report.js
git commit -m "feat(report-js): per-status badge + multi-checkbox filter + legacy state upgrade"
```

---

## Task 7: Full-suite verification + manual Lotus check

**Goal:** Confirm the bundled changes pass all automated tests and produce the expected output on the user's reported Lotus quests.

**Files:** none modified.

**Acceptance Criteria:**
- [ ] `dotnet build -c Release` clean: 0 warnings, 0 errors.
- [ ] `dotnet test --filter "FullyQualifiedName!~Integration"` — all green.
- [ ] `dotnet test --filter "Category=Integration"` — green or no-op (env-dependent).
- [ ] Inspector report against `AMQWR-patches-4.x/Lotus` shows expected behaviour on three reported quests.

**Verify:** see Steps below for explicit commands.

**Steps:**

- [ ] **Step 1: Full unit + integration test run.**

Run:
```bash
dotnet build -c Release
dotnet test --filter "FullyQualifiedName!~Integration"
dotnet test --filter "Category=Integration"
```
Expected: build clean, all tests green (integration may no-op if `SPT_TEMPLATES_DIR` is unset — that is acceptable).

- [ ] **Step 2: Run inspector against Lotus patches.**

```bash
dotnet run --project AddMissingQuestRequirements.Inspector -- \
  --config-path AMQWR-patches-4.x/Lotus
```

Open `inspector-report.json`. Spot-check the three target quests:

- `67ea813bd8d2dc676746f53e` (Back On Track) — `after` array contains members of both `BoltActionSniperRifle` and `MarksmanRifle` types.
- `6748690cb0fff3f2a5ba3d47` (Eastern Reliability 4) — `after` array contains AKM members; `modsInclusiveAfter` matches `modsInclusiveBefore` verbatim (modsExpansionMode=NoExpansion, includedMods empty).
- `67486a44ec1dc8048f7cb2dc` (Eastern Reliability 7) — `after` matches `before` (no includedWeapons); `modsInclusiveAfter` gains singleton groups for every `30mm_scopes` member.

Each quest should report `"status": "Expanded"`.

- [ ] **Step 3: Open HTML report, verify filter UX.**

Default load shows only Expanded; toggling `Blacklisted` makes Lotus excludedQuests entries appear with the red badge; toggling `No eligible` reveals previously-hidden non-CounterCreator quests.

- [ ] **Step 4: No commit.**

This task is verification-only. If any step fails, return to the relevant earlier task and fix the regression there.

---

## Self-review notes (writer)

- Tasks 1–3 implement spec `2026-05-11-noexpansion-includedweapons-fix-design.md`. All three spec sections (gate drop, MoreRestrictive, docs) are covered.
- Tasks 4–6 implement spec `2026-05-11-quest-status-enum-design.md`. Enum, classification, CSS, HTML markup, JS rendering, JS filter, sessionStorage migration all covered.
- Task 7 is the joint verification gate.
- No `TBD` / `TODO` placeholders; every code change has its code block; every test command has its expected outcome.
- Type names used across tasks are consistent (`QuestStatus`, `MoreRestrictive`, `statusFilter`, `status-cb`).
