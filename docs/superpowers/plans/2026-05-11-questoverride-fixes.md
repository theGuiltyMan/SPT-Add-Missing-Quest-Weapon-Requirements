# QuestOverride Bug Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two `QuestOverrideEntry` bugs that silently neutralize user overrides: missing-field drop in `MergeHelper` and parent-condition-id mismatch in `QuestOverrideResolver`.

**Architecture:** Two small, independent edits in `Util/MergeHelper.cs` and `Pipeline/Quest/QuestOverrideResolver.cs`, each gated on a failing xUnit test added first. No new files, no schema change.

**Tech Stack:** .NET 9, xUnit + FluentAssertions, existing test harness.

---

## Bug context

**Bug 1 — silent field drop.** `MergeHelper.CloneEntry` (`Util/MergeHelper.cs:296-304`) and `MergeEntries` (`:306-315`) copy only `Id`, `Behaviour`, `ExpansionMode`, `Conditions`, `IncludedWeapons`, `ExcludedWeapons`. Three fields on `QuestOverrideEntry` (`Models/QuestOverrideEntry.cs`) — `ModsExpansionMode`, `IncludedMods`, `ExcludedMods` — get reset to defaults the moment an entry hits `MergeQuestEntries` (which `OverrideReader.ApplyQuestOverrides` always calls). Users authoring `modsExpansionMode` / `includedMods` / `excludedMods` see zero effect.

**Bug 2 — parent-id not matched.** `QuestOverrideResolver.Resolve` (`Pipeline/Quest/QuestOverrideResolver.cs:11-35`) tests `entry.Conditions.Contains(conditionId)` only against the sub-condition id (`ConditionNode.Id` = leaf `_id` under `_props.counter.conditions[]`). Users typically copy the outer `CounterCreator` wrapper id (`AvailableForFinish[i]._id`) from quest editors and SPT JSON — the more visible id, also the one keyed in locale (already relied on in `ReportBuilder.cs:346`). Mismatch → entry skipped, no quest-wide fallback fires (fallback only triggers when `Conditions` is empty), `OverrideMatched=false`, mode silently degrades to `Auto`.

## File Structure

- Modify: `AddMissingQuestRequirements/Util/MergeHelper.cs:296-315` — extend `CloneEntry` and `MergeEntries`.
- Modify: `AddMissingQuestRequirements/Pipeline/Quest/QuestOverrideResolver.cs:11-35` — accept `ConditionNode`, match parent id too.
- Modify: `AddMissingQuestRequirements/Pipeline/Quest/QuestPatcher.cs:50` — pass `condition` instead of `condition.Id`.
- Modify: `AddMissingQuestRequirements/Reporting/ReportBuilder.cs:182` — same call-site update.
- Create: `AddMissingQuestRequirements.Tests/Pipeline/Quest/QuestOverrideResolverTests.cs` — new file; resolver had no direct test coverage.
- Modify: `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs` — add tests for the three preserved fields.

---

### Task 1: Preserve `ModsExpansionMode` / `IncludedMods` / `ExcludedMods` through merge

**Goal:** `MergeHelper.CloneEntry` and `MergeEntries` round-trip every public field on `QuestOverrideEntry`.

**Files:**
- Modify: `AddMissingQuestRequirements/Util/MergeHelper.cs:296-315`
- Test: `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs`

**Acceptance Criteria:**
- [ ] New test exercising `MergeQuestEntries` with a single entry carrying non-default `ModsExpansionMode`, `IncludedMods`, `ExcludedMods` recovers those fields verbatim on the result entry.
- [ ] New test exercising `MergeQuestEntries` with `MERGE` behaviour on two entries (same condition set) unions `IncludedMods` and `ExcludedMods` and picks the more restrictive `ModsExpansionMode` (`Math.Max`-by-enum-int, same rule as `ExpansionMode`).
- [ ] All existing `MergeHelperTests` pass unchanged.

**Verify:** `dotnet test --filter "FullyQualifiedName~MergeHelperTests"` → all green.

**Steps:**

- [ ] **Step 1: Write failing tests**

Append to `AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs` (inside the existing test class, namespace already present):

```csharp
[Fact]
public void MergeQuestEntries_preserves_mods_fields_on_first_insert()
{
    var incoming = new List<QuestOverrideEntry>
    {
        new()
        {
            Id = "q1",
            Conditions = ["c1"],
            ModsExpansionMode = ExpansionMode.NoExpansion,
            IncludedMods = ["mod-a", "mod-b"],
            ExcludedMods = ["mod-x"],
        },
    };

    var result = MergeHelper.MergeQuestEntries(
        new Dictionary<string, List<QuestOverrideEntry>>(),
        incoming,
        OverrideBehaviour.MERGE);

    var entry = result["q1"].Single();
    entry.ModsExpansionMode.Should().Be(ExpansionMode.NoExpansion);
    entry.IncludedMods.Should().BeEquivalentTo(["mod-a", "mod-b"]);
    entry.ExcludedMods.Should().BeEquivalentTo(["mod-x"]);
}

[Fact]
public void MergeQuestEntries_MERGE_unions_mod_lists_and_takes_max_mods_mode()
{
    var existing = new Dictionary<string, List<QuestOverrideEntry>>
    {
        ["q1"] =
        [
            new()
            {
                Id = "q1",
                Conditions = ["c1"],
                ModsExpansionMode = ExpansionMode.WhitelistOnly,
                IncludedMods = ["mod-a"],
                ExcludedMods = ["mod-x"],
            },
        ],
    };
    var incoming = new List<QuestOverrideEntry>
    {
        new()
        {
            Id = "q1",
            Conditions = ["c1"],
            ModsExpansionMode = ExpansionMode.NoExpansion,
            IncludedMods = ["mod-b"],
            ExcludedMods = ["mod-y"],
        },
    };

    var result = MergeHelper.MergeQuestEntries(existing, incoming, OverrideBehaviour.MERGE);

    var entry = result["q1"].Single();
    entry.ModsExpansionMode.Should().Be(ExpansionMode.NoExpansion);
    entry.IncludedMods.Should().BeEquivalentTo(["mod-a", "mod-b"]);
    entry.ExcludedMods.Should().BeEquivalentTo(["mod-x", "mod-y"]);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~MergeQuestEntries_preserves_mods_fields_on_first_insert|FullyQualifiedName~MergeQuestEntries_MERGE_unions_mod_lists_and_takes_max_mods_mode" --nologo -v minimal`
Expected: 2 failed (`ModsExpansionMode` is `Auto`, `IncludedMods` / `ExcludedMods` empty).

- [ ] **Step 3: Patch `CloneEntry` and `MergeEntries`**

In `AddMissingQuestRequirements/Util/MergeHelper.cs`, replace lines 296-315 with:

```csharp
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
```

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~MergeHelperTests" --nologo -v minimal`
Expected: all MergeHelperTests pass.

- [ ] **Step 5: Commit**

```bash
git add AddMissingQuestRequirements/Util/MergeHelper.cs \
        AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs
git commit -m "fix(overrides): preserve mods fields through MergeHelper clone/merge"
```

---

### Task 2: Resolver matches outer CounterCreator id as well as sub id

**Goal:** `QuestOverrideResolver` matches `entry.Conditions` against either `ConditionNode.Id` or `ConditionNode.ParentConditionId`, so overrides authored against the outer CounterCreator id (the id users see in quest editors and locale keys) apply.

**Files:**
- Modify: `AddMissingQuestRequirements/Pipeline/Quest/QuestOverrideResolver.cs:11-35`
- Modify: `AddMissingQuestRequirements/Pipeline/Quest/QuestPatcher.cs:50`
- Modify: `AddMissingQuestRequirements/Reporting/ReportBuilder.cs:182`
- Test: `AddMissingQuestRequirements.Tests/Pipeline/Quest/QuestOverrideResolverTests.cs` (new file)

**Acceptance Criteria:**
- [ ] Resolver returns the entry when `entry.Conditions` contains the sub-condition id (existing behaviour preserved).
- [ ] Resolver returns the entry when `entry.Conditions` contains the parent CounterCreator id only.
- [ ] Resolver still prefers a specific-condition match over a generic (empty `Conditions`) match.
- [ ] Resolver returns null when neither id matches and no generic entry exists.
- [ ] `QuestPatcher` and `ReportBuilder` compile and pass their existing tests after the signature change.

**Verify:** `dotnet test --filter "FullyQualifiedName!~Integration" --nologo -v minimal` → all green.

**Steps:**

- [ ] **Step 1: Write failing resolver tests**

Create `AddMissingQuestRequirements.Tests/Pipeline/Quest/QuestOverrideResolverTests.cs`:

