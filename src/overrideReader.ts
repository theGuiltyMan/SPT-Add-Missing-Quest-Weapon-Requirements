import { FileSystemSync } from "@spt/utils/FileSystemSync";
import path from "path";
import { DependencyContainer, inject, injectable } from "tsyringe";
import { IOverriddenWeapons } from "./models/IOverriddenWeapons";
import { IQuestOverrides } from "./models/IQuestOverrides";
import { tryReadJson } from "./util/jsonHelper";
import { LogHelper } from "./util/logHelper";
import { OverridedSettings } from "./models/OverridedSettings";
import { pushIfNotExists } from "./util/misc";
import { OverrideBehaviour } from "./models/OverrideBehaviour";
import { Overridable } from "./models/Overridable";
import { IWeaponCategory } from "./models/IWeaponCategory";
import { IQuestOverride } from "./models/IQuestOverride";
import { IQuestOverrideSetting } from "./models/IQuestOverrideSetting";


@injectable()
export class OverrideReader 
{
    constructor(
        @inject("LogHelper") protected logger: LogHelper,
        @inject("FileSystemSync") protected vfs: FileSystemSync,
        @inject("modDir") protected modsDirectory: string
    ) 
    {
        logger.log("OverrideReader created");
    }

    public run(childContainer: DependencyContainer): void 
    {
        childContainer.registerInstance<OverridedSettings>("OverridedSettings", this.readOverrides());
    }
    private processOverridableArray<T>(target: T[], source: Overridable<T>[], defaultBehaviour: OverrideBehaviour) 
    {
        if (!source) return;
        source.forEach(item => 
        {
            const value = (item as any).value ?? item;
            const behaviour = (item as any).behaviour ?? defaultBehaviour;

            switch (behaviour) 
            {
                case OverrideBehaviour.IGNORE:
                    if (target.includes(value)) break;
                    pushIfNotExists(target, value);
                    break;
                case OverrideBehaviour.MERGE:
                case OverrideBehaviour.REPLACE:
                    pushIfNotExists(target, value);
                    break;
                case OverrideBehaviour.DELETE: {
                    const index = target.indexOf(value);
                    if (index > -1) 
                    {
                        this.logger.logDebug(`Deleting ${value} from array`);
                        target.splice(index, 1);
                    }
                }
                    break;
            }
        });
    }

    private processOverridableRecord<T>(target: Record<string, T>, source: Record<string, Overridable<T>>, defaultBehaviour: OverrideBehaviour) 
    {
        if (!source) return;
        for (const key in source) 
        {
            const item = source[key];
            const value = (item as any).value ?? item;
            const behaviour = (item as any).behaviour ?? defaultBehaviour;

            switch (behaviour) 
            {
                case OverrideBehaviour.IGNORE:
                    if (target[key] !== undefined) break;
                    target[key] = value;
                    break;
                case OverrideBehaviour.MERGE:
                case OverrideBehaviour.REPLACE:
                    this.logger.logDebug(`Overriding ${key} with ${value}`);
                    target[key] = value;
                    break;
                case OverrideBehaviour.DELETE:
                    this.logger.logDebug(`Deleting ${key}`);
                    delete target[key];
                    break;
            }
        }
    }
    private readOverrides(): OverridedSettings 
    {
        const overridedSettings = new OverridedSettings();
        this.logger.log("Reading overrides");
        this.vfs.getDirectories(this.modsDirectory).map(m => path.join(this.modsDirectory, m))
            .filter(modDir => this.vfs.exists(path.join(modDir, "MissingQuestWeapons")))
            .forEach(modDir => 
            {
                this.logger.log(`Processing mod: ${path.basename(modDir)}`)
                this.logger.plusIndent();

                try 
                {
                    const questOverridesData = tryReadJson<IQuestOverrides>(path.join(modDir, "MissingQuestWeapons"), "QuestOverrides", this.logger);
                    if (questOverridesData) 
                    {
                        const defaultOverrideBehaviour = questOverridesData.OverrideBehaviour ?? OverrideBehaviour.IGNORE;
                        questOverridesData.Overrides.forEach((v) => 
                        {
                            this.logger.log(`Processing quest override: ${v.id} ${v.conditions?.length ? `with conditions ${v.conditions.join(", ")}` : ""}`);
                            const overrideBehaviour = v.OverrideBehaviour ?? defaultOverrideBehaviour;
                            let hasOverrides = overridedSettings.questOverrides[v.id] !== undefined && overridedSettings.questOverrides[v.id].length > 0;

                            const existingOverrides: IQuestOverride[] = [];
                            if (hasOverrides) 
                            {
                                // try and find matching conditions
                                for (const existingOverride of overridedSettings.questOverrides[v.id]) 
                                {
                                    if ((!v.conditions || v.conditions.length === 0) && (!existingOverride.condition)) 
                                    {
                                        existingOverrides.push(existingOverride);
                                    }
                                    else if (v.conditions && existingOverride.condition) 
                                    {
                                        if (v.conditions.includes(existingOverride.condition)) 
                                        {
                                            existingOverrides.push(existingOverride);
                                            break;
                                        }
                                    }
                                }
                            }

                            hasOverrides = existingOverrides.length > 0;


                            const deleteFromExisting = () => 
                            {
                                for (const existingOverride of existingOverrides) 
                                {
                                    const index = overridedSettings.questOverrides[v.id].indexOf(existingOverride);
                                    if (index > -1) 
                                    {
                                        overridedSettings.questOverrides[v.id].splice(index, 1);
                                    }
                                }
                            }


                            const createOverride=(questOverrideSettings: IQuestOverrideSetting, condition?: string): IQuestOverride=> 
                            {


                                const created = {
                                    id: questOverrideSettings.id,
                                    skip: questOverrideSettings.skip,
                                    onlyUseWhiteListedWeapons: questOverrideSettings.onlyUseWhiteListedWeapons,
                                    whiteListedWeapons: questOverrideSettings.whiteListedWeapons?.map(w => (w as any).value ?? w) ?? [],
                                    blackListedWeapons: questOverrideSettings.blackListedWeapons?.map(w => (w as any).value ?? w) ?? [],
                                    condition: condition
                                }

                                for (const w of created.whiteListedWeapons) 
                                {
                                    if (created.blackListedWeapons?.includes(w)) 
                                    {
                                        this.logger.error(`Weapon ${w} is both blacklisted and whitelisted for quest ${v.id} condition ${condition}`);
                                        created.blackListedWeapons = created.blackListedWeapons?.filter(bw => bw !== w);
                                    }
                                }

                                return created;
                            }
                            const createNewOverrides = () : IQuestOverride[] => 
                            {
                                const toAdd : IQuestOverride[] = [];
                                if (v.conditions)
                                {
                                    for (const condition of v.conditions) 
                                    {

                                        toAdd.push(createOverride(v, condition));
                                    }
                                }
                                else 
                                {
                                    toAdd.push(createOverride(v));
                                }

                                return toAdd;
                            }

                            const addNewOverride = (newOverride) => 
                            {
                                if (overridedSettings.questOverrides[v.id] === undefined) 
                                {
                                    overridedSettings.questOverrides[v.id] = [];
                                }
                                overridedSettings.questOverrides[v.id].push(newOverride);
                            }

                            const addNewOverrides = () => 
                            {
                                if (overridedSettings.questOverrides[v.id] === undefined) 
                                {
                                    overridedSettings.questOverrides[v.id] = [];
                                }

                                const toAdd = createNewOverrides();
                                
                                for (const questOverride of toAdd) 
                                {
                                    overridedSettings.questOverrides[v.id].push(questOverride);
                                }
                            }

                            const replaceExisting = () => 
                            {
                                deleteFromExisting();
                                addNewOverrides();
                            }

                            const mergeWithExisting = () => 
                            {
                                const toMerge = [];
                                if (v.conditions)
                                {
                                    for (const condition of v.conditions) 
                                    {
                                        toMerge.push({
                                            ...v, condition
                                        })
                                    }
                                }
                                else 
                                {
                                    toMerge.push(v);
                                }

                                for (const newOverride of toMerge) 
                                {
                                    const index = existingOverrides.findIndex(o => o.condition === newOverride.condition);
                                    if ( index === -1)
                                    {
                                        addNewOverride(newOverride);
                                    }
                                    else 
                                    {
                                        const existingOverride = existingOverrides[index];
                                        if (newOverride.skip !== undefined) existingOverride.skip = newOverride.skip;
                                        if (newOverride.onlyUseWhiteListedWeapons !== undefined) existingOverride.onlyUseWhiteListedWeapons = newOverride.onlyUseWhiteListedWeapons;
                                        if (newOverride.whiteListedWeapons) 
                                        {
                                            this.processOverridableArray(existingOverride.whiteListedWeapons, newOverride.whiteListedWeapons, OverrideBehaviour.MERGE);
                                        }
                                        if (newOverride.blackListedWeapons) 
                                        {
                                            this.processOverridableArray(existingOverride.blackListedWeapons, newOverride.blackListedWeapons, OverrideBehaviour.MERGE);
                                        }

                                        for (const w of existingOverride.whiteListedWeapons) 
                                        {
                                            if (existingOverride.blackListedWeapons?.includes(w)) 
                                            {
                                                this.logger.error(`Weapon ${w} is both blacklisted and whitelisted for quest ${v.id} condition ${existingOverride.condition}`);
                                                existingOverride.blackListedWeapons = existingOverride.blackListedWeapons?.filter(bw => bw !== w);
                                            }
                                        }
                                    }
                                }
                                
                            }
                            if (!hasOverrides) 
                            {
                                this.logger.log(`No existing overrides found for quest ${v.id} with conditions ${v.conditions}`);

                                if (overrideBehaviour === OverrideBehaviour.DELETE) 
                                {
                                    // nothing to do
                                    return
                                }
                                else 
                                {
                                    // create new
                                    addNewOverrides();
                                    return;
                                }

                            }
                            else 
                            {
                                this.logger.log(`Found ${existingOverrides.length} existing overrides for quest ${v.id} with conditions ${v.conditions}`);
                                this.logger.logDebug(`Existing overrides for quest ${v.id}:`);
                                this.logger.logDebug(overridedSettings.questOverrides[v.id]);
                                switch (overrideBehaviour) 
                                {
                                    case OverrideBehaviour.IGNORE:
                                        {
                                            const toAdd = createNewOverrides();
                                            for (const newOverride of toAdd) 
                                            {
                                                if (!existingOverrides.find(o => o.condition === newOverride.condition)) 
                                                {
                                                    addNewOverride(newOverride);
                                                }
                                            }}
                                        break;
                                    case OverrideBehaviour.DELETE:
                                        deleteFromExisting();
                                        break;
                                    case OverrideBehaviour.REPLACE:
                                        replaceExisting();
                                        break;
                                    case OverrideBehaviour.MERGE:
                                        mergeWithExisting();
                                        break;
                                    default:
                                        this.logger.error(`Unknown OverrideBehaviour ${overrideBehaviour} for quest ${v.id}`);
                                        break;
                                }
                                this.logger.logDebug(`Quest overrides after processing for quest ${v.id}:`);
                                this.logger.logDebug(overridedSettings.questOverrides[v.id]);
                            }
                        });

                        questOverridesData.BlackListedQuests.forEach(v => 
                        {
                            if (!overridedSettings.questOverrides[v]) 
                            {
                                overridedSettings.questOverrides[v] = [];
                            }

                            // Add a blacklist override. It will be picked up by getOverrideForQuest.
                            overridedSettings.questOverrides[v].push({
                                id: v,
                                blackListed: true
                            });
                        })
                    }
                }
                catch (e) 
                {
                    this.logger.error(`Error reading QuestOverrides in ${modDir}: ${e.message}`);
                }

                try 
                {
                    const overriddenWeaponsData = tryReadJson<IOverriddenWeapons>(path.join(modDir, "MissingQuestWeapons"), "OverriddenWeapons", this.logger);
                    if (overriddenWeaponsData) 
                    {
                        const defaultBehaviour = overriddenWeaponsData.OverrideBehaviour ?? OverrideBehaviour.IGNORE;
                        if (overriddenWeaponsData.Override) 
                        {
                            this.logger.logDebug("Processing overridden weapons");
                            this.logger.logDebug("Existing overridden weapons:");
                            this.logger.logDebug(overridedSettings.overriddenWeapons);
                            this.logger.logDebug("Overrides to process:");
                            this.logger.logDebug(overriddenWeaponsData.Override);
                            this.processOverridableRecord(overridedSettings.overriddenWeapons, overriddenWeaponsData.Override, defaultBehaviour);
                            this.logger.logDebug("Overridden weapons after processing:");
                            this.logger.logDebug(overridedSettings.overriddenWeapons);
                        }


                        if (overriddenWeaponsData.CanBeUsedAs) 
                        {
                            this.logger.logDebug("Processing canBeUsedAs weapons");
                            for (const key in overriddenWeaponsData.CanBeUsedAs) 
                            {
                                if (!overridedSettings.canBeUsedAs[key]) 
                                {
                                    overridedSettings.canBeUsedAs[key] = [];
                                }
                                this.logger.logDebug(`Existing canBeUsedAs weapons for ${key}:`);
                                this.logger.logDebug(overridedSettings.canBeUsedAs[key]);
                                this.logger.logDebug("canBeUsedAs to process:");
                                this.logger.logDebug(overriddenWeaponsData.CanBeUsedAs[key]);
                                this.processOverridableArray(overridedSettings.canBeUsedAs[key], overriddenWeaponsData.CanBeUsedAs[key], defaultBehaviour);
                                this.logger.logDebug(`canBeUsedAs for ${key} after processing:`);
                                this.logger.logDebug(overridedSettings.canBeUsedAs[key]);
                            }
                        }

                        if (overriddenWeaponsData.CanBeUsedAsShortNameWhitelist) 
                        {
                            this.logger.logDebug("Processing canBeUsedAsShortNameWhitelist weapons");
                            if (!overridedSettings.canBeUsedAsShortNameWhitelist) 
                            {
                                overridedSettings.canBeUsedAsShortNameWhitelist = [];
                            }
                            this.logger.logDebug("Existing canBeUsedAsShortNameWhitelist weapons:");
                            this.logger.logDebug(overridedSettings.canBeUsedAsShortNameWhitelist);
                            this.logger.logDebug("canBeUsedAsShortNameWhitelist to process:");
                            this.logger.logDebug(overriddenWeaponsData.CanBeUsedAsShortNameWhitelist);
                            this.processOverridableArray(overridedSettings.canBeUsedAsShortNameWhitelist, overriddenWeaponsData.CanBeUsedAsShortNameWhitelist, defaultBehaviour);
                            this.logger.logDebug("canBeUsedAsShortNameWhitelist after processing:");
                            this.logger.logDebug(overridedSettings.canBeUsedAsShortNameWhitelist);
                        }

                        if (overriddenWeaponsData.CanBeUsedAsShortNameBlacklist) 
                        {
                            this.logger.logDebug("Processing canBeUsedAsShortNameBlacklist weapons");
                            if (!overridedSettings.canBeUsedAsShortNameBlackList) 
                            {
                                overridedSettings.canBeUsedAsShortNameBlackList = [];
                            }
                            this.logger.logDebug("Existing canBeUsedAsShortNameBlacklist weapons:");
                            this.logger.logDebug(overridedSettings.canBeUsedAsShortNameBlackList);
                            this.logger.logDebug("canBeUsedAsShortNameBlacklist to process:");
                            this.logger.logDebug(overriddenWeaponsData.CanBeUsedAsShortNameBlacklist);
                            this.processOverridableArray(overridedSettings.canBeUsedAsShortNameBlackList, overriddenWeaponsData.CanBeUsedAsShortNameBlacklist, defaultBehaviour);
                            this.logger.logDebug("canBeUsedAsShortNameBlacklist after processing:");
                            this.logger.logDebug(overridedSettings.canBeUsedAsShortNameBlackList);
                        }
                        if (overriddenWeaponsData.CustomCategories) 
                        {
                            this.logger.log("Custom Categories found");
                            for (const item of overriddenWeaponsData.CustomCategories) 
                            {
                                const customCategory: IWeaponCategory = (item as any).value ?? item;
                                const behaviour = (item as any).behaviour ?? defaultBehaviour;
                                if (behaviour === OverrideBehaviour.DELETE) 
                                {
                                    if (overridedSettings.customCategories[customCategory.name]) 
                                    {
                                        this.logger.log(`Deleting custom category: ${customCategory.name}`);
                                        delete overridedSettings.customCategories[customCategory.name];
                                    }
                                    continue;
                                }

                                if (behaviour === OverrideBehaviour.IGNORE && overridedSettings.customCategories[customCategory.name]) 
                                {
                                    this.logger.log(`Ignoring custom category: ${customCategory.name}`);
                                    continue;
                                }
                                const alreadyExists = overridedSettings.customCategories[customCategory.name] !== undefined;
                                if (!overridedSettings.customCategories[customCategory.name]) 
                                {
                                    overridedSettings.customCategories[customCategory.name] = {
                                        name: customCategory.name,
                                        ids: [],
                                        whiteListedKeywords: [],
                                        blackListedKeywords: [],
                                        allowedCalibres: [],
                                        alsoCheckDescription: false
                                    };
                                }


                                // merge with existing categories except the name
                                const category = overridedSettings.customCategories[customCategory.name];

                                if (behaviour === OverrideBehaviour.REPLACE) 
                                {
                                    this.logger.log(`Replacing custom category: ${customCategory.name}`);
                                    if (alreadyExists)
                                    {
                                        this.logger.logDebug("Existing category:");
                                        this.logger.logDebug(category);
                                    }
                                    category.ids = customCategory.ids ?? [];
                                    category.whiteListedKeywords = customCategory.whiteListedKeywords ?? [];
                                    category.blackListedKeywords = customCategory.blackListedKeywords ?? [];
                                    category.allowedCalibres = customCategory.allowedCalibres ?? [];
                                    category.alsoCheckDescription = customCategory.alsoCheckDescription || false;
                                }
                                else // MERGE
                                {
                                    if (alreadyExists)
                                    {
                                        this.logger.log(`Merging custom category: ${customCategory.name}`);
                                        this.logger.logDebug("Existing category:");
                                        this.logger.logDebug(category);
                                    }
                                    else 
                                    {
                                        this.logger.logDebug(`Adding new custom category: ${customCategory.name}`);
                                    }
                                    if (customCategory.ids) 
                                    {
                                        for (const id of customCategory.ids) 
                                        {
                                            pushIfNotExists(category.ids, id);
                                        }
                                    }

                                    if (customCategory.whiteListedKeywords) 
                                    {
                                        for (const id of customCategory.whiteListedKeywords) 
                                        {
                                            pushIfNotExists(category.whiteListedKeywords, id);
                                        }
                                    }

                                    if (customCategory.blackListedKeywords) 
                                    {
                                        for (const id of customCategory.blackListedKeywords) 
                                        {
                                            pushIfNotExists(category.blackListedKeywords, id);

                                        }
                                    }

                                    if (customCategory.allowedCalibres) 
                                    {
                                        for (const id of customCategory.allowedCalibres) 
                                        {
                                            pushIfNotExists(category.allowedCalibres, id);
                                        }
                                    }

                                    customCategory.alsoCheckDescription ||= category.alsoCheckDescription;
                                    // this.logger.logDebug("Merged category:");
                                    // this.logger.logDebug(category);
                                }
                                this.logger.log(`Final category ${customCategory.name}:`);
                                this.logger.log(category);
                            }
                        }
                    }
                }
                catch (e) 
                {
                    this.logger.error(`Error reading OverriddenWeapons in ${modDir}: ${e.message}`);
                }
                this.logger.minusIndent();

            })


        // finalize
        this.logger.logDebug("Finalizing read overrides");
        for (const id of overridedSettings.canBeUsedAsShortNameBlackList)
        {
            this.logger.logDebug(`Finalizing canBeUsedAsShortNameBlackList item: ${id}`);
            const index = overridedSettings.canBeUsedAsShortNameWhitelist.indexOf(id);
            if (index > -1) 
            {
                this.logger.log(`Removing ${id} from canBeUsedAsShortNameWhitelist because it is also in canBeUsedAsShortNameBlackList`);
                overridedSettings.canBeUsedAsShortNameWhitelist.splice(index, 1);
            }
        }
        
        this.logger.log("##### Quest Overrides #####");
        this.logger.plusIndent();
        for (const key in overridedSettings.questOverrides) 
        {
            this.logger.log(overridedSettings.questOverrides[key]);
        }
        this.logger.minusIndent();

        this.logger.log("##### #####");

        this.logger.logDebug("##### Overridden Weapons #####");
        this.logger.plusIndent();
        this.logger.logDebug("Overridden weapons:");
        this.logger.logDebug(overridedSettings.overriddenWeapons);
        this.logger.logDebug("##### #####");
        this.logger.logDebug("##### canBeUsedAs #####");
        this.logger.plusIndent();
        for (const key in overridedSettings.canBeUsedAs)
        {
            this.logger.logDebug(`${key}: ${overridedSettings.canBeUsedAs[key]}`);
        }
        this.logger.minusIndent();
        this.logger.logDebug("##### #####");
        this.logger.logDebug("##### canBeUsedAsShortNameWhitelist #####");
        this.logger.plusIndent();
        this.logger.logDebug(overridedSettings.canBeUsedAsShortNameWhitelist);
        this.logger.minusIndent();
        this.logger.logDebug("##### #####");
        this.logger.logDebug("##### canBeUsedAsShortNameBlackList #####");
        this.logger.plusIndent();
        this.logger.logDebug(overridedSettings.canBeUsedAsShortNameBlackList);
        this.logger.minusIndent();
        this.logger.logDebug("##### #####");
        this.logger.logDebug("##### Custom Categories #####");
        this.logger.plusIndent();
        for (const key in overridedSettings.customCategories)
        {
            this.logger.logDebug(overridedSettings.customCategories[key]);
        }
        this.logger.minusIndent();
        this.logger.minusIndent();
        
        return overridedSettings;
    }
}