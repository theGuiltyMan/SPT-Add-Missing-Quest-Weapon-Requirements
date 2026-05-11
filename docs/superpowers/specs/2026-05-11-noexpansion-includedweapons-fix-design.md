# `NoExpansion` drops `includedWeapons` — design

## Problem

User-authored quest overrides of the shape

```jsonc
{ "id": "<questId>", "expansionMode": 2, "includedWeapons": ["AKM"] }
```

silently produce no additions. `expansionMode: 2` is `ExpansionMode.NoExpansion`. The weapon expander gates the include-additions step behind `mode != NoExpansion`:

```csharp
// Pipeline/Weapon/WeaponArrayExpander.cs:83
if (mode != ExpansionMode.NoExpansion && overrideEntry is not null)
{
    foreach (var entry in overrideEntry.IncludedWeapons)
    {
        AddResolved(entry, weapons, seen, categorization);
    }
}
```

So `NoExpansion` runs:

- Step 2 (type expansion) — skipped.
- Step 3 (whitelist additions) — **skipped** by the gate above.
- Step 4 (excludes) — runs.
- Step 5 (`canBeUsedAs` aliases via `GroupExpander.ApplyAliasesAndReattach`) — runs.

Result: the user's whitelist is ignored; only `canBeUsedAs` cross-links fire. This matches the inspector report's "only canBeUsedAs added" symptom on `67ea813bd8d2dc676746f53e` (Lotus: Back On Track), `6748690cb0fff3f2a5ba3d47` (Eastern Reliability 4), and `67486a44ec1dc8048f7cb2dc` (Eastern Reliability 7).

## Why this is the wrong behaviour

1. **Asymmetric with the mods side.** `Pipeline/Attachment/WeaponModsExpander.cs:117-120` already appends `IncludedMods` under `NoExpansion`. The two expanders should treat the two whitelist fields the same way.
2. **Inconsistent with user mental model.** `NoExpansion` reads as "don't broaden by type"; users still expect their explicit whitelist additions to apply. The current behaviour requires `WhitelistOnly`, which *clears* the original list — a different action entirely.
3. **Mod's shipped default config relies on this pattern.** `config/MissingQuestWeapons/QuestOverrides.jsonc:117-134` (and many subsequent entries) ships `{ "expansionMode": 2, "includedWeapons": ["SVD"|"AR-15"|…] }`. Under the current gate every one of these entries is partially neutralised — only `canBeUsedAs` aliases fire, `includedWeapons` is ignored. The shipped default has been broken since initial release (`b1eab0e`); the bug is only now visible because the resolver fix in 2.0.3 lets the override actually reach the expander.
4. **Regression surfaced by 2.0.3.** Commit `1da200e` fixed `MergeHelper.CloneEntry` (no longer drops `ModsExpansionMode` / `IncludedMods` / `ExcludedMods`) and resolver parent-id matching. Pre-2.0.3, many user overrides with `expansionMode: 2` silently failed to resolve, so the expander defaulted to `Auto` and the original weapon list got auto-expanded — masking this gate. After the resolver fix, the override is correctly applied → mode flips to `NoExpansion` → the gate kicks in → output collapses.

Documentation (`Models/ExpansionMode.cs:28-33`, `CLAUDE.md` §Key behavioural rules) currently encodes this gate as intended ("type expansion and whitelist additions are suppressed"). The docs are wrong, not the user.

## Design

### Code change

`Pipeline/Weapon/WeaponArrayExpander.cs:83`. Drop the `mode != ExpansionMode.NoExpansion` gate:

```csharp
if (overrideEntry is not null)
{
    foreach (var entry in overrideEntry.IncludedWeapons)
    {
        AddResolved(entry, weapons, seen, categorization);
    }
}
```

This is the only behavioural change. Steps 2, 4, 5 stay as they are. `WhitelistOnly`'s existing `weapons.Clear()` (line 76-79) runs before this block, so the semantics for `WhitelistOnly` are unchanged.

### Behaviour matrix after the fix

| Mode | Step 2 (type) | Step 3a (clear) | Step 3b (includedWeapons) | Step 4 (excluded) | Step 5 (canBeUsedAs) |
|------|---------------|-----------------|---------------------------|-------------------|----------------------|
| `Auto` | run | skip | run | run | run |
| `WhitelistOnly` | skip (precondition not met) | run | run | run | run |
| `NoExpansion` | skip | skip | **run** (was skip) | run | run |

Mirrors `WeaponModsExpander` after the fix:

| Mode | Original groups | Type expansion | `includedMods` | `excludedMods` |
|------|-----------------|----------------|----------------|----------------|
| `Auto` | partition | run | append | run |
| `WhitelistOnly` | discard | n/a | rebuild from these | run |
| `NoExpansion` | verbatim | skip | append | run |

### Doc updates

- `Models/ExpansionMode.cs:28-33` — rewrite to: *"Weapons: type expansion is suppressed. `includedWeapons` are still appended and `canBeUsedAs` aliases still apply. Mods: every original group is kept verbatim (no type or alias expansion); `includedMods` is still appended as new singleton groups."*
- `CLAUDE.md` §"Key behavioural rules" — same wording fix on the bullet covering `NoExpansion`.
- `Util/MergeHelper.cs:313` comment ("more restrictive (NoExpansion > WhitelistOnly > Auto)") — **wrong**. See "Merge priority fix" below.

### Merge priority fix

`Util/MergeHelper.cs:309-321` uses `Math.Max((int)a.ExpansionMode, (int)b.ExpansionMode)` for both `ExpansionMode` and `ModsExpansionMode` under `MergeEntries`. Enum int values are `Auto=0, WhitelistOnly=1, NoExpansion=2`. True restrictiveness axis on both weapons (post-fix) and mods:

| Rank | Mode | Effect |
|-----:|------|--------|
| Least restrictive | `Auto` | broaden existing list + add includes |
| Middle | `NoExpansion` | preserve existing list + add includes |
| **Most restrictive** | `WhitelistOnly` | discard existing, rebuild from includes only |

So `WhitelistOnly` is strictest but has int value 1; `Math.Max` picks `NoExpansion` (2) over `WhitelistOnly` (1), silently dropping the WhitelistOnly side's discard intent when two mods merge on the same condition set under `OverrideBehaviour.MERGE`.

Fix: replace both `Math.Max` calls with a `MoreRestrictive` helper. Enum int values stay (user-visible in JSONC configs as `"expansionMode": 2` — re-numbering would force a config migration). Helper:

```csharp
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

`MergeEntries`:

```csharp
ExpansionMode     = MoreRestrictive(a.ExpansionMode, b.ExpansionMode),
ModsExpansionMode = MoreRestrictive(a.ModsExpansionMode, b.ModsExpansionMode),
```

Updated comment on the `MergeEntries` method:

> When merging, prefer the most restrictive mode. Order: `WhitelistOnly` (discards original) > `NoExpansion` (preserves original) > `Auto` (broadens original).

### Tests

`AddMissingQuestRequirements.Tests/Pipeline/Weapon/WeaponArrayExpanderTests.cs` — add:

- `NoExpansion_AppendsIncludedWeapons` — single-weapon condition, override `{ expansionMode: NoExpansion, includedWeapons: ["AKM"] }`, expect output to contain every AKM member.
- `NoExpansion_PreservesOriginalWeapons` — same setup; original weapon ID still in output.
- `NoExpansion_AppendsIncludedWeapons_BareId` — `includedWeapons` of a single bare ID, not a type name. ID appears once in output.
- `NoExpansion_ExcludedWeaponsStillApply` — `{ expansionMode: NoExpansion, includedWeapons: ["AKM"], excludedWeapons: ["<one-akm-id>"] }`. Excluded ID is absent.
- `NoExpansion_NoOverride_LeavesListUnchanged` — sanity: `NoExpansion` with no override still no-ops on a 2+ weapon list (regression guard).

Existing `WhitelistOnly_*` and `Auto_*` tests should keep passing unchanged.

`AddMissingQuestRequirements.Tests/Util/MergeHelperTests.cs` — add:

- `Merge_WhitelistOnly_Beats_NoExpansion_OnExpansionMode` — A=WhitelistOnly, B=NoExpansion → result `WhitelistOnly`.
- `Merge_WhitelistOnly_Beats_Auto_OnExpansionMode` — A=Auto, B=WhitelistOnly → result `WhitelistOnly`.
- `Merge_NoExpansion_Beats_Auto_OnExpansionMode` — A=NoExpansion, B=Auto → result `NoExpansion`.
- `Merge_SamePriority_OnModsExpansionMode` — mirror the three cases for the mods field.
- Existing `MergeHelper` tests that assert `Math.Max` behaviour: update to match the new ordering. Failing cases under the old logic indicate a bug being fixed, not a regression.

### Manual verification (inspector)

After build + deploy, run inspector against the user's AMQWR-patches-4.x slice:

- `67ea813bd8d2dc676746f53e` (Lotus: Back On Track) — `expansionMode: 2 includedWeapons: ["BoltActionSniperRifle","MarksmanRifle"]`. Expect `After` to contain members of both types.
- `6748690cb0fff3f2a5ba3d47` (Lotus: Eastern Reliability 4) — `expansionMode: 2 includedWeapons: ["AKM"] modsExpansionMode: 2 includedMods: []`. Expect AKM members appended to weapon list; mods inclusive/exclusive verbatim.
- `67486a44ec1dc8048f7cb2dc` (Lotus: Eastern Reliability 7) — `includedWeapons: [] modsExpansionMode: 2 includedMods: ["30mm_scopes"]`. Expect weapon list verbatim; mods inclusive gains `30mm_scopes` singleton groups.

## Out of scope

- Reporting changes (badges, filters, `QuestStatus`) — separate spec: `2026-05-11-quest-status-enum-design.md`.
- Any change to `Auto` semantics.
- Any change to `WhitelistOnly`.

## Open questions

None.