```csharp
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using FluentAssertions;
using Xunit;

namespace AddMissingQuestRequirements.Tests.Pipeline.Quest;

public sealed class QuestOverrideResolverTests
{
    private static OverriddenSettings BuildSettings(params QuestOverrideEntry[] entries)
    {
        var dict = new Dictionary<string, List<QuestOverrideEntry>>();
        foreach (var e in entries)
        {
            if (!dict.TryGetValue(e.Id, out var list))
            {
                list = [];
                dict[e.Id] = list;
            }
            list.Add(e);
        }
        return new OverriddenSettings
        {
            QuestOverrides = dict,
        };
    }

    private static ConditionNode Cond(string id, string parentId) => new()
    {
        Id = id,
        ParentConditionId = parentId,
        ConditionType = "CounterCreator",
    };

    [Fact]
    public void Resolve_returns_null_when_quest_unknown()
    {
        var settings = BuildSettings();
        QuestOverrideResolver.Resolve(settings, "q-missing", Cond("sub", "outer")).Should().BeNull();
    }

    [Fact]
    public void Resolve_matches_sub_condition_id()
    {
        var entry = new QuestOverrideEntry { Id = "q1", Conditions = ["sub-1"] };
        var settings = BuildSettings(entry);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeSameAs(entry);
    }

    [Fact]
    public void Resolve_matches_parent_condition_id()
    {
        var entry = new QuestOverrideEntry { Id = "q1", Conditions = ["outer-1"] };
        var settings = BuildSettings(entry);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeSameAs(entry);
    }

    [Fact]
    public void Resolve_prefers_specific_match_over_generic()
    {
        var generic  = new QuestOverrideEntry { Id = "q1", Conditions = [] };
        var specific = new QuestOverrideEntry { Id = "q1", Conditions = ["outer-1"] };
        var settings = BuildSettings(generic, specific);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeSameAs(specific);
    }

    [Fact]
    public void Resolve_falls_back_to_generic_when_no_specific_match()
    {
        var generic  = new QuestOverrideEntry { Id = "q1", Conditions = [] };
        var specific = new QuestOverrideEntry { Id = "q1", Conditions = ["other-id"] };
        var settings = BuildSettings(specific, generic);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeSameAs(generic);
    }

    [Fact]
    public void Resolve_returns_null_when_specific_misses_and_no_generic()
    {
        var entry = new QuestOverrideEntry { Id = "q1", Conditions = ["other-id"] };
        var settings = BuildSettings(entry);

        QuestOverrideResolver.Resolve(settings, "q1", Cond("sub-1", "outer-1"))
            .Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test --filter "FullyQualifiedName~QuestOverrideResolverTests" --nologo -v minimal`
Expected: build failure — `Resolve(...)` overload taking `ConditionNode` does not exist yet.

- [ ] **Step 3: Update resolver signature + matching**

Replace the body of `AddMissingQuestRequirements/Pipeline/Quest/QuestOverrideResolver.cs` with:

```csharp
using AddMissingQuestRequirements.Models;

namespace AddMissingQuestRequirements.Pipeline.Quest;

/// <summary>
/// Resolves the <see cref="QuestOverrideEntry"/> applicable to a given quest condition.
/// Condition-specific matches win over generic (quest-wide) entries. A specific match
/// accepts either the sub-condition id (<see cref="ConditionNode.Id"/>) or the outer
/// CounterCreator wrapper id (<see cref="ConditionNode.ParentConditionId"/>); users
/// commonly author overrides against the outer id because that is what quest editors
/// and the locale keys surface.
/// </summary>
public static class QuestOverrideResolver
{
    public static QuestOverrideEntry? Resolve(
        OverriddenSettings settings,
        string questId,
        ConditionNode condition)
    {
        if (!settings.QuestOverrides.TryGetValue(questId, out var entries))
        {
            return null;
        }

        foreach (var entry in entries)
        {
            if (entry.Conditions.Count == 0)
            {
                continue;
            }
            if (entry.Conditions.Contains(condition.Id)
                || (!string.IsNullOrEmpty(condition.ParentConditionId)
                    && entry.Conditions.Contains(condition.ParentConditionId)))
            {
                return entry;
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Conditions.Count == 0)
            {
                return entry;
            }
        }

        return null;
    }
}
```

- [ ] **Step 4: Update the two call sites**

In `AddMissingQuestRequirements/Pipeline/Quest/QuestPatcher.cs:50`, change:

```csharp
var overrideEntry = QuestOverrideResolver.Resolve(settings, questId, condition.Id);
```

to:

```csharp
var overrideEntry = QuestOverrideResolver.Resolve(settings, questId, condition);
```

In `AddMissingQuestRequirements/Reporting/ReportBuilder.cs:182`, change:

```csharp
var overrideEntry = QuestOverrideResolver.Resolve(settings, quest.Id, c.Id);
```

to:

```csharp
var overrideEntry = QuestOverrideResolver.Resolve(settings, quest.Id, c);
```

- [ ] **Step 5: Run full unit suite**

Run: `dotnet test --filter "FullyQualifiedName!~Integration" --nologo -v minimal`
Expected: all unit tests pass, including the new `QuestOverrideResolverTests`.

- [ ] **Step 6: Commit**

```bash
git add AddMissingQuestRequirements/Pipeline/Quest/QuestOverrideResolver.cs \
        AddMissingQuestRequirements/Pipeline/Quest/QuestPatcher.cs \
        AddMissingQuestRequirements/Reporting/ReportBuilder.cs \
        AddMissingQuestRequirements.Tests/Pipeline/Quest/QuestOverrideResolverTests.cs
git commit -m "fix(overrides): resolver matches outer CounterCreator id as well as sub id"
```

---

## Self-review

- Spec coverage: bug 1 → Task 1; bug 2 → Task 2. Both have failing-test-first steps, exact code, exact verify commands, and a commit step.
- No placeholders.
- Types: `QuestOverrideResolver.Resolve` new signature `(OverriddenSettings, string, ConditionNode)` consistent across tests and both call sites. Field names (`ModsExpansionMode`, `IncludedMods`, `ExcludedMods`) match `Models/QuestOverrideEntry.cs:38,49,63`.
