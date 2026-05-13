# Changelog

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
