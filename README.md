# AddMissingQuestRequirements

> Server-side SPT 4.x mod that auto-expands quest weapon requirements to include modded clones.

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet) ![SPT 4.x](https://img.shields.io/badge/SPT-4.x-8B0000) ![License: MIT](https://img.shields.io/badge/license-MIT-green)

A server-side SPT mod that automatically includes **modded weapons and attachments** in base-game quest conditions.

Every vanilla quest that says *"kill 10 PMCs with an AK-74"* implicitly means "any modded AK-74 clone too" — but the game only counts the specific weapon IDs BSG listed. This mod fixes that at server startup: it figures out what category each weapon and attachment belongs to, then expands quest conditions to include every same-category item installed on your server.

**No in-raid cost.** Everything happens once, when the server boots. Nothing is patched on the fly.

---

## Install

1. Build or download the release. Drop the whole folder into `SPT/user/mods/`:

    ```
    SPT/user/mods/AddMissingQuestRequirements/
        AddMissingQuestRequirements.dll
        config/
            config.jsonc
        MissingQuestWeapons/
            QuestOverrides.jsonc
            WeaponOverrides.jsonc
            AttachmentOverrides.jsonc
    ```

2. Start the SPT server. You should see one line in the server console:

    > `AddMissingQuestRequirements ready in 1047ms — 20 of 49 weapon-quests changed (50 of 918 conditions). Weapons: 203 items across 66 types. Attachments: 1990 items across 1338 types.`

3. Launch the game. Quest kill counts should now tick on modded weapons of the same type.

**Nothing to configure to use the mod** — defaults cover all vanilla quests. Read on only if you want to customize behaviour or add support for your own mods.

---

## Build from source

Requirements: .NET 9 SDK. Clone, then:

```bash
dotnet build -c Release
```

The mod DLL lands in `AddMissingQuestRequirements/bin/Release/net9.0/`. Copy the built folder into `SPT/user/mods/AddMissingQuestRequirements/` alongside `config/` and `MissingQuestWeapons/`.

For auto-deploy on each build, copy `local.props.template` → `local.props` and point `SptRoot` at your SPT install; the Release build then drops the DLL + config tree into `user/mods/` automatically.

Run the test suite:

```bash
dotnet test --filter "FullyQualifiedName!~Integration"        # unit tests only (fast, fully hermetic)
dotnet test --filter "Category=Integration"                   # integration tests (need an SPT DB slice)
```

Integration tests look for an SPT database slice in this order: the `SPT_TEMPLATES_DIR` + `SPT_LOCALE_EN_PATH` env vars, then a local `SptDbExporter/export/` directory walked upward from the test binary. When neither is present the integration tests short-circuit to a no-op (still pass, but assert nothing). Produce a slice by running `SptDbExporter` against a real SPT install, or point the env vars at your `SPT_Data/database/` tree.

---

## How it works (at a glance)

At server startup, the mod runs three phases:

1. **Discover overrides.** Every mod's `MissingQuestWeapons/` folder is scanned and the override files are merged.
2. **Categorize items.** Each weapon in the game DB is assigned one or more type names (`AssaultRifle`, `Shotgun`, `BoltActionSniperRifle`, …) via a rule chain; attachments are categorized the same way (`Silencer`, `Suppressor`, `Collimator`, …).
3. **Patch quests.** For every quest condition, if it lists a specific weapon ID the mod expands it to include every weapon of the same type. Attachment groups are treated similarly.

Once the server finishes startup, the in-memory quest data has been rewritten. From that point on, the game behaves as usual — no runtime hooks.

---

## Configuration

All user-editable settings live in three JSONC files. Comments and trailing commas are allowed.

### `config/config.jsonc` — global settings

| Field | Type | Default | What it does |
|---|---|---|---|
| `enabled` | bool | `true` | Master switch. Set to `false` to skip the entire pipeline on next server start. The startup line confirms it's disabled; no quest or item data is touched. |
| `parentTypes` | object | see file | Makes one type "kind of" another. A weapon tagged `Revolver` is also implicitly `Pistol`. |
| `excludedItems` | string[] | `[]` | Item IDs to ignore completely during categorization. |
| `excludedWeaponTypes` | string[] | `[]` | Type names whose weapons are excluded from expansion. |
| `weaponLikeAncestors` | string[] | `["Weapon","Knife","ThrowWeap","Launcher"]` | Which DB-root nodes count as "weapons" for categorization. `Launcher` covers underbarrel grenade launchers (GP-34, M203, GP-25), which live outside the `Weapon` subtree. |
| `includeParentCategories` | bool | `true` | When a weapon belongs to a specific type like `BoltActionSniperRifle`, should it also count as the broader parent type `SniperRifle`? |
| `bestCandidateExpansion` | bool | `false` | If a quest lists 5 weapons where 4 share a type and 1 doesn't, expand using the shared type anyway (with a warning). |
| `unknownWeaponHandling` | int | `2` | What to do with IDs the mod can't find: `0`=Strip, `1`=KeepInDb, `2`=KeepAll. |
| `validateOverrideIds` | bool | `false` | Warn at startup for any override IDs not in the DB. |
| `debug` | bool | `false` | Emit detailed logs **and** write a JSON + HTML debug report to the mod folder. |

### `MissingQuestWeapons/QuestOverrides.jsonc` — tweak individual quests

Top-level structure:

```jsonc
{
  "version": 2,
  "overrideBehaviour": 0,          // how THIS file merges with other mods' copies
  "excludedQuests": [
    "5c1234c286f77406fa13baeb",    // these quests are left untouched
    ...
  ],
  "overrides": [
    {
      "id": "59ca2eb686f77445a80ed049",   // quest ID
      "expansionMode": 2,                  // see table below
      "conditions": [],                    // which sub-conditions this applies to (empty = all)
      "includedWeapons": ["SVD"],          // forcibly include these (IDs or type names)
      "excludedWeapons": [],               // forcibly exclude these (IDs or type names)

      // Optional mod-group fields (for weaponModsInclusive / weaponModsExclusive):
      "modsExpansionMode": 0,
      "includedMods": [],
      "excludedMods": []
    },
    ...
  ]
}
```

**`expansionMode` (for the weapon list)**

| Value | Name | Meaning |
|---|---|---|
| `0` | `Auto` | Default. Full pipeline — type expansion → whitelist additions → aliases → blacklist removals. |
| `1` | `WhitelistOnly` | Discard the game's weapon list; use `includedWeapons` only. |
| `2` | `NoExpansion` | Keep the game's weapon list; don't add same-type weapons. `includedWeapons` still applies. |

**`modsExpansionMode`, `includedMods`, `excludedMods` — all three apply only to `weaponModsInclusive`.** `weaponModsExclusive` always runs in the default `Auto` mode regardless of the override (broadening a kill-counts rule and simultaneously tightening its reject rule would silently invert the author's intent, so the exclusive field is left alone).

| Value | Name | Meaning for `weaponModsInclusive` |
|---|---|---|
| `0` | `Auto` | Singleton groups with ≥2 same-type peers expand; multi-item AND-bundles (like "Test Drive" suppressor+scope combos) stay verbatim. |
| `1` | `WhitelistOnly` | Discard the original field, rebuild from `includedMods` only. |
| `2` | `NoExpansion` | Keep the original groups verbatim; `includedMods` still appended. |

`includedMods` — bare attachment IDs and type names (like `"Suppressor"`) are each appended to `weaponModsInclusive` as **singleton groups** (one item per group). Type-name entries expand to one singleton per member of that type. There is no override knob for adding a multi-item AND-bundle.

`excludedMods` — drops groups from `weaponModsInclusive`. A bare ID drops any group containing it. A type name drops only groups whose members are *all* in that type.

### `MissingQuestWeapons/WeaponOverrides.jsonc` — weapon categorization

| Field | What it does |
|---|---|
| `manualTypeOverrides` | Force a specific weapon ID into one or more types. Useful when the rule chain misses a niche weapon. Format: `"itemId": "Type1,Type2"`. |
| `canBeUsedAs` | Declare that two weapon IDs are interchangeable for quest purposes (e.g. a modded AK variant counts as the base AK). Format: `"id_a": ["id_b"]`. |
| `aliasNameStripWords` | Words stripped from weapon locale names before the auto-aliasing pass. |
| `aliasNameExcludeWeapons` | Weapons that should *not* participate in short-name aliasing. |
| `customTypeRules` | Your own type-detection rules. See "Writing type rules" below. |

### `MissingQuestWeapons/AttachmentOverrides.jsonc` — attachment categorization

Same fields as WeaponOverrides but for attachments:

| Field | What it does |
|---|---|
| `manualAttachmentTypeOverrides` | Force an attachment ID into types. |
| `canBeUsedAs` | Cross-link interchangeable attachment IDs. |
| `aliasNameStripWords` | Strip color/finish suffixes before auto-aliasing. |
| `customTypeRules` | Your own attachment type rules. |

---

## Writing type rules

Rules classify items by looking at their properties. The mod ships with built-in rules; you add your own under `customTypeRules` in the respective overrides file.

A rule has three parts:

```jsonc
{
  "conditions": { /* what the item must satisfy — all keys ANDed */ },
  "type": "TypeName",                // the name to assign
  "alsoAs": ["OtherType", ...]       // optional extra types
}
```

### Available conditions

| Key | Checks |
|---|---|
| `hasAncestor` | `"AssaultRifle"` — item's `_parent` chain contains a node with this `_name`. |
| `properties` | `{ "BoltAction": true, "ammoCaliber": "Caliber556x45NATO" }` — all listed `_props` values match. |
| `caliber` | `"Caliber762x39"` — shorthand for `_props.ammoCaliber` equality. |
| `nameContains` | `"pump"` — case-insensitive substring check against the locale name. |
| `nameMatches` | `"^HK\\s*416"` — regex against locale name. |
| `descriptionMatches` | Regex against locale description. |
| `pathMatches` | Regex against the full `_parent`-chain name path. |
| `and` / `or` / `not` | Meta-conditions. `and` / `or` take an array of objects; `not` takes one object. |

### Examples

**Tag every bolt-action sniper explicitly:**

```jsonc
{
  "conditions": {
    "hasAncestor": "SniperRifle",
    "properties": { "BoltAction": true }
  },
  "type": "BoltActionSniperRifle"
}
```

**Keyword-based custom category: "russian AK-family assault rifles":**

```jsonc
{
  "conditions": {
    "and": [
      { "hasAncestor": "AssaultRifle" },
      { "or": [
          { "nameMatches": "^AK[-\\s]?\\d" },
          { "nameContains": "Kalashnikov" }
      ]}
    ]
  },
  "type": "russian_ar"
}
```

**Attachment functional type by `muzzleModType`** (built-in examples from the shipped rules):

```jsonc
{ "conditions": { "hasAncestor": "Muzzle", "properties": { "muzzleModType": "silencer" } },   "type": "Suppressor"  },
{ "conditions": { "hasAncestor": "Muzzle", "properties": { "muzzleModType": "brake"    } },   "type": "MuzzleBrake" },
// "conpensator" is BSG's spelling in the item database — not a typo in this config.
{ "conditions": { "hasAncestor": "Muzzle", "properties": { "muzzleModType": "conpensator"  }}, "type": "Compensator" }
```

### The `{directChildOf:X}` template

Use this as a `type` value instead of a literal name. At runtime it resolves to the `_name` of whichever node sits directly below `X` in the item's chain. The built-in weapon rule is a single one-liner:

```jsonc
{ "conditions": { "hasAncestor": "Weapon" }, "type": "{directChildOf:Weapon}" }
```

This automatically assigns every weapon to its immediate SPT category (`Pistol` / `AssaultRifle` / `Shotgun` / …) without listing them.

---

## Debugging

Turn on `"debug": true` in `config/config.jsonc`. On the next server start you'll get:

- **Detailed log output** — every category assignment, every quest patch, every skipped ID.
- **`AddMissingQuestRequirements-debug-report.json`** next to the DLL. Structured dump of: settings, every weapon and attachment with its assigned types, every patched quest with `before` / `after` for each condition.
- **`AddMissingQuestRequirements-debug-report.html`** — same data as an interactive browseable report. Open it in a browser. The Quests tab is the most useful view: a search box filters by quest name/ID, and a "hide noop" toggle focuses on conditions the mod actually changed. Expand a quest to see each condition's before/after weapon list and mod groups side by side.

The HTML report is the **same format** the standalone Inspector tool produces (run `dotnet run --project AddMissingQuestRequirements.Inspector`, or the convenience wrappers in `tools/`), so you can compare against the "what should happen" baseline without SPT running.

### Logging

The SPT console always receives Info / Success / Warning lines (Overrides
loaded, Weapons categorized, Attachments categorized, the startup summary,
and any warnings). The file `AddMissingQuestRequirements.log` next to the
DLL receives everything, including Debug lines when `debug: true` in
`config/config.jsonc`. The file is truncated on every server start, so you
always get a single clean transcript per run.

If you are debugging behaviour, turn on `debug` and read the log file —
the console stays readable and the detailed trace lives on disk.

If you hit an issue, open the HTML report, find the problem quest, and look at what got added or missing. Most misbehaviours are either:

- **Wrong type assigned** → add a `manualTypeOverrides` entry for the specific weapon/attachment, or write a `customTypeRules` rule.
- **Over-expansion** (too many weapons added to a quest) → add a `QuestOverrides` entry with `expansionMode: 2` (`NoExpansion`) or use `excludedWeapons`.
- **Under-expansion** (a modded weapon not getting picked up) → check if it's in the right type; use `canBeUsedAs` or `includedWeapons`.

---

## Advanced topics

### Override merging across mods

Every installed mod can ship its own `MissingQuestWeapons/` folder. The mod scans all of them and merges the contents. Conflicts are resolved by `overrideBehaviour`:

| Value | Name | Meaning |
|---|---|---|
| `0` | `IGNORE` | Default. Entries later in the merge don't overwrite earlier ones. |
| `1` | `MERGE` | Arrays are unioned. |
| `2` | `REPLACE` | Later entry wins completely. |
| `3` | `DELETE` | Later entry removes earlier. |

Each file has a top-level `overrideBehaviour` (the default for its entries) that individual entries can override:

```jsonc
{
  "id": "59ca2eb686f77445a80ed049",
  "behaviour": 2,                // this entry fully replaces any prior mod's override
  "includedWeapons": ["MyCustomWeaponId"]
}
```

Individual array values can also carry a behaviour:

```jsonc
"includedWeapons": [
  "AK",
  { "value": "ObsoleteWeaponId", "behaviour": 3 }   // DELETE
]
```

### How `weaponModsInclusive` groups work

The field is an **array of arrays**. Every inner array is one group. Two rules:

- **Inside a group: AND.** All items in the group must be on the weapon.
- **Across groups: OR.** Any one group is enough.

Example — "kill using a silencer **and** a scope":

```jsonc
"weaponModsInclusive": [
  ["silencer_id", "scope_id"]
]
```

One group, two items → the weapon must carry both.

Example — "kill using a silencer **or** a muzzle brake":

```jsonc
"weaponModsInclusive": [
  ["silencer_id"],
  ["muzzle_brake_id"]
]
```

Two groups, one item each → either one is enough.

**What the mod does to existing groups.** Multi-item groups pass through verbatim — the expander never adds or removes items inside a group. Singleton groups (one item) can expand into N singletons when the field has ≥2 singletons that share a common type; see `modsExpansionMode` above.

**What you can do through overrides.** `includedMods` appends new **singleton** groups (each entry becomes its own one-item group). `excludedMods` drops groups. There's no config field for adding a new multi-item AND-bundle — if a quest needs a brand-new "A and B" requirement, the override surface can't express it today.

### Multi-mod layering example

Suppose mod A ships `AttachmentOverrides.jsonc` with a `canBeUsedAs` entry for its custom scope. Mod B wants to override that link. Mod B's file:

```jsonc
{
  "version": 2,
  "overrideBehaviour": 1,     // default: MERGE
  "canBeUsedAs": {
    "mod_b_custom_scope": ["mod_a_custom_scope"]
  }
}
```

Since neither declares `REPLACE`, the resulting map unions both entries. Mod A's link stays; Mod B's link is added.

### File versioning

Every config file has a top-level `"version"` integer. When you upgrade the mod and a newer version ships, old files are auto-migrated on load — your settings survive. You'll see a warning in the log if anything couldn't be migrated cleanly.

---

## Getting help

Turn on `"debug": true` and restart the server once, then open an issue with:

1. Your `config.jsonc`, `WeaponOverrides.jsonc`, `QuestOverrides.jsonc`, `AttachmentOverrides.jsonc`.
2. `AddMissingQuestRequirements-debug-report.json` and `AddMissingQuestRequirements-debug-report.html` from next to the DLL.
3. `AddMissingQuestRequirements.log` from next to the DLL.
4. A description of the quest / weapon / attachment that's misbehaving.
