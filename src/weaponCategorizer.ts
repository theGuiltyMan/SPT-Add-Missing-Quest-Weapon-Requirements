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
        const matches = (item: ITemplateItem, regexes: string[], alsoDesc: boolean) => 
        {
            const name = this.localeHelper.getName(item._id);
            const desc = this.localeHelper.getDescription(item._id);
            for (const regex of regexes) 
            {
                if (name.match(regex) || (alsoDesc && desc.match(regex)))
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
        this.logger.log("\n\n ###############  Prepared weapon types:  ################\n\n")
        this.logger.log(this.weaponsTypes)
        this.logger.logDebug("\n\n ###############  Prepared weapon to type:  ################\n\n")
        this.logger.logDebug(this.weaponToType)
        this.logger.logDebug("\n\n ###############################\n\n")

        dependecyContainer.registerInstance<Record<string, string[]>>("WeaponTypes", this.weaponsTypes);
        dependecyContainer.registerInstance<Record<string, string[]>>("WeaponToType", this.weaponToType);
    }
}