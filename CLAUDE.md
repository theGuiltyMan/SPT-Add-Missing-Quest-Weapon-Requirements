# CLAUDE.md

Guidance for AI coding assistants (Claude Code, GitHub Copilot, etc.) working in this repository. Human contributors should read `README.md` first, then `CONTRIBUTING.md`.

## Project Overview

**AddMissingQuestRequirements** is a C# server-side mod for SPT 4.x. At server startup it scans every installed mod for a `MissingQuestWeapons/` config folder, categorizes every weapon and attachment in the SPT item database by type, and rewrites quest conditions that list specific weapon IDs so they also accept every modded weapon of the same type. The same pipeline runs via a standalone **Inspector** CLI for offline iteration against an exported DB slice.

Ancestry: a rewrite of an upstream TypeScript mod of the same name. The C# version inherits the TS design's behavioural rules (see §Key Behavioural Rules) and extends them (see §Improvements).

## External references

These live outside this repo; use them when you need ground truth:

- **SPT server source + example mods**: a local checkout of `spt-server-csharp`. The most relevant examples for the DI / IOnLoad hook are `14AfterDBLoadHook`, `5ReadCustomJsonConfig`, and `11RegisterClassesInDI`.
- **SPT database templates**: `spt-server-csharp/Libraries/SPTarkov.Server.Assets/SPT_Data/database/templates/`.
  - `items.json` — full item database (weapon `_parent` chains, `_props` fields including `BoltAction`, `ammoCaliber`, `muzzleModType`).
  - `quests.json` — quest definitions including `CounterCreator` condition structures.
- **Exported DB slice**: `SptDbExporter/export/` (items.json, quests.json, locale_en.json). Not committed. Produced by running `SptDbExporter` against a real SPT install. Used by the Inspector and the integration tests when `SPT_TEMPLATES_DIR` is unset.

## Build

.NET 9 class library. Standard workflow:

```bash
dotnet build -c Release
dotnet test --filter "FullyQualifiedName!~Integration"   # unit tests (fast, hermetic)
dotnet test --filter "Category=Integration"              # integration smoke
```

Integration tests look for the SPT slice in this order: `SPT_TEMPLATES_DIR` + `SPT_LOCALE_EN_PATH` env vars → walk upward from the test binary looking for `SptDbExporter/export/` → short-circuit to a no-op pass. See `AddMissingQuestRequirements.Inspector/SliceLoader.cs` for the resolution logic.

Auto-deploy on Windows: copy `local.props.template` → `local.props`, set `SptRoot`. The `DeployToSptMods` target in each csproj is gated on `$([MSBuild]::IsOSPlatform(Windows))` so it no-ops on Linux / CI.

## Inspector

Two modes, one binary:

- **One-shot CLI** (`tools/inspect.bat [inspector-config.json]` on Windows, or `dotnet run --project AddMissingQuestRequirements.Inspector` anywhere) — run the pipeline once, write `inspector-report.html` + `inspector-report.json`. Self-contained.
- **Serve** (`tools/inspect-serve.bat [--port N] [--no-open]` on Windows, or `dotnet run --project AddMissingQuestRequirements.Inspector -- serve` anywhere) — local HTTP server at `http://localhost:5173`, opens the browser, watches `MainConfigPath` for saves. Each `*.jsonc` save reruns the pipeline and pushes the new state over SSE; broken JSONC shows a toast and keeps the last good state. Read-only to disk.

## SPT integration patterns

Every SPT C# mod follows these conventions.

**Mod metadata**: one `record ModMetadata : AbstractModMetadata` per project, with `ModGuid`, `Name`, `Author`, `Version`, and `SptVersion` range.

**Entry points**: classes implement `IOnLoad` and carry `[Injectable(TypePriority = ...)]`. The DI container (Microsoft.Extensions.DI via `SPTarkov.DI`) resolves and calls them. Constructor params are injected.

**Load order anchors** (from `OnLoadOrder`):
- `PostDBModLoader + 1` — the database is loaded but SPT hasn't finished processing it. This is the hook this mod uses (equivalent to the TS `postDBLoad`).
- `PostSptModLoader + 1` — after SPT's own processing.

**Injected services** used by this mod:
- `DatabaseServer` → `GetTables().Templates.Items` (item DB), `GetTables().Templates.Quests` (quest DB).
- `ISptLogger<T>` → structured logging. Wrapped by `SptModLogger<T>` so the pipeline can stay on the domain-only `IModLogger` surface.
- `LocaleService` → item + quest locale lookup (names and descriptions).
- `ModHelper` → `GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly())` for the mod's install path.

JSONC config files are parsed through `Util/JsoncReader.cs` + `Config/ConfigLoader.cs`, which wrap `System.Text.Json` with `JsonCommentHandling.Skip` + `AllowTrailingCommas` and then run the version-migration chain. `ModHelper.GetJsonDataFromFile` is NOT JSONC-safe.

## Architecture

### Three-phase pipeline

Runs once during `IOnLoad.OnLoad()`. See `AddMissingQuestRequirements/Spt/AddMissingQuestRequirementsLoader.cs`.

1. **OverrideReader** (`Pipeline/Override/`) — discovers every mod directory with a `MissingQuestWeapons/` folder, reads `QuestOverrides.jsonc`, `WeaponOverrides.jsonc`, `AttachmentOverrides.jsonc`, and merges them via the `OverrideBehaviour` system. Emits an `OverriddenSettings` container consumed by the other phases.
2. **WeaponCategorizer** / **AttachmentCategorizer** (`Pipeline/Weapon/`, `Pipeline/Attachment/`) — walk the item DB filtered by `weaponLikeAncestors` (weapons) or `Mod` ancestry (attachments), run the rule engine, apply `manualTypeOverrides`, and build the short-name alias map. Default rules for weapons come from `DefaultWeaponRuleFactory.Build(itemDb, config.WeaponLikeAncestors)` — one rule per ancestor, either `{directChildOf:A}` when `A` has a subtree (Weapon → AssaultRifle → …) or literal `A` when items are direct children (Knife, ThrowWeap, Launcher).
3. **QuestPatcher** (`Pipeline/Quest/`) — iterates every `CounterCreator` condition. For each sub-condition it dispatches to every registered `IConditionExpander` (`WeaponArrayExpander`, `WeaponModsExpander`). Expanders mutate in place and don't coordinate.

Adding a new expandable condition field is one new `IConditionExpander` + a registration in `QuestPatcher`'s constructor list in `AddMissingQuestRequirementsLoader.OnLoad`. The patcher core loop stays unchanged.

### CounterCreator sub-condition fields

Each CounterCreator condition holds a `counter.conditions[]` array. Each sub-condition can carry:

| Field | Type | Semantics | Handler |
|-------|------|-----------|---------|
| `weapon` | `string[]` | Flat list of weapon item IDs that count the kill. Honours the caliber filter below. | `WeaponArrayExpander` |
| `weaponCaliber` | `string[]` | Caliber strings (e.g. `"Caliber556x45NATO"`). **Filter**, not expand target — constrains expansion. | `WeaponArrayExpander` (filter) |
| `weaponModsInclusive` | `string[][]` | Each inner array is an AND-bundle; bundles compose with OR. Kill counts iff at least one bundle is fully present on the weapon. | `WeaponModsExpander` |
| `weaponModsExclusive` | `string[][]` | Same structure, inverted: weapon is rejected iff at least one bundle is fully present. | `WeaponModsExpander` |

Fields do not always appear together. Base-game quests usually have `weaponCaliber` / `weaponMods*` alongside a `weapon` array, but mod-added quests can ship `weaponModsInclusive` / `weaponModsExclusive` without any `weapon` array. Each field is processed independently.

### Logging abstraction

Domain phases depend on `IModLogger` (Util/IModLogger.cs), not `ISptLogger<T>`, so they can run offline under the Inspector.

```csharp
public interface IModLogger
{
    void Info(string message);
    void Success(string message);
    void Warning(string message);
    void Debug(string message);   // gated externally
}
```

Implementations:
- `NullModLogger` — no-op singleton, for tests.
- `CapturingModLogger` — per-level lists, for log-assertion tests.
- `FileModLogger` — truncates + appends to `AddMissingQuestRequirements.log` next to the DLL.
- `SptModLogger<T>` — adapter over `ISptLogger<T>`; used inside the server process.
- `DebugFilteringModLogger` — decorator that suppresses `Debug` when `config.Debug == false`.
- `TeeModLogger` — fan-out. `Info` / `Success` / `Warning` write to both sinks; `Debug` writes to the file sink only (the console would flood).

The SPT loader wires: `TeeModLogger(SptModLogger, DebugFilteringModLogger(FileModLogger, config.Debug))`. Call sites call `Debug` unconditionally; the gating happens at the decorator.

### Override system

`OverrideBehaviour` enum: `IGNORE` (default), `MERGE`, `REPLACE`, `DELETE`. Applied at three levels — file, entry, individual array value. Array values support `{ "value": "...", "behaviour": "DELETE" }`. Condition-override precedence: condition-specific override (matching `condition` ID in the entry) → generic quest override → no override. See `Pipeline/Override/OverrideReader.cs` for the merge loop.

### Rule-chain type detection

Every item is classified by one of two parallel rule sets:

- **Default rules** live in code. Weapons: `DefaultWeaponRuleFactory` (generated from `WeaponLikeAncestors`). Attachments: `Pipeline/Attachment/DefaultAttachmentRules.cs`.
- **User rules** ship per-mod in `WeaponOverrides.jsonc` / `AttachmentOverrides.jsonc` under `customTypeRules`. `OverrideReader` merges them into `settings.TypeRules` / `settings.AttachmentTypeRules`. User rules are evaluated before defaults.

Each rule is `{ conditions: {...}, type: "TypeName", alsoAs: ["..."] }`. All keys inside `conditions` are ANDed. The engine (`Pipeline/Rules/RuleEngine.cs`) evaluates every rule against every item and unions the matches — an item can belong to many types. `TypeSelector` picks the smallest covering type at expansion time.

Condition keys (registered in `Pipeline/Rules/RuleConditions/ConditionFactory.cs`):

| Key | Checks |
|-----|--------|
| `hasAncestor` | item has a Node with this `_name` anywhere in its `_parent` chain |
| `properties` | every key/value in the object matches `_props.<key> == <value>` |
| `caliber` | `_props.ammoCaliber` equals value |
| `nameContains` | locale name substring (case-insensitive) |
| `nameMatches` | locale name regex (case-insensitive) |
| `descriptionMatches` | locale description regex |
| `pathMatches` | full ancestor path string matches regex (powerful, brittle) |
| `and` / `or` / `not` | meta-conditions composing leaf conditions |

User-authored regex is wrapped in `RegexGuard` (`Pipeline/Rules/RuleConditions/RegexGuard.cs`) with a 500ms `MatchTimeout` and a catching `IsMatchSafe` — pathological patterns degrade to a non-match instead of hanging startup.

The catch-all template `{directChildOf:X}` resolves at runtime to the `_name` of whichever node sits directly below `X` in the item's chain — auto-covers new SPT weapon categories without a config change.

`DefaultAttachmentRules` ships two rule classes:

1. **Structural** — `{directChildOf:X}` for every ancestor under `Mod` (Muzzle → Silencer/FlashHider/MuzzleCombo, Sights → Collimator/OpticScope/…). Covers the whole attachment tree without per-node config.
2. **Functional muzzle types** — `muzzleModType` property rules producing `Suppressor`, `MuzzleBrake`, `Compensator`, `MuzzleAdapter`. BSG's structural parents conflate function (the FlashHider node also holds brakes and compensators); these rules give `TypeSelector` a tighter option when it matters.

Custom categories are first-class: a type named `"AKM"` produced by a user rule is the same shape as `"AssaultRifle"` produced by a default rule — same maps, same expansion.

### Mod-group expansion

`WeaponModsExpander` rewrites each of `weaponModsInclusive` and `weaponModsExclusive` as a whole. Per field, per mode:

- **`Auto` (default)**:
  - Partition groups into singletons (`Count == 1`) and multi-item bundles (`Count >= 2`).
  - Multi-item bundles pass through verbatim — they're authored AND-bundles and must not be broadened.
  - Singletons expand only if both gates pass:
    1. There are at least 2 singletons in the field (lone singleton → verbatim, mirrors `WeaponArrayExpander.Count >= 2`).
    2. Every singleton's minimal covering type is identical (strict match; catch-all ancestors like `Muzzle` don't count as a shared type).
  - When both gates pass, emit one singleton group per member of the shared type, plus `canBeUsedAs` aliases per emitted ID.
  - When either gate fails, emit singletons verbatim.
- **`NoExpansion`**: every original group verbatim; `IncludedMods` still appended as singletons.
- **`WhitelistOnly`**: original field discarded; rebuilt from `IncludedMods` singletons only.

`IncludedMods` appends new singleton groups. `ExcludedMods` drops groups: a bare id drops any containing group; a type name drops only groups whose members are entirely in that type. Empty groups are dropped. Output is de-duplicated by sorted-set key, first-occurrence order.

Rationale for strict-per-singleton match: BSG sometimes lists mixed-category items in a single condition (e.g. 22 silencers + 1 muzzle brake in "The Tarkov Shooter - Part 7"). Union-based type selection would fall through to a broad ancestor and balloon the output. Strict match preserves authored intent when types disagree.

### Config file layout

```
config/
  config.jsonc                          ← global ModConfig
  MissingQuestWeapons/
    QuestOverrides.jsonc                ← excludedQuests + per-quest/per-condition entries
    WeaponOverrides.jsonc               ← manualTypeOverrides, canBeUsedAs, customTypeRules,
                                          aliasNameStripWords, aliasNameExcludeWeapons
    AttachmentOverrides.jsonc           ← manualTypeOverrides, canBeUsedAs, customTypeRules,
                                          aliasNameStripWords
```

`ModConfig` fields (see `Models/ModConfig.cs`): `enabled`, `parentTypes`, `excludedItems`, `excludedWeaponTypes`, `weaponLikeAncestors`, `includeParentCategories`, `bestCandidateExpansion`, `unknownWeaponHandling`, `validateOverrideIds`, `debug`. Every field has a sane default — the shipped `config.jsonc` is a worked example, not a requirement.

Built-in rules live in code. User rules ride on the `customTypeRules` field of each `*Overrides.jsonc` — no `rules/` subdirectory.

All config files carry a top-level `"version"` integer. Missing version = v0 (TS format) and triggers the full migration chain. Migration functions live in `Config/Migrations.cs` (e.g. `v0_to_v1`, `v3_to_v4_Config`). Newer versions on disk than the binary supports trigger a warning and the file is used as-is.

### Improvements over the TS version

These were the reasons for the rewrite and are all shipped today.

1. **Attachment condition expansion** — `weaponModsInclusive` / `weaponModsExclusive` handled by `WeaponModsExpander` (see §Mod-group expansion). Mod-added quests with no `weapon` array are supported.
2. **Caliber-based type detection** — native `caliber` condition in rules, not limited to custom categories.
3. **Granular condition filtering** — override entries can match conditions by structural properties, not only by condition ID.
4. **Separated logging** — categorization output, patching output, and end-of-run summary are distinct. Full per-condition before/after diff available via the Inspector HTML report.
5. **Rule-chain type detection** — replaces the TS hardcoded bolt-action / pump-action / revolver branches. New detections are a config change, not a code change.
6. **Best-candidate expansion** — opt-in (`bestCandidateExpansion`). When one type covers all but one weapon in a condition, expand using that type and log the outlier instead of silently doing nothing.
7. **Override ID validation** — opt-in (`validateOverrideIds`). Warn at startup when `includedWeapons` / `excludedWeapons` reference IDs not in the DB.

### Key behavioural rules (inherited from TS; do not change without a plan)

- **Weapon field:** single-weapon conditions (`Count == 1`) are never expanded by type. Whitelist (`includedWeapons`) and `canBeUsedAs` still apply. Mirrored by `WeaponArrayExpander.Count >= 2`.
- **Weapon processing order** within a condition: type expansion → whitelist additions → `canBeUsedAs` aliases → blacklist (`excludedWeapons`) removals. Blacklist always runs last.
- **Mod fields:** see §Mod-group expansion. Multi-item groups are AND-bundles and are preserved verbatim; singleton expansion fires only with ≥2 singletons sharing the same minimal type.
- **Unknown IDs** follow `ModConfig.UnknownWeaponHandling`: `Strip` removes with a warning; `KeepInDb` keeps IDs present in the item DB silently and strips others with a warning; `KeepAll` keeps everything silently.
- **`canBeUsedAs` alias expansion:** after the alias map is built, a second pass cross-links all transitive group members.
- **`includeParentCategories: true` (default):** a weapon appears in both its restrictive type and every `parentTypes` ancestor (e.g. a bolt-action sniper is tagged `BoltActionSniperRifle` and `SniperRifle`). Manual-override types walk the parent chain too.

## C# code style

- **Always use braces** — `if`, `else`, `for`, `foreach`, `while`, `using`, all block statements. No braceless one-liners.
- **No expression-bodied members** for anything with logic. Trivial getters and single-call forwarders are fine.
- **Prefer `var`** when the type is obvious from the right-hand side.
- **`sealed` by default** for concrete classes unless inheritance is intentional.
- **`init`-only properties** for immutable data models (records and config classes).
- **Collection expressions** (`[..x]`, `[]`) over `new List<T>()` where supported.
- **No TODO comments in tracked files.** If it's real work, open an issue; if it's speculation, delete it.
