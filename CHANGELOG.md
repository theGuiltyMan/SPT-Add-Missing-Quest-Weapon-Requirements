# Changelog

## 3.0.0

**Breaking**
- Requires SPT 4.1.x. Built against `SPTarkov.*` 4.1.2 on .NET 10. SPT 4.0 users stay on 2.3.0.
- The release zip now unpacks to `SPT_Runtime/user/mods/GuiltyMan-AddMissingQuestRequirements/` (SPT 4.1 layout).
- Override folders now load in a fixed order: this mod's own folder first, then the others alphabetically. Under the default `IGNORE` the shipped settings win; override packs that change shipped settings must set `overrideBehaviour` to `MERGE` or `REPLACE`.

**Fixes**
- A broken override file from another mod (invalid JSON, misspelled `overrideBehaviour`) is now skipped with a warning naming the file, instead of stopping all quest patching.
- `overrideBehaviour: IGNORE` (the default) now adds new items to `excludedQuests`, `aliasNameStripWords`, `aliasNameExcludeWeapons` and `customTypeRules` instead of discarding the file's whole list.
- Inspector: results now match the live server (attachment `customTypeRules`, folder order, file migrations). It still never writes to your config files.
- Inspector `ExportConfigPath`: the export now keeps every `config.jsonc` field, every quest override entry and attachment `customTypeRules`, and uses the current file versions. With several override folders, a re-imported export can resolve some conditions differently.
- `AttachmentOverrides.jsonc` is now expected at version 2, matching the shipped file. Existing files need no change.
- Quest override entries now merge per condition. Several entries for the same quest, one per condition, all apply. `IGNORE` applies only to conditions no earlier entry covered; `REPLACE`, `MERGE` and `DELETE` act on exactly the conditions they list. An empty `conditions` list covers every condition.
- The `AK` weapon type no longer includes the Akula push dagger, the Bofors Ak5 rifles or the AK-50.
- Mod-added quests with several kill conditions, each needing a different weapon, now accept only that condition's weapons.
- Two quests with bundled overrides (20 gauge / 23x75 weapons, Mosin rifles) accepted no weapon at all. They now accept the intended weapons.
- Override packs can now add weapons to quest conditions that require one specific weapon; the shipped config no longer blocks them.

**Internal**
- Ported to the SPT 4.1 server API: `IOnLoad.OnLoadAsync`, `IModMetadata`, and injected `TemplateTable` in place of `DatabaseServer`. No behaviour change in categorization or quest patching.

## 2.3.0

**Fixes**
- Override item IDs are now matched case-insensitively against the item database and folded to the DB's canonical casing. Miscased IDs (e.g. uppercase Massivesoft template IDs) in `manualTypeOverrides`, `canBeUsedAs`, and quest `includedWeapons` / `excludedWeapons` / `includedMods` / `excludedMods` previously no-opped silently; they now work and are written back into quests with the correct casing.

**Added**
- Code-level default rule tags every bolt-action rifle under the `SniperRifle` node (`_props.BoltAction == true`) as `BoltActionSniperRifle`, removing the need for per-weapon manual overrides. Guarded by `hasAncestor: SniperRifle` so the bolt-action TOZ-106 shotgun is not mistagged. Both the SPT loader and the Inspector inherit the rule via `DefaultWeaponRuleFactory`.

## 2.2.0

**Added**
- `applyToManualOverrides` boolean on `TypeRule` (default `false`). When `true`, a non-core rule (`nameContains` / `nameMatches` / `pathMatches` / `descriptionMatches`) merges its type into items that have a `manualTypeOverrides` entry instead of being suppressed. Lets mod authors define grouping rules (e.g. an "HK" name-match) that stack with per-item structural overrides.

## 2.0.4

**Fixes**
- Quest overrides using `"expansionMode": 2` with `includedWeapons` now actually add those weapons. Affected many entries in the shipped config and user patches — they silently did nothing before.
- Conflicting override modes from different mods now resolve correctly when merging.

**Inspector report**
- Manually-blacklisted quests and quests with nothing to expand are now flagged separately from real no-ops.
- Quests tab filter: pick any combination of `Blacklisted` / `No eligible` / `Noop` / `Expanded`. Defaults to showing only expanded quests.

## 2.0.3

- Quest override resolver matches outer CounterCreator wrapper id, not just the inner sub-condition id.
- Mods-side override fields (`modsExpansionMode`, `includedMods`, `excludedMods`) preserved through merge.

## 2.0.2

- Loader runs after TraderRegistration so other mods' quests are visible to the patcher.
