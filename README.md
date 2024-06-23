# Add Missing Quest Weapon Requirements

This mod dynamically reads and applies overrides to the game's quest condition weapon requirements based on override files.

# <font color="red">Important

## Make sure this loads after any mod that register quests, otherwise they will not be processed. As far as I know currently there is only one mod that does this: [Virtual's Custom Quest Loader](https://hub.sp-tarkov.com/files/file/885-virtual-s-custom-quest-loader/)

</font>

- **Dynamic Override Reading**: The mod scans the game's mod directory for any overrides specified by the user or other mods in the `MissingQuestWeapons` folder. This mod's files are commented so you can check them to see how it works.

### Other mods can also add their overrides by adding **MissingQuestWeapons** folder to their root. (also anyone can just create a new folder in user/mods to save their custom overrides instead of using this mod's. This way they won't be overridden when the mod updates)

```
-- user/mods/<yourfolder>/
                        -- MissingQuestWeapons/
                            -- OverriddenWeapons.jsonc
                            -- QuestOverrides.jsonc
```

## How It Works

Upon the game's database loading, the mod initializes and reads its configuration. Then scans for every folder inside user/mods directory for overrides, processes and (and combines if same weapon/category exists multiple times) these overrides, and applies them to the quests. Each weapon condition of a quest is matched with the best matched weapon type and missing ones are added.

## Installation

Just with any other server mod unzip to your SPT root folder.

## Logging

The mod by defaults only logs to a file inside its directory called log.log. You can check this file to which quests are updated. If there is a problem with the mod be sure to send this file.
