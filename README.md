# AddMissingQuestRequirements

> Server-side SPT 4.1 mod that auto-expands quest weapon requirements to include modded clones.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet) ![SPT 4.1](https://img.shields.io/badge/SPT-4.1-8B0000) ![License: MIT](https://img.shields.io/badge/license-MIT-green)

A server-side SPT mod that automatically includes **modded weapons and attachments** in base-game quest conditions.

Every vanilla quest that says *"kill 10 PMCs with an AK-74"* implicitly means "any modded AK-74 clone too" — but the game only counts the specific weapon IDs BSG listed. This mod fixes that at server startup: it figures out what category each weapon and attachment belongs to, then expands quest conditions to include every same-category item installed on your server.

**No in-raid cost.** Everything happens once, when the server boots. Nothing is patched on the fly.

---

## Install

1. Build or download the release. Drop the whole folder into `SPT_Runtime/user/mods/`:

    ```
    SPT_Runtime/user/mods/AddMissingQuestRequirements/
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

Requirements: .NET 10 SDK. Clone, then:

```bash
dotnet build -c Release
```

The build output lands in `AddMissingQuestRequirements/bin/Release/AddMissingQuestRequirements/`: the DLL plus `config/` and `MissingQuestWeapons/`. Copy that folder into `SPT_Runtime/user/mods/`.

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
| `unknownWeaponHandling` | string | `"KeepAll"` | What to do with IDs the mod can't find: `"Strip"` (0), `"KeepInDb"` (1), `"KeepAll"` (2). |
| `validateOverrideIds` | bool | `false` | Warn at startup for any override IDs not in the DB. |
| `modBundleCartesianCap` | int | `500` | Maximum number of AND-bundles one `includedModBundles` / `excludedModBundles` entry may produce. Larger products are truncated with a warning. |
| `debug` | bool | `false` | Emit detailed logs **and** write a JSON + HTML debug report to the mod folder. |

### `MissingQuestWeapons/QuestOverrides.jsonc` — tweak individual quests

Top-level structure:

```jsonc
{
  "version": 3,
  "overrideBehaviour": "IGNORE",  // how THIS file merges with other mods' copies
  "excludedQuests": [
    "5c1234c286f77406fa13baeb",    // these quests are left untouched
    ...
  ],
  "overrides": [
    {
      "id": "59ca2eb686f77445a80ed049",   // quest ID
      "expansionMode": "NoExpansion",      // see table below
      "conditions": [],                    // which sub-conditions this applies to (empty = all)
      "includedWeapons": ["SVD"],          // forcibly include these (IDs or type names)
      "excludedWeapons": [],               // forcibly exclude these (IDs or type names)

      // Optional mod-group fields (for weaponModsInclusive / weaponModsExclusive):
      "modsExpansionMode": "Auto",
      "includedMods": [],                  // appended to weaponModsInclusive
      "excludedMods": [],                  // appended to weaponModsExclusive
      "includedModBundles": [],            // AND-bundles appended to weaponModsInclusive
      "excludedModBundles": []             // AND-bundles appended to weaponModsExclusive
    },
    ...
  ]
}
```

Enum fields (`expansionMode`, `modsExpansionMode`, `overrideBehaviour`, `behaviour`, `unknownWeaponHandling`) accept either the name (case-insensitive) or the number shown in the tables.

**`expansionMode` (for the weapon list)**

| Value | Name | Meaning |
|---|---|---|
| `0` | `Auto` | Default. Full pipeline — type expansion → whitelist additions → aliases → blacklist removals. |
| `1` | `WhitelistOnly` | Discard the game's weapon list; use `includedWeapons` only. Good when BSG's list is wrong. |
| `2` | `NoExpansion` | Keep the game's weapon list; don't add same-type weapons. `includedWeapons` still applies. |

**`modsExpansionMode` (for `weaponModsInclusive` / `weaponModsExclusive`)**

| Value | Name | Meaning |
|---|---|---|
| `0` | `Auto` | Singleton groups with ≥2 same-type peers expand; multi-item AND-bundles (like "Test Drive" suppressor+scope combos) stay verbatim. |
| `1` | `WhitelistOnly` | Discard the original field, rebuild it from the override: `includedMods` + `includedModBundles` for `weaponModsInclusive`, `excludedMods` + `excludedModBundles` for `weaponModsExclusive`. |
| `2` | `NoExpansion` | Keep the original groups verbatim; the override's mods and bundles are still appended. |

**`includedMods` / `excludedMods`**

Each field only adds to its own condition field; nothing in an override removes groups. `includedMods` adds required mods to `weaponModsInclusive` (the weapon must carry one of them). `excludedMods` adds forbidden mods to `weaponModsExclusive` (the weapon must carry none of them). A bare attachment ID adds one single-item group; a type name (like `"Suppressor"`) adds one single-item group per attachment of that type.

**`includedModBundles` / `excludedModBundles`**

For mods that must appear together. Each entry is a list whose items are type names or bare IDs; the mod emits one AND-bundle per combination (the cartesian product). Example: `[["m60_barrels", "aimpoint_scopes"]]` (types holding 2 barrels and 5 scopes) adds 10 bundles of shape `[barrel, scope]`. Each entry is capped at `modBundleCartesianCap` bundles (see `config.jsonc`).

### `MissingQuestWeapons/WeaponOverrides.jsonc` — weapon categorization

| Field | What it does |
|---|---|
| `manualTypeOverrides` | Force a specific weapon ID into one or more types. Useful when the rule chain misses a niche weapon. Format: `"itemId": "Type1,Type2"`. |
| `canBeUsedAs` | Declare that two weapon IDs are interchangeable for quest purposes (e.g. a modded AK variant counts as the base AK). Format: `"id_a": ["id_b"]`. |
| `aliasNameStripWords` | Words stripped from weapon locale names before the auto-aliasing pass. Use for pure-cosmetic suffixes like `"(FDE)"`. Never put model/mechanism words here. |
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

A rule has four parts:

```jsonc
{
  "conditions": { /* what the item must satisfy — all keys ANDed */ },
  "type": "TypeName",                       // the name to assign
  "alsoAs": ["OtherType", ...],             // optional extra types
  "applyToManualOverrides": false           // optional, see "Stacking on manual overrides" below
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
| `pathMatches` | Regex against the full `_parent`-chain name path (most powerful, most brittle). |
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

### Stacking on manual overrides

Non-core rule conditions (`nameContains` / `nameMatches` / `pathMatches` / `descriptionMatches`) are skipped for any item that is already listed in `manualTypeOverrides`. The assumption is that you wrote the override because the heuristic was wrong, so re-applying it would undo your intent.

When the opposite is true — you want a grouping rule like an "HK family" name-match to stack on top of structural overrides like `"AssaultRifle,AssaultCarbine"` — set `"applyToManualOverrides": true` on that single rule:

```jsonc
{
  "conditions": {
    "or": [
      { "nameMatches": "^HK" },
      { "descriptionMatches": "( HK |Heckler|Koch)" }
    ]
  },
  "type": "HK",
  "applyToManualOverrides": true
}
```

Core rules (`hasAncestor` / `properties` / `caliber`) always stack regardless of this flag.

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

The HTML report is the **same format** the standalone Inspector tool produces (see `./inspect.bat`), so you can compare against the "what should happen" baseline without SPT running.

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
- **Over-expansion** (too many weapons added to a quest) → add a `QuestOverrides` entry with `"expansionMode": "NoExpansion"` or use `excludedWeapons`.
- **Under-expansion** (a modded weapon not getting picked up) → check if it's in the right type; use `canBeUsedAs` or `includedWeapons`.

---

## Advanced topics

### Override merging across mods

Every installed mod can ship its own `MissingQuestWeapons/` folder. The mod scans all of them and merges the contents. Conflicts are resolved by `overrideBehaviour`:

| Value | Name | Meaning |
|---|---|---|
| `0` | `IGNORE` | Default. Entries later in the merge don't overwrite earlier ones, but new ones are still added. |
| `1` | `MERGE` | Arrays are unioned. |
| `2` | `REPLACE` | Later entry wins completely. |
| `3` | `DELETE` | Later entry removes earlier. |

For plain lists (`excludedQuests`, `aliasNameStripWords`, `aliasNameExcludeWeapons`), `IGNORE` adds the items that aren't there yet, the same as `MERGE`. For `customTypeRules`, `IGNORE` skips a rule only when an earlier mod already defines a rule with the same `type`.

Load order: this mod's own `MissingQuestWeapons/` folder is read first, then every other mod folder in alphabetical order (case-insensitive). "Later" in the table above means later in that order.

Quest override entries merge **per condition**. An entry with `"conditions": []` covers every condition of the quest; otherwise it covers only the conditions it lists, by sub-condition id or by the outer CounterCreator id. For each condition, the entries that cover it are applied in load order:

- `IGNORE` applies only if no earlier entry covered this condition. An earlier entry in the same file counts too.
- `REPLACE` wins for the conditions it lists; earlier entries still apply to their other conditions.
- `MERGE` unions the lists with what earlier files set for this condition; the more restrictive expansion mode wins.
- `DELETE` removes every earlier override for the conditions it lists (all conditions if `conditions` is empty).

One entry per condition is fine, in one file or across files. Within one file, an entry naming a condition beats that file's quest-wide entry for that condition.

Each file has a top-level `overrideBehaviour` (the default for its entries) that individual entries can override:

```jsonc
{
  "id": "59ca2eb686f77445a80ed049",
  "behaviour": "REPLACE",        // this entry fully replaces any prior mod's override
  "includedWeapons": ["MyCustomWeaponId"]
}
```

Individual `canBeUsedAs` values (in `WeaponOverrides.jsonc` and `AttachmentOverrides.jsonc`) can also carry a behaviour:

```jsonc
"canBeUsedAs": {
  "MyCustomWeaponId": [
    "BaseWeaponId",
    { "value": "ObsoleteWeaponId", "behaviour": "DELETE" }   // DELETE: removes a link an earlier mod added
  ]
}
```

### Intra-group AND, cross-group OR

This matters only if you're tweaking `weaponModsInclusive` / `weaponModsExclusive` on a condition.

- A single group of `[A, B]` means the weapon must carry **both** A and B (AND).
- Multiple groups `[[A], [B]]` means the weapon must carry either A or B (OR).

The mod preserves AND-bundles verbatim when expanding. If you need to widen a bundle, you have to express the new alternative as a **new group**, not by adding items to the existing one.

### Multi-mod layering example

Suppose mod A ships `AttachmentOverrides.jsonc` with a `canBeUsedAs` entry for its custom scope. Mod B wants to override that link. Mod B's file:

```jsonc
{
  "version": 2,
  "overrideBehaviour": "MERGE",  // the default is "IGNORE"
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
