import { inject, injectable } from "tsyringe";
import { IAddMissingQuestRequirementConfig } from "./models/IAddMissingQuestRequirementConfig";
import { LogHelper, LogType } from "./util/logHelper";
import { WeaponCategorizer } from "./weaponCategorizer";
import { IQuest } from "@spt-aki/models/eft/common/tables/IQuest";
import { ITemplateItem } from "@spt-aki/models/eft/common/tables/ITemplateItem";
import { LogBackgroundColor } from "@spt-aki/models/spt/logging/LogBackgroundColor";
import { LogTextColor } from "@spt-aki/models/spt/logging/LogTextColor";
import path from "path";
import { stringify } from "querystring";
import { IOverriddenWeapons } from "./models/IOverriddenWeapons";
import { IQuestOverride } from "./models/IQuestOverride";
import { IQuestOverrides } from "./models/IQuestOverrides";
import { IWeaponCategory } from "./models/IWeaponCategory";
import { tryReadJson } from "./util/jsonHelper";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";

@injectable()
export class QuestPatcher
{
    constructor(
        @inject("LogHelper") protected logHelper: LogHelper,
        @inject("AMQRConfig") protected config: IAddMissingQuestRequirementConfig,
        @inject("WeaponCategorizer") protected weaponCategorizer: WeaponCategorizer,
        @inject("DatabaseServer") protected databaseServer: DatabaseServer
    ) 
    {
        
    }

    
    private quests : Record<string, IQuest> = {};
    private allItems : Record<string, ITemplateItem> = {};
    private locale : Record<string, string> = {};

    run():void
    {
        try 
        {
            throw new Error("Not implemented");
            const db = this.databaseServer.getTables();
            this.quests = db.templates.quests;
            this.allItems = db.templates.items;
            this.locale = db.locales.global["en"]; // we are using shotguns' names for determining the type of shotgun, so we need to use the english locale (for now)
            // categorize all weapons based on their parent
            // Read in the json c config content and parse it into json
            const weaponsTypes = {};
            const weaponToType: Record<string, string[]> = {};

            // search all mods for MissingQuestWeapons folders
            // -- MissingQuestWeapons
            // ---- QuestOverrides.json(c)
            // ---- OverriddenWeapons.json(c)
    
            

            // #region config parsing
            
            // #endregion
        
            this.addToLog("^%%%%%%%%%%%")
            this.addToLog(`Blacklisted Quests: ${Object.values(questOverrides).filter(q=>q.skip).map(q=>q.id).join(", ")}`);
            this.addToLog(`Overridden Weapons: ${JSON.stringify(overriddenWeapons, null, 4)}`);
            this.addToLog(`CanBeUsedAs: ${JSON.stringify(canBeUsedAs)}`);
            this.addToLog(`CanBeUsedAs: ${stringify(canBeUsedAs)}`);
            this.addToLog(`Custom Categories: ${JSON.stringify(customCategories)}`);
            //#region weapon categorization methods
            const countAsWeapon = (name: string) : number => 
            {
                return name == "Weapon" ? 0 :  (name == "ThrowWeap" || name == "Knife" || name == "Launcher") ? 1  : -1;
            }
            const getWeaponType = (item: ITemplateItem) : string =>
            {
                if (!item._parent)
                {
                    return null;
                }
                switch (countAsWeapon(this.allItems[item._parent]._name))
                {
                    case 1:
                        return this.allItems[item._parent]._name;
                    case 0:
                        return item._name;
                    case -1:
                        return getWeaponType(this.allItems[item._parent]);
                }
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
                    if (!weaponToType[itemId]) 
                    {
                        weaponToType[itemId] = [];
                    }
                    weaponToType[itemId].push(weaponType);
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
                        if (this.locale[`${itemId} Name`].includes("pump"))
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
                //#endregion

            for (const itemId in this.allItems) 
            {
                const item = this.allItems[itemId];
                if (item._type !== "Item" || !item._props) 
                {
                    continue;
                }

                addToWeaponType(getWeapClass(item), item);
            }

            // debug
            {
                const debugJson = {}
                for (const type in weaponsTypes) 
                {
                    debugJson[type] = weaponsTypes[type].map((w: string)=> this.getPrintable(w));
                }

       
                this.addToLog(JSON.stringify(debugJson, null, 4));
            }

            const canCountAs = (weaponId: string, weaponType: string) : boolean => 
            {
                return weaponToType[weaponId] && weaponToType[weaponId].includes(weaponType);
            }

            // iterate through all quests
            for (const questId in this.quests) 
            {
                if (questOverrides[questId] && questOverrides[questId].skip)
                {
                    continue;
                }
                const quest = this.quests[questId];
        
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
                            let weaponsChangesLog = "";
                            if (!(questOverrides[questId] && questOverrides[questId].onlyUseWhiteListedWeapons))
                                
                            {
                                let weaponType = null;
                                // select the most restrictive weapon type if all weapons are of the same type
                                // for example if all weapons are revolvers, then the type is revolver
                                // but if there are revolvers and pistols, then the type is pistol

                                const potentialTypes : Record<string, string[]> = {};
                                for (const weaponId of counterCondition.weapon) 
                                {
                                    if (!weaponToType[weaponId] || weaponToType[weaponId].length === 0) 
                                    {
                                        this.logger.error(`Weapon (${this.getPrintable(weaponId)}) not found in weaponToType for quest ${this.getPrintable(questId)}`);
                                        break;
                                    }

                                    for (const w of weaponToType[weaponId]) 
                                    {
                                        if (!potentialTypes[w]) 
                                        {
                                            potentialTypes[w] = [];
                                        }
                                        potentialTypes[w].push(weaponId);
                                        if (canBeUsedAs[weaponId])
                                        {
                                            canBeUsedAs[weaponId].forEach(v=>potentialTypes[w].push(v));
                                        }
                                    }
                                }

                       
                                let bestCandidate = null;
                                // check if there is a weapon type that all weapons are of
                                for (const w in potentialTypes) 
                                {
                                    if (potentialTypes[w].length === counterCondition.weapon.length) 
                                    {
                                        if (weaponType == null)
                                        {
                                            weaponType = w;
                                        }
                                        else 
                                        {
                                            // if there are multiple types, then select the most restrictive
                                            if (this.config.kindOf[w] == weaponType)
                                            {
                                                weaponType = w;
                                            }
                                        }
                                    }
                                    else if (counterCondition.weapon.length - potentialTypes[w].length === 1)
                                    {
                                        bestCandidate = {
                                            type: w,
                                            weapons: potentialTypes[w],
                                            missing: counterCondition.weapon.filter(i=>!potentialTypes[w].includes(i))
                                        };
                                    }
                                }

                                if (weaponType == null && bestCandidate != null)
                                {
                                    this.addToLog(`Quest ${this.logHelper.getPrintable(questId)} best candidate: \n Type: ${bestCandidate.type} \n Weapons: ${bestCandidate.weapons.map((w: string)=> this.getPrintable(w)).join(", ")} \n Missing: ${bestCandidate.missing.map(w=> this.getPrintable(w)).join(", ")}`);
                                }
                                const lastEntry = counterCondition.weapon.length;
                                if (weaponType)
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
                                    if (lastEntry !== counterCondition.weapon.length)
                                    {
                                        weaponsChangesLog += `Added ${counterCondition.weapon.length - lastEntry} weapons of type ${weaponType}: ${counterCondition.weapon.slice(lastEntry).map(id => this.getPrintable(id)).join(", ")} \n`;
                                    }
                                }
                            }
                            else 
                            {
                                {
                                    this.addToLog(`Only adding whiteListedWeapons to ${this.getPrintable(questId)}`);
                                    break;
                                }
                            }

                            // remove the blacklisted weapons

                            if (questOverrides[questId] && questOverrides[questId].blackListedWeapons?.size > 0)
                            {
                                for (const w of questOverrides[questId].blackListedWeapons) 
                                {
                                    const index = counterCondition.weapon.indexOf(w);
                                    if (index !== -1) 
                                    {
                                        counterCondition.weapon.splice(index, 1);
                                        weaponsChangesLog += `Removed blacklisted weapon: ${this.getPrintable(w)} \n`;
                                    }
                                }
                            }

                            if (questOverrides[questId] && questOverrides[questId].whiteListedWeapons?.size > 0)
                            {
                            // add the white listed weapons
                                for (const w of questOverrides[questId].whiteListedWeapons) 
                                {
                                    if (counterCondition.weapon.indexOf(w) === -1) 
                                    {
                                        counterCondition.weapon.push(w);
                                        weaponsChangesLog += `Added whilelisted weapon ${this.getPrintable(w)} \n`;
                                    }
                                }
                            }

                            if (weaponsChangesLog.length > 0)
                            {
                                this.addToLog(`Quest ${this.getPrintable(questId)} \n ${weaponsChangesLog}`);
                            }
                        
                        }
                    }
                })            
            }    
        }
        catch (e)
        {
            // todo: change name
            this.logger.error("An error occurred in MissingQuestWeapons mod. Please check the 'log.log' file in the mod directory () for more information.")
            //todo
            // replace string

            
            // this.addToLog(e.stack, LogType.FILE);
            this.addToLog(`${(e.stack as string).replaceAll("S:\\Games\\SPT\\user\\mods\\addmissingquestrequirements\\src", 
                "X:\\Projects\\SPT-DEV\\Mods\\AddMissingQuestRequirements\\src")}`, LogType.FILE);
            // this.logger.logWithColor("ASD", LogTextColor.CYAN, LogBackgroundColor.MAGENTA);
            this.logger.error(`${(e.stack as string).replaceAll("S:\\Games\\SPT\\user\\mods\\addmissingquestrequirements\\src", 
                "X:\\Projects\\SPT-DEV\\Mods\\AddMissingQuestRequirements\\src")}`, LogTextColor.CYAN, LogBackgroundColor.MAGENTA);
            this.logToFile(true);
            // throw e; //todo
            return
        }
        this.logToFile();
    }
}