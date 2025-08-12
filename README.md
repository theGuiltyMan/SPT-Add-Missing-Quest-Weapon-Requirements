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
                "\b(AKM|AK-1|VPO|Draco)\w*"
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

## For Mod Authors

To ensure your mod's weapons are correctly handled for quest requirements, you can create a `MissingQuestWeapons` directory inside your mod's root folder. Inside it, you can add:

1.  **`OverriddenWeapons.jsonc`**: To provide correct categories for your weapons if the automatic categorization is not sufficient.
2.  **`QuestOverrides.jsonc`**: If your mod adds or alters quests and you need to specify weapon requirements.

This mod will automatically detect and load these files.