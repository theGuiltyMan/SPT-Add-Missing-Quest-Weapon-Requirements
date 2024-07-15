import {inject, injectable} from "tsyringe";
import {LogHelper, LogType} from "./util/logHelper";
import {ITemplateItem} from "@spt/models/eft/common/tables/ITemplateItem";
import {DatabaseServer} from "@spt/servers/DatabaseServer";
import {LocaleHelper} from "./util/localeHelper";
import {pushIfNotExists} from "./util/misc";
import {IAddMissingQuestRequirementConfig} from "./models/ConfigFiles/IAddMissingQuestRequirementConfig";
import {IOverrides} from "./models/Overrides/IOverrides";
import {ItemRepository, ItemType} from "./itemRepository";
import {ItemOverrides} from "./models/ConfigFiles/ItemOverrides";


abstract class ItemCategorizer 
{
    public readonly canBeUsedAs: Record<string, string[]> = {};
    public readonly customCategories: Set<string> = new Set<string>();
    public readonly itemToType: Record<string, string[]> = {};
    public readonly itemTypes: Record<string, string[]> = {};


    protected constructor(
        // @inject("LogHelper") protected logger: LogHelper,
        // @inject("DatabaseServer") protected databaseServer: DatabaseServer,
        // @inject("AMQRConfig") protected config: IAddMissingQuestRequirementConfig,
        // @inject("OverridedSettings") protected overridedSettings: IOverrides,
        // @inject("LocaleHelper") protected localeHelper: LocaleHelper,
        // @inject("ItemRepository") protected itemRepository: ItemRepository
    ) 
    {
    }

    public run(): void 
    {
        this.prepare();
        this.process();
        this.finalize();
    }

    protected abstract finalize(): void;

    protected abstract process(): void;

    protected abstract prepare(): void;
}

@injectable()
export class WeaponCategorizer extends ItemCategorizer 
{
    private overrides: ItemOverrides;
    private readonly _weaponIds: string[] = [];

    constructor(
        @inject("LogHelper") protected logger: LogHelper,
        @inject("DatabaseServer") protected databaseServer: DatabaseServer,
        @inject("AMQRConfig") protected config: IAddMissingQuestRequirementConfig,
        @inject("OverridedSettings") protected overridedSettings: IOverrides,
        @inject("LocaleHelper") protected localeHelper: LocaleHelper,
        @inject("ItemRepository") protected itemRepository: ItemRepository
    ) 
    {
        super();
    }

    protected prepare(): void 
    {
        this.overrides = this.overridedSettings.weaponOverrides;
        this.logger.log("WeaponCategorizer created");
    }

    protected process(): void 
    {
        const allItems = this.itemRepository.allItems;
        for (const id in allItems) 
        {
            const item = allItems[id];
            this.processItem(item)
        }
        this.logger.log(`Found ${this._weaponIds.length} weapons`);
        this.processShortNames()
        this.processCategories();
        this.finalizeCanBeUsedAs();
    }

    protected finalize(): void 
    {
        this.logger.log("\n\n ###############  Can be used as weapons:  ################\n\n")
        this.logger.log(this.overrides.CanBeUsedAs)
        this.logger.log("\n\n ###############  Prepared weapon types:  ################\n\n")
        this.logger.log(this.itemTypes)
        this.logger.logDebug("\n\n ###############  Prepared weapon to type:  ################\n\n")
        this.logger.logDebug(this.itemToType)
        this.logger.logDebug("\n\n ###############################\n\n")
    }

    private processItem(item: ITemplateItem): void 
    {
        const [b, itemType] = this.itemRepository.tryGetType(item)

        // this.logger.log(`Processing ${item._id}: isItem:${b} - ${itemType?.base} \\ ${itemType?.type}` );
        
        if (!b || !this.countAsWeapon(itemType)) 
        {
            return;
        }
        this._weaponIds.push(item._id);

        this.addToWeaponType(itemType.type, item);
    }

    private countAsWeapon(itemType: ItemType): boolean 
    {
        return itemType.base === "Weapon";
    }

    private addToCanBeUsedAs(id0: string, id1: string) 
    {
        if (!this.canBeUsedAs[id0]) 
        {
            this.canBeUsedAs[id0] = [];
        }
        pushIfNotExists(this.canBeUsedAs[id0], id1);
    }

    private matches(item: ITemplateItem, regexes: string[], alsoDesc: boolean) 
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

    private processCategories() 
    {
        for (const k in this.overrides.CustomCategories) 
        {
            try 
            {
                this.customCategories.add(this.overrides.CustomCategories[k].name);
                this.logger.log("Processing Custom Category");
                this.logger.log(this.overrides.CustomCategories[k]);
                const customCategory = this.overrides.CustomCategories[k];
                this.logger.log(`Processing Custom Category:  ${customCategory.name}`);
                const potentials: ITemplateItem[] = [];
                this.logger.plusIndent();
                for (const id of this._weaponIds) 
                {
                    this.logger.minusIndent();
                    const item = this.itemRepository.getItem(id);
                    this.logger.logDebug(`Processing ${item._id}`, LogType.NONE, true);
                    this.logger.plusIndent();
                    if (customCategory?.ids.includes(id)) 
                    {
                        this.logger.logDebug(`Adding ${item._id} to ${customCategory.name}. Found in ids.`);
                        potentials.push(item);
                        continue;
                    }

                    if (customCategory?.blackListedKeywords?.length > 0) 
                    {
                        if (this.matches(item, Array.from(customCategory.blackListedKeywords), customCategory.alsoCheckDescription)) 
                        {
                            this.logger.logDebug(`Skipping ${item._id} from ${customCategory.name}. Found in blacklisted keywords.`);
                            continue;
                        }
                    }

                    if (customCategory?.whiteListedKeywords?.length > 0) 
                    {
                        if (!this.matches(item, Array.from(customCategory.whiteListedKeywords), customCategory.alsoCheckDescription)) 
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

    }

    private finalizeCanBeUsedAs() 
    {
        for (let i = Object.keys(this.canBeUsedAs).length - 1; i >= 0; --i) 
        {
            const key = Object.keys(this.canBeUsedAs)[i];
            const value = this.canBeUsedAs[key];
            // process custom categories
            for (const v of value) 
            {
                if (this.itemTypes[v]) 
                {
                    for (const id of this.itemTypes[v]) 
                    {
                        if (id === key) continue;
                        pushIfNotExists(this.canBeUsedAs[key], id);
                    }
                }
            }

            // add to each other
            for (const v of value) 
            {
                if (!this.canBeUsedAs[v]) 
                {
                    this.canBeUsedAs[v] = [];
                }
                pushIfNotExists(this.canBeUsedAs[v], key);
                for (const id of value) 
                {
                    if (id === v) continue;
                    pushIfNotExists(this.canBeUsedAs[v], id);
                }
            }
        }
    }


    private addWeaponType(weaponType: string, itemId: string) 
    {
        if (!this.itemTypes[weaponType]) 
        {
            this.itemTypes[weaponType] = [];
        }

        pushIfNotExists(this.itemTypes[weaponType], itemId);
        if (!this.itemToType[itemId]) 
        {
            this.itemToType[itemId] = [];
        }
        pushIfNotExists(this.itemToType[itemId], weaponType);
    }

    private addToWeaponType(weaponType: string, item: ITemplateItem) 
    {
        const itemId = item._id;
        if (!weaponType || !itemId || this.config.BlackListedWeaponsTypes.includes(weaponType) || this.config.BlackListedItems.includes(itemId)) 
        {
            return;
        }


        if (this.overrides.Override[itemId]) 
        {
            this.overrides.Override[itemId].split(",").map(w => w.trim()).forEach(w => this.addWeaponType(w, itemId));
            return;
        }
        // check if the weapon is a more restrive type (e.g. bolt action, pump action, etc)
        switch (weaponType) 
        {
            case "Shotgun":
                // until i find a better way to categorize shotguns
                if (this.localeHelper.getName(itemId).includes("pump")) 
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
    }

    private processShortNames() 
    {
        const allShortNames = this._weaponIds.map(id => [id, this.localeHelper.getShortName(id)]);
        // can be used to find similar names
        const whiteList = this.overrides.CanBeUsedAsShortNameWhitelist;
        for (let i0 = 0; i0 < allShortNames.length; i0++) 
        {
            const name = allShortNames[i0][1];
            if (this.overrides.CanBeUsedAsShortNameBlacklist.includes(name)) 
            {
                continue;
            }
            for (let i1 = i0 + 1; i1 < allShortNames.length; i1++) 
            {
                const name2 = allShortNames[i1][1];
                if (this.overrides.CanBeUsedAsShortNameBlacklist.includes(name2)) 
                {
                    continue;
                }
                if (name === name2) 
                {
                    // this.logger.log(`${allShortNames[i0][0]} - ${allShortNames[i1][0]}`);
                    this.addToCanBeUsedAs(allShortNames[i0][0], allShortNames[i1][0]);
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
                    this.addToCanBeUsedAs(allShortNames[i0][0], allShortNames[i1][0]);
                }

            }
        }
    }


}
