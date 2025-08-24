# Add Missing Quest Requirements

This mod automatically detects weapons from other mods and adds them to the appropriate quest requirements. It categorizes weapons based on their properties and allows for extensive customization through configuration files.

## Features

- **Automatic Weapon Detection**: Scans through all installed mods to find weapons and categorizes them.
- **Flexible Categorization**: Categorizes weapons into types like `Pistol`, `Shotgun`, `AssaultRifle`, etc. It also handles more specific types like `BoltActionSniperRifle` and `PumpActionShotgun`.
- **Highly Customizable**: Use configuration files to override weapon types, create custom categories, blacklist items, and fine-tune quest requirements.
- **Mod Integration**: Other mod authors can easily make their weapons compatible by adding a `MissingQuestWeapons` folder with configuration files to their own mod.

## Configuration

### Main Config (`config/config.jsonc`)

This file contains the main settings for the mod.

```jsonc
{
    // Defines inheritance for weapon types. For example, a Revolver is also a Pistol.
    "kindOf": {
        "Revolver": "Pistol",
        "PumpActionShotgun": "Shotgun",
        "BoltActionSniperRifle": "SniperRifle"
    },
    // A list of item IDs to be completely ignored by the mod.
    "BlackListedItems": [],
    // A list of weapon types to be ignored.
    "BlackListedWeaponsTypes": [],
    // If true, restrictive types (e.g., BoltActionSniperRifle) will also be added to their parent category (e.g., SniperRifle).
    "categorizeWithLessRestrive": true,
    // Delay in seconds before the mod starts processing.
    "delay": 0,
    // Logging preference: "all", "console", "file", "none".
    "logType": "file",
    // Enable extra logs for debugging.
    "debug": false
}
```

### Overriding Weapon Categories (`MissingQuestWeapons/OverriddenWeapons.jsonc`)

This file allows you to manually adjust how weapons are categorized. You can place this file in this mod's directory or in your own mod's `MissingQuestWeapons` folder for compatibility.

```jsonc
{
    // Manually assign one or more categories to a weapon using its ID.
    "Override": {
        "57c44b372459772d2b39b8ce": "AssaultCarbine,AssaultRifle" // AS VAL
    },
    // Define aliases. If a quest requires the key weapon, weapons in the value array will also be allowed.
    "CanBeUsedAs": {
        "5c46fbd72e2216398b5a8c9c": [ // SVDS
            "MIRA_weapon_izhmash_svd_762x54"
        ]
    },
    // Words to ignore when comparing weapon short names to find aliases.
    "CanBeUsedAsShortNameWhitelist": [
        ".300 Blackout",
        "FDE"
    ],
    // Weapon short names to exclude from the alias-finding logic.
    "CanBeUsedAsShortNameBlacklist" : [],
    // Define completely new weapon categories based on keywords, calibers, and item IDs.
    "CustomCategories": [
        {
            "name": "AKM",
            "ids": [],
            "whiteListedKeywords": [
                "\\b(AKM|AK-1|VPO|Draco)\\w*"
            ],
            "blackListedKeywords": [],
            "allowedCalibres": [
                "Caliber762x39"
            ],
            "alsoCheckDescription": false
        }
    ]
}
```

### Customizing Quests (`MissingQuestWeapons/QuestOverrides.jsonc`)

This file allows you to modify the weapon requirements for specific quests. You can place this file in this mod's directory or in your own mod's `MissingQuestWeapons` folder.

```jsonc
{
    // A list of quest IDs to be completely ignored by this mod.
    "BlackListedQuests": [
        "5c1234c286f77406fa13baeb" // Setup
    ],
    // Fine-tune individual quests.
    "Overrides": [
        {
            "id": "5a27bb8386f7741c770d2d0a", // Wet Job - Part 1
            // if true, the mod will not add any weapons to this quest automatically.
            "skip": true,
            // if true, only weapons from whiteListedWeapons will be considered for this quest.
            "onlyUseWhiteListedWeapons": false,
            // A list of weapon IDs or categories to always add to this quest's requirements.
            "whiteListedWeapons": [],
            // A list of weapon IDs or categories to always remove from this quest's requirements.
            "blackListedWeapons": []
        }
    ]
}
```

### Advanced: Override Behaviours

When multiple mods provide `QuestOverrides.jsonc` or `OverriddenWeapons.jsonc` files, there needs to be a way to resolve conflicts. This is handled by the `OverrideBehaviour` property. It allows you to control how your configuration merges with configurations from other mods.

The `OverrideBehaviour` can be set in two places:
1.  At the top level of a configuration file (`QuestOverrides.jsonc` or `OverriddenWeapons.jsonc`), which sets the default behavior for all entries in that file.
2.  On a specific entry (e.g., a single quest override in `Overrides` or a single item in `CustomCategories`), which applies only to that entry.

If no behaviour is specified, the default is `IGNORE`.

There are four possible behaviours:

- **`IGNORE` (Default)**: If an entry with the same identifier (like a quest ID or weapon ID) has already been processed from another mod's file, this entry will be skipped. This is the safest option and prevents accidental overwrites.

- **`MERGE`**: Combines the new entry with an existing one. For lists like `whiteListedWeapons`, new items are added. For boolean flags like `skip`, the value is set if `true` in the new entry.

- **`REPLACE`**: Completely overwrites an existing entry with the new one. All old values are discarded.

- **`DELETE`**: Removes an existing entry entirely. This can be used to nullify a setting from another mod.

#### How to use `OverrideBehaviour`

**1. File/Entry Level**

You can set a default behaviour for an entire file. This is useful if your mod is intended to be a "base" or an "overwriter" for other settings.

*Example: A `QuestOverrides.jsonc` that merges its settings with others.*
```jsonc
{
    "OverrideBehaviour": "MERGE", // Can be "MERGE", "REPLACE", "IGNORE", "DELETE"
    "BlackListedQuests": [],
    "Overrides": [
        {
            "id": "5a27bb8386f7741c770d2d0a", // Wet Job - Part 1
            "whiteListedWeapons": ["some_new_sv98_mod"]
        }
    ]
}
```

You can also set it on a single entry to be more specific.

*Example: Replacing a single quest override while ignoring others.*
```jsonc
{
    // Default behaviour is IGNORE
    "Overrides": [
        {
            "id": "5a27bb8386f7741c770d2d0a", // Wet Job - Part 1
            "OverrideBehaviour": "REPLACE",
            "onlyUseWhiteListedWeapons": true,
            "whiteListedWeapons": ["my_special_m4"]
        }
    ]
}
```

**2. Value Level**

For some properties that are arrays (like `CustomCategories`, `CanBeUsedAs`, `CanBeUsedAsShortNameWhitelist`, `CanBeUsedAsShortNameBlacklist`), you can control the behavior of individual items within the array. This is done by replacing the simple value (e.g., a string) with an object containing `value` and `behaviour`.

*Example: Deleting a specific weapon from a `CanBeUsedAs` alias list defined by another mod.*
This `OverriddenWeapons.jsonc` assumes another mod has already defined that `weapon_A` can be used as `weapon_B`. We want to remove that alias, but keep other potential aliases for `weapon_A`.

```jsonc
{
    "OverrideBehaviour": "MERGE", // Merge with existing CanBeUsedAs
    "CanBeUsedAs": {
        "weapon_A": [
            {
                "value": "weapon_B",
                "behaviour": "DELETE"
            }
        ]
    }
}
```

*Example: Deleting a custom category defined by another mod.*
```jsonc
{
    "CustomCategories": [
        {
            "value": { "name": "AKM" }, // Only name is needed for deletion
            "behaviour": "DELETE"
        }
    ]
}
```

## For Mod Authors

To ensure your mod's weapons are correctly handled for quest requirements, you can create a `MissingQuestWeapons` directory inside your mod's root folder. Inside it, you can add:

1.  **`OverriddenWeapons.jsonc`**: To provide correct categories for your weapons if the automatic categorization is not sufficient.
2.  **`QuestOverrides.jsonc`**: If your mod adds or alters quests and you need to specify weapon requirements.

This mod will automatically detect and load these files.