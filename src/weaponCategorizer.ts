import { DependencyContainer, inject, injectable } from "tsyringe";
import { LogHelper, LogType } from "./util/logHelper";
import { ITemplateItem } from "@spt-aki/models/eft/common/tables/ITemplateItem";
import { IAddMissingQuestRequirementConfig } from "./models/IAddMissingQuestRequirementConfig";
import { OverridedSettings } from "./models/OverridedSettings";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";
import { LocaleHelper } from "./util/localeHelper";
import { pushIfNotExists } from "./util/misc";

@injectable()
export  class WeaponCategorizer 
{
    private allItems: Record<string, ITemplateItem>;     
    private weaponsTypes: Record<string, string[]>  = {};
    private weaponToType: Record<string, string[]> = {};
    private locale: Record<string, string>;

    constructor(
        @inject("LogHelper") protected logger: LogHelper,
        @inject("DatabaseServer") protected databaseServer: DatabaseServer,
        @inject("AMQRConfig") protected config: IAddMissingQuestRequirementConfig,
        @inject("OverridedSettings") protected overridedSettings: OverridedSettings,
        @inject("LocaleHelper") protected localeHelper: LocaleHelper
    )
    {
        this.allItems = this.databaseServer.getTables().templates.items;
        this.locale = this.databaseServer.getTables().locales.global["en"];

        logger.log("WeaponCategorizer created");
    }

    // todo: make it more generic to support more than just weapons
    private countAsWeapon = (name: string) : number => 
    {
        return name == "Weapon" ? 0 :  (name == "ThrowWeap" || name == "Knife" || name == "Launcher") ? 1  : -1;
    }

    private getWeaponType = (item: ITemplateItem) : string =>
    {
        if (!item._parent)
        {
            return null;
        }
        switch (this.countAsWeapon(this.allItems[item._parent]._name))
        {
            case 1:
                return this.allItems[item._parent]._name;
            case 0:
                return item._name;
            case -1:
                return this.getWeaponType(this.allItems[item._parent]);
        }
    }

    private getWeapClass = (item: ITemplateItem) : string  => 
    {
    
        if (item._type !== "Item" || !item._props) 
        {
            return null;
        }
    
        return this.getWeaponType(item);
    }

    private addWeaponType = (weaponType : string, itemId: string) => 
    {
        if (!this.weaponsTypes[weaponType]) 
        {
            this.weaponsTypes[weaponType] = [];
        }
            
        pushIfNotExists(this.weaponsTypes[weaponType],itemId);
        if (!this.weaponToType[itemId]) 
        {
            this.weaponToType[itemId] = [];
        }
        pushIfNotExists(this.weaponToType[itemId],weaponType);
    }

    private addToWeaponType = (weaponType: string, item: ITemplateItem) => 
    {
        const itemId = item._id;
        if (!weaponType || !itemId || this.config.BlackListedWeaponsTypes.includes(weaponType) || this.config.BlackListedItems.includes(itemId))
        {
            return;
        }
    

    
        if (this.overridedSettings.overriddenWeapons[itemId]) 
        {
            this.overridedSettings.overriddenWeapons[itemId].split(",").map(w=>w.trim()).forEach(w=>this.addWeaponType(w, itemId));
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
                        this.addWeaponType(weaponType, itemId);
                    }
                    weaponType = "PumpActionShotgun";
                }
                break;
            case "SniperRifle":
                if (item._props.BoltAction)
                {
                    if (this.config.categorizeWithLessRestrive)
                    {
                        this.addWeaponType(weaponType, itemId);
                    }
                    weaponType = "BoltActionSniperRifle";
                }
                break;
            case "Revolver":
                if (this.config.categorizeWithLessRestrive)
                {
                    this.addWeaponType("Pistol", itemId);
                }
                break;
            default:
                break;
        }
    
    
        this.addWeaponType(weaponType, itemId);
    };
    run(dependecyContainer :DependencyContainer):void
    {
        for (const itemId in this.allItems) 
        {
            try 
            {
                const item = this.allItems[itemId];
                if (item._type !== "Item" || !item._props) 
                {
                    continue;
                }


                this.addToWeaponType(this.getWeapClass(item), item);
            }
            catch (e)
            {
                this.logger.error(`Error processing ${itemId}: ${e}`);
            }
        }

        const allWeaponIds = Object.keys(this.weaponToType);
        const addToCanBeUsedAs = (id0 : string, id1: string) => 
        {
            if (!this.overridedSettings.canBeUsedAs[id0])
            {
                this.overridedSettings.canBeUsedAs[id0] = [];
            }
            pushIfNotExists(this.overridedSettings.canBeUsedAs[id0],id1);
        }
        
        const allShortNames = allWeaponIds.map(id => [id, this.localeHelper.getShortName(id)]);
        // can be used to find similar names
        const whiteList = this.overridedSettings.canBeUsedAsShortNameWhitelist;
        for (let i0 = 0; i0 < allShortNames.length; i0++) 
        {
            const name = allShortNames[i0][1];
            if (this.overridedSettings.canBeUsedAsShortNameBlackList.includes(name))
            {
                continue;
            }
            for (let i1 = i0 + 1; i1 < allShortNames.length; i1++) 
            {
                const name2 = allShortNames[i1][1];
                if (this.overridedSettings.canBeUsedAsShortNameBlackList.includes(name2))
                {
                    continue;
                }
                if (name === name2)
                {
                    // this.logger.log(`${allShortNames[i0][0]} - ${allShortNames[i1][0]}`);
                    addToCanBeUsedAs(allShortNames[i0][0], allShortNames[i1][0]);
                    continue;
                }
                const s0 = name.split(" ");
                const s1 = name2.split(" ");
                if (s0.length === 1 && s1.length === 1)
                {
                    continue;
                }
                
                let same = true;

                for (const w of s0) 
                {
                    if (!s1.includes(w) && !whiteList.includes(w))
                    {
                        same = false;
                        break;
                    }
                }

                if (!same)
                {
                    same = true;
                    for (const w of s1) 
                    {
                        if (!s0.includes(w) && !whiteList.includes(w))
                        {
                            same = false;
                            break;
                        }
                    }
                }

                if (same)
                {
                    // this.logger.log(`${allShortNames[i0][0]} - ${allShortNames[i1][0]}`);
                    addToCanBeUsedAs(allShortNames[i0][0], allShortNames[i1][0]);
                    continue;
                }

            }
        }


        const matches = (item: ITemplateItem, regexes: string[], alsoDesc: boolean) => 
        {
            const name = this.localeHelper.getName(item._id);
            const desc = this.localeHelper.getDescription(item._id);
            for (const regex of regexes) 
            {
                
                if (name.match(new RegExp(regex, "i")) || (alsoDesc && desc.match(new RegExp(regex, "i"))))
                {
                    return true;
                }
            }
            return false;
        }
        // process custom categories
        for (const k in this.overridedSettings.customCategories) 
        {

            try 
            {

                this.logger.log("Processing Custom Category");
                this.logger.log(this.overridedSettings.customCategories[k]);
                const customCategory = this.overridedSettings.customCategories[k];
                this.logger.log(`Processing Custom Category:  ${customCategory.name}`);
                const potentials : ITemplateItem[] = [];
                this.logger.plusIndent();
                for (const id of allWeaponIds) 
                {
                    this.logger.minusIndent();
                    const item = this.allItems[id];
                    this.logger.logDebug(`Processing ${item._id}`,LogType.NONE, true);
                    this.logger.plusIndent();
                    if (customCategory?.ids.includes(id))
                    {
                        this.logger.logDebug(`Adding ${item._id} to ${customCategory.name}. Found in ids.`);
                        potentials.push(item);
                        continue;
                    }

                    if (customCategory?.blackListedKeywords?.length > 0)
                    {
                        if (matches(item, Array.from(customCategory.blackListedKeywords), customCategory.alsoCheckDescription))
                        {
                            this.logger.logDebug(`Skipping ${item._id} from ${customCategory.name}. Found in blacklisted keywords.`);
                            continue;
                        }
                    }

                    if (customCategory?.whiteListedKeywords?.length > 0)
                    {
                        if (!matches(item, Array.from(customCategory.whiteListedKeywords), customCategory.alsoCheckDescription))
                        {
                            this.logger.logDebug(`Skipping ${item._id} from ${customCategory.name}. Not found in whitelisted keywords.`);
                            continue;
                        }
                    }

                    if (customCategory?.allowedCalibres?.length > 0)
                    {
                        if (!item._props.ammoCaliber || !customCategory.allowedCalibres.includes(item._props.ammoCaliber))
                        {
                            this.logger.logDebug(`Skipping ${item._id} from ${customCategory.name}. Not found in allowed calibres.`);
                            continue;
                        }
                    }

                    potentials.push(item);
                }

                this.logger.minusIndent();
                for (const item of potentials) 
                {
                    this.logger.log(`Adding ${item._id} to ${customCategory.name}`);
                    this.addWeaponType(customCategory.name, item._id);
                }


            }
            catch (e)
            {
                this.logger.error(`Error processing custom category ${k}: ${e}`);
        
            }
        }
        // process can be used as once more 

        for (let i = Object.keys(this.overridedSettings.canBeUsedAs).length -1; i >=0 ; --i) 
        {
            const key = Object.keys(this.overridedSettings.canBeUsedAs)[i];
            const value = this.overridedSettings.canBeUsedAs[key];
            // process custom categories
            for (const v of value) 
            {
                if (this.weaponsTypes[v])
                {
                    for (const id of this.weaponsTypes[v]) 
                    {
                        if (id === key) continue;
                        pushIfNotExists(this.overridedSettings.canBeUsedAs[key],id);
                    }
                }
            }

            // add to each other
            for (const v of value) 
            {
                if (!this.overridedSettings.canBeUsedAs[v])
                {
                    this.overridedSettings.canBeUsedAs[v] = [];
                }
                pushIfNotExists(this.overridedSettings.canBeUsedAs[v],key);
                for (const id of value) 
                {
                    if (id === v) continue;
                    pushIfNotExists(this.overridedSettings.canBeUsedAs[v],id);
                }
            }
        }

        this.logger.log("\n\n ###############  Can be used as weapons:  ################\n\n")
        this.logger.log(this.overridedSettings.canBeUsedAs)
        this.logger.log("\n\n ###############  Prepared weapon types:  ################\n\n")
        this.logger.log(this.weaponsTypes)
        this.logger.logDebug("\n\n ###############  Prepared weapon to type:  ################\n\n")
        this.logger.logDebug(this.weaponToType)
        this.logger.logDebug("\n\n ###############################\n\n")

        dependecyContainer.registerInstance<Record<string, string[]>>("WeaponTypes", this.weaponsTypes);
        dependecyContainer.registerInstance<Record<string, string[]>>("WeaponToType", this.weaponToType);
    }
}
