import {VFS} from "@spt-aki/utils/VFS";
import path from "path";
import {DependencyContainer, inject, injectable} from "tsyringe";
import {ItemOverrides} from "./models/Overrides/ItemOverrides";
import {IQuestOverridesSettings} from "./models/ConfigFiles/IQuestOverridesSettings";
import {tryReadJson} from "./util/jsonHelper";
import {LogHelper, LogType} from "./util/logHelper";
import {IOverrides} from "./models/Overrides/IOverrides";
import {isArray, mergeWith, uniq} from "lodash";
import {QuestOverrides} from "./models/Overrides/QuestOverrides";

abstract class OverrideCombiner<TRead, TSetting> 
{

    protected abstract get fileName(): string;

    protected parsedOverride: TRead;
    protected data: TSetting;

    protected processed: boolean = false;

    protected constructor(
        protected logger: LogHelper) 
    {
        this.parsedOverride = {} as TRead;
    }

    public readOverrideInFolder(folder: string): void 
    {
        this.logger.log(`Reading ${this.fileName} in ${folder}`)
        const data = tryReadJson<TRead>(folder, this.fileName);
        // this.logger.log(JSON.stringify(data));
        if (!data) 
        {
            return;
        }
        mergeWith(this.parsedOverride, data, this.customCombine.bind(this));
        this.logger.log("AAA");
        // this.logger.log(JSON.stringify(this.parsedOverride));
    }

    protected customCombine(objValue: any, srcValue: any, key: string, object: any, source: any, stack: any): any 
    {
        if (isArray(objValue) && srcValue) 
        {
            return uniq(objValue.concat(srcValue));
        }
    }

    protected abstract processData(): void;

    public getData(): TSetting 
    {
        if (!this.processed) 
        {
            this.processData();
        }
        return this.data;

    }
}

class WeaponOverrideCombiner extends OverrideCombiner<ItemOverrides, ItemOverrides> 
{

    // TODO:currently the anything but array and objects are overwritten by the last mod
    //  low priority since high effort and low impact 
    
    // protected customCombine(objValue: any, srcValue: any, key: string, object: any, source: any, stack: any): any 
    // {
    //     return super.customCombine(objValue, srcValue, key, object, source, stack);
    // }

    protected processData(): void 
    {
        this.data = this.parsedOverride;

        for (let i = this.data.CanBeUsedAsShortNameBlacklist.length - 1; i >= 0; i--) 
        {
            const index = this.data.CanBeUsedAsShortNameWhitelist.indexOf(this.data.CanBeUsedAsShortNameBlacklist[i]);
            if (index !== -1) 
            {
                this.logger.warn(`ShortName Filter: ${this.data.CanBeUsedAsShortNameBlacklist[i]} is both blacklisted and whitelisted. Removing from whitelist.`);
                this.data.CanBeUsedAsShortNameWhitelist.splice(index, 1);
            }
        }
    }

    protected get fileName(): string 
    {
        return "OverriddenWeapons"
    }

    constructor(
        logger: LogHelper) 
    {
        super(logger);
        this.data = new ItemOverrides()
    }

}

class QuestOverrideCombiner extends OverrideCombiner<IQuestOverridesSettings, QuestOverrides> 
{
    protected get fileName(): string 
    {
        return "QuestOverrides"
    }

    protected customCombine(objValue: any, srcValue: any, key: string, object: any, source: any, stack: any): any 
    {
        const checkBoolConflict = (b0: boolean | undefined, b1: boolean | undefined): boolean | undefined => 
        {
            if (b0 === undefined) 
            {
                return b1;
            }
            if (b1 === undefined) 
            {
                return b0;
            }

            if (b0 !== b1) 
            {
                this.logger.error(`Quest ${object.id} has conflicting skip values: ${b0} and ${b1}. Using ${b1}`);
            }
            return srcValue;
        }
        if (key === "skip") 
        {
            return checkBoolConflict(objValue, srcValue);
        }
        if (key === "onlyUseWhiteListedWeapons") 
        {
            return checkBoolConflict(objValue, srcValue);
        }
        return super.customCombine(objValue, srcValue, key, object, source, stack);
    }

    protected processData(): void 
    {
        for (const v of this.parsedOverride.Overrides) 
        {
            this.data.questOverrides[v.id] = v;
            const questOverride = this.data.questOverrides[v.id];
            if (v.whiteListedWeapons) 
            {
                for (let i = v.whiteListedWeapons.length - 1; i >= 0; --i) 
                {
                    const w = v.whiteListedWeapons[i];
                    if (questOverride.blackListedWeapons.includes(w)) 
                    {
                        this.logger.error(`Weapon ${w} is both blacklisted and whitelisted for quest ${v.id}. Removing from whitelist.`);
                        v.whiteListedWeapons.splice(i, 1);
                    }
                }
            }
        }

        // add blacklisted quests
        for (const b of this.parsedOverride.BlackListedQuests) 
        {
            if (!this.data.questOverrides[b]) 
            {
                this.data.questOverrides[b] = {
                    id: b,
                    blackListed: true
                }
            }
            else 
            {
                this.logger.error(`Quest ${b} is both in blacklisted quests and in quest overrides. Blacklisting will take precedence.`);
                this.data.questOverrides[b].blackListed = true;
            }
        }
    }

    constructor(
        logger: LogHelper) 
    {
        super(logger);
        this.data = new QuestOverrides()
    }
}

@injectable()
export class OverrideReader 
{
    constructor(
        @inject("LogHelper") protected logger: LogHelper,
        @inject("VFS") protected vfs: VFS,
        @inject("modDir") protected modsDirectory: string
    ) 
    {
        logger.log("OverrideReader created");
    }

    public run(childContainer: DependencyContainer): void 
    {
        childContainer.registerInstance<IOverrides>("OverridedSettings", this.readOverrides());
    }

    private readOverrides(): IOverrides 
    {

        const questOverride = new QuestOverrideCombiner(this.logger);
        const weaponOverride = new WeaponOverrideCombiner(this.logger)
        this.logger.log("Reading overrides");
        this.vfs.getDirs(this.modsDirectory).map(m => path.resolve(this.modsDirectory, m))
            .filter(modDir => this.vfs.exists(path.resolve(modDir, "MissingQuestWeapons")))
            .forEach(modDir => 
            {
                this.logger.log(`Processing mod: ${path.basename(modDir)}`)
                this.logger.plusIndent();

                try 
                {
                    questOverride.readOverrideInFolder(path.resolve(modDir, "MissingQuestWeapons"));
                }
                catch (e) 
                {
                    this.logger.error(`Error reading QuestOverrides in ${modDir}: ${e.message}`);
                }

                try 
                {
                    weaponOverride.readOverrideInFolder(path.resolve(modDir, "MissingQuestWeapons"));
                }
                catch (e) 
                {
                    this.logger.error(`Error reading OverriddenWeapons in ${modDir}: ${e.message}`);
                }
                this.logger.minusIndent();

            })


        const overrides: IOverrides = {
            questOverrides: questOverride.getData(),
            weaponOverrides: weaponOverride.getData()
        };


        this.logger.log("##### Quest Overrides #####");
        this.logger.plusIndent();
        for (const key in overrides.questOverrides) 
        {
            this.logger.log(overrides.questOverrides[key]);
        }
        this.logger.minusIndent();

        this.logger.log("##### #####");

        this.logger.log("##### Overridden Weapons #####");
        this.logger.plusIndent();
        this.logger.log(overrides.weaponOverrides);
        this.logger.minusIndent();
        return overrides;
    }
}