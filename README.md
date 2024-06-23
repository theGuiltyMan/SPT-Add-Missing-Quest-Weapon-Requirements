# Add Missing Quest Weapon Requirements

This mod dynamically reads and applies overrides to the game's quest conditions' weapon requirements based on override files. Normally, the conditions' weapons need to be manually updated for every added gun. This mod aims to automate this process.

# <font color="red">Important

## Make sure this loads after any mod that registers quests; otherwise, they will not be processed. As far as I know, currently there is only one mod that does this:  [Virtual's Custom Quest Loader](https://hub.sp-tarkov.com/files/file/885-virtual-s-custom-quest-loader/)

</font>

- **Dynamic Override Reading**: The mod scans the game's mod directory for any overrides specified by the user or other mods in the `MissingQuestWeapons` folder. The mod's files are commented, so you can check them to see how it works.

### Other mods can also add their overrides by adding a **MissingQuestWeapons** folder to their root. (Anyone can also create a new folder in user/mods to add/save their custom overrides instead of adding them to this mod's. This way, they won't be overridden when the mod updates.)

```
-- user/mods/<yourfolder>/
                        -- MissingQuestWeapons/
                            -- OverriddenWeapons.jsonc
                            -- QuestOverrides.jsonc
```

## How It Works

Upon the game's database loading, the mod initializes and reads its configuration. It then scans every folder inside the user/mods directory for overrides, processes and combines them (if the same weapon/category exists multiple times), and applies them to the quests. Each weapon condition of a quest is matched with the best-matched weapon type, and missing ones are added.

## Installation

As with any other server mod, unzip to your SPT root folder.

## Logging

By default, logging is only done to a file inside its directory called "log.log". ou can check this file to see how weapons are categorized and quests are updated. In the config, you can set _debug_ to increase the details that are logged. If there is a problem with the mod, be sure to send this file.
