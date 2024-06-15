import { DependencyContainer } from "tsyringe";
import { jsonc } from "jsonc";
import path from "path";
import { ITemplateItem } from "@spt-aki/models/eft/common/tables/ITemplateItem";
import { IPostDBLoadMod } from "@spt-aki/models/external/IPostDBLoadMod";
import { ILogger } from "@spt-aki/models/spt/utils/ILogger";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";
import { LogTextColor } from "@spt-aki/models/spt/logging/LogTextColor";
import { LogBackgroundColor } from "@spt-aki/models/spt/logging/LogBackgroundColor";
import { VFS } from "@spt-aki/utils/VFS";
import { log } from "console";

class Mod implements IPostDBLoadMod 
{
    private databaseServer: DatabaseServer;
    private vfs: VFS;
    private logger: ILogger;
    private config: any;

    public postDBLoad(container: DependencyContainer): void 
    {
    // Database will be loaded, this is the fresh state of the DB so NOTHING from the AKI
    // logic has modified anything yet. This is the DB loaded straight from the JSON files
        this.databaseServer = container.resolve<DatabaseServer>("DatabaseServer");
        this.vfs = container.resolve<VFS>("VFS");
        this.logger = container.resolve<ILogger>("WinstonLogger");
        this.config = jsonc.parse(
            this.vfs.readFile(path.resolve(__dirname, "../config/config.jsonc"))
        );

        if (this.config.delay <= 0)
        {
            this.patchQuests();
        }
        else 
        {
            setTimeout(() => this.patchQuests(), this.config.delay * 1000);
        }
    }

    private patchQuests(): void
    {
        const db = this.databaseServer.getTables();
        const quests = db.templates.quests;
        const allItems = db.templates.items;
        // const locale = db.locales.global[this.config.logLocale];
        const locale = db.locales.global["en"]; // we are using shotguns' names for determining the type of shotgun, so we need to use the english locale (for now)
        // categorize all weapons based on their parent
        // Read in the json c config content and parse it into json
        const weaponsTypes = {};
        // todo add multiple types for a weapon (e.g. both sniper and bolt action, revolver and pistol, etc)
        const weaponToType: Record<string, string> = {};

        // search all mods for MissingQuestWeapons folders
        // -- MissingQuestWeapons
        // ---- BlackListedQuests.json(c) : string[]
        // ---- OverriddenWeapons.json(c) : Record<string, string>
        const blackListedQuests : string[] = [];
        const overriddenWeapons : Record<string, string> = {};

        const modsDirectory = path.resolve(__dirname, "../../");

        const readAndParseJson = (modDir: string, file: string) : any => 
        {
            const jsoncPath = path.resolve(modDir, `MissingQuestWeapons/${file}.jsonc`);
            const jsonPath = path.resolve(modDir, `MissingQuestWeapons/${file}.json`);
        
            let fileContent = null;
            if (this.vfs.exists(jsoncPath)) 
            {
                fileContent = jsonc.parse(this.vfs.readFile(jsoncPath));
            }
            else if (this.vfs.exists(jsonPath)) 
            {
                fileContent = JSON.parse(this.vfs.readFile(jsonPath));
            }
            return fileContent;
        }

        this.vfs.getDirs(modsDirectory).map(m=>path.resolve(modsDirectory, m))
            .filter(modDir => this.vfs.exists(path.resolve(modDir, "MissingQuestWeapons")))
            .forEach(modDir => 
            {
            
                let json = readAndParseJson(modDir, "BlackListedQuests");
                if (json) 
                {
                    json.forEach((v) => 
                    {
                        if (!blackListedQuests.includes(v)) 
                        {
                            blackListedQuests.push(v);
                        }
                    });
                }

                json = readAndParseJson(modDir, "OverriddenWeapons");
                if (json) 
                {
                    for (const key in json) 
                    {
                        overriddenWeapons[key] = json[key];
                    }
                }
            
            })

        if (this.config.log)
        {
            this.logger.logWithColor(`Blacklisted Quests: ${blackListedQuests.join(", ")}`, LogTextColor.CYAN, LogBackgroundColor.MAGENTA);
                
            this.logger.logWithColor(`Overridden Weapons: ${JSON.stringify(overriddenWeapons, null, 4)}`, LogTextColor.CYAN, LogBackgroundColor.MAGENTA);
        }

        const getWeaponType = (item: ITemplateItem) : string =>
        {
            if (!item._parent)
            {
                return null;
            }
            return allItems[item._parent]._name === "Weapon" ? item._name : getWeaponType(allItems[item._parent])
        }

        const getWeapClass = (item: ITemplateItem) : string  => 
        {

            if (item._type !== "Item" || !item._props) 
            {
                return null;
            }

            return getWeaponType(item);
        }

        const addToWeaponType = (weaponType: string, item: ITemplateItem) => 
        {
            const itemId = item._id;
            if (!weaponType || !itemId || this.config.BlackListedWeaponsTypes.includes(weaponType) || this.config.BlackListedItems.includes(itemId))
            {
                return;
            }

            const add = (weaponType : string, itemId: string) => 
            {
                if (!weaponsTypes[weaponType]) 
                {
                    weaponsTypes[weaponType] = [];
                }
        
                weaponsTypes[weaponType].push(itemId);

                weaponToType[itemId] = weaponType;
            }

            if (overriddenWeapons[itemId]) 
            {
                overriddenWeapons[itemId].split(",").map(w=>w.trim()).forEach(w=>add(w, itemId));
                return;
            }
            // check if the weapon is a more restrive type (e.g. bolt action, pump action, etc)
            switch (weaponType)
            {
                case "Shotgun":
                    // until i find a better way to categorize shotguns

                    if (locale[`${itemId} Name`].includes("pump"))
                    {
                        if (this.config.categorizeWithLessRestrive)
                        {
                            add(weaponType, itemId);
                        }
                        weaponType = "PumpActionShotgun";
                    }
                    break;
                case "SniperRifle":
                    if (item._props.BoltAction)
                    {
                        if (this.config.categorizeWithLessRestrive)
                        {
                            add(weaponType, itemId);
                        }
                        weaponType = "BoltActionSniperRifle";
                    }
                    break;
                case "Revolver":
                    if (this.config.categorizeWithLessRestrive)
                    {
                        add("Pistol", itemId);
                    }
                    break;
                default:
                    break;
            }


            add(weaponType, itemId);
        };

        for (const itemId in allItems) 
        {
            const item = allItems[itemId];
            if (item._type !== "Item" || !item._props) 
            {
                continue;
            }

            addToWeaponType(getWeapClass(item), item);
        }

        if (this.config.log) 
        {
            const debugJson = {}
            for (const type in weaponsTypes) 
            {
                debugJson[type] = weaponsTypes[type].map(w=> `${locale[`${w} Name`]} (${w})`);
            }

       
            this.logger.logWithColor(JSON.stringify(debugJson, null, 4), LogTextColor.CYAN, LogBackgroundColor.MAGENTA);
        }

        // iterate through all quests
        for (const questId in quests) 
        {
            if (blackListedQuests.includes(questId))
            {
                continue;
            }
            const quest = quests[questId];
            // iterate through all conditions
            [quest.conditions.AvailableForStart, quest.conditions.Started, quest.conditions.AvailableForFinish, quest.conditions.Success, quest.conditions.Fail].forEach((conditions) =>
            {
                if (!conditions || !conditions.length) 
                {
                    return;
                }

                for (const condition of conditions) 
                {
                    if (!condition.counter )
                    {
                        continue;
                    }

                    for (const counterCondition of condition.counter.conditions) 
                    {
                        if (!counterCondition.weapon || !counterCondition.weapon.length) 
                        {
                            continue;
                        }

                        let typeCounter = 0;
                        let weaponType = null;

                        for (const weaponId of counterCondition.weapon) 
                        {
                            if (!weaponToType[weaponId]) 
                            {
                                continue;
                            }

                            const type = weaponToType[weaponId];
                            if (typeCounter === 0) 
                            {
                                weaponType = type;
                                typeCounter++;
                            } 
                            else if (weaponType !== type) 
                            {
                                weaponType = null;
                                typeCounter = 0
                                break;
                            }
                            else 
                            {
                                typeCounter++;
                            }
                        }

                        const lastEntry = counterCondition.weapon.length;
                        if (typeCounter >= this.config.amountNeededForWeaponType)
                        {
                            // add the missing weapons
                            for (const w of weaponsTypes[weaponType]) 
                            {
                                // probably just assign the array directly but just to be sure 
                                if (counterCondition.weapon.indexOf(w) === -1) 
                                {
                                    counterCondition.weapon.push(w);
                                }
                            }
                            if (this.config.log && lastEntry !== counterCondition.weapon.length)
                            {
                                this.logger.logWithColor(`Added missing weapons to quest ${questId} for weapon type ${weaponType}: ${counterCondition.weapon.slice(lastEntry).map(w=> locale[`${w} Name`]).join(", ")}`, LogTextColor.CYAN, LogBackgroundColor.MAGENTA);

                            }
                        }
                    }
                }
            })            
        }
    }
}

module.exports = { mod: new Mod() };
