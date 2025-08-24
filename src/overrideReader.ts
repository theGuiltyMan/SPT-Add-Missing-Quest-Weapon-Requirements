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
                    if (target.includes(value)) return;
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
                    if (target[key] !== undefined) return;
                    target[key] = value;
                    break;
                case OverrideBehaviour.MERGE:
                case OverrideBehaviour.REPLACE:
                    target[key] = value;
                    break;
                case OverrideBehaviour.DELETE:
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
                            this.logger.log(`Processing quest override: ${v.id})}`)
                            const overrideBehaviour = v.OverrideBehaviour ?? defaultOverrideBehaviour;
                            const hasValue = overridedSettings.questOverrides[v.id] !== undefined;

                            if (!hasValue) 
                            {
                                overridedSettings.questOverrides[v.id] = {
                                    id: v.id,
                                    whiteListedWeapons: [],
                                    blackListedWeapons: [],
                                    skip: false,
                                    onlyUseWhiteListedWeapons: false
                                }
                            }

                            switch (overrideBehaviour) 
                            {
                                case OverrideBehaviour.IGNORE:
                                    if (hasValue) 
                                    {
                                        this.logger.log(`Ignoring override for quest ${v.id} as it already exists`);
                                        return;
                                    }
                                    break;
                                case OverrideBehaviour.DELETE:
                                    this.logger.log(`Deleting override for quest ${v.id}`);
                                    delete overridedSettings.questOverrides[v.id];
                                    return;
                                case OverrideBehaviour.REPLACE:
                                    if (hasValue) 
                                    {
                                        this.logger.log(`Replacing override for quest ${v.id}`);
                                        overridedSettings.questOverrides[v.id] = {
                                            id: v.id,
                                            whiteListedWeapons: [],
                                            blackListedWeapons: [],
                                            skip: false,
                                            onlyUseWhiteListedWeapons: false
                                        }
                                    }
                                    break;
                                case OverrideBehaviour.MERGE:
                                    // nothing to do here, just merge the values
                                    break;
                                default:
                                    this.logger.error(`Unknown OverrideBehaviour ${overrideBehaviour} for quest ${v.id}`);
                                    return;
                            }
                            const questOverride = overridedSettings.questOverrides[v.id];

                            questOverride.onlyUseWhiteListedWeapons ||= v.onlyUseWhiteListedWeapons || false;

                            if (v.whiteListedWeapons) 
                            {
                                v.whiteListedWeapons.forEach(w => pushIfNotExists(questOverride.whiteListedWeapons, w));
                            }
                            if (v.blackListedWeapons) 
                            {
                                v.blackListedWeapons.forEach(w => pushIfNotExists(questOverride.blackListedWeapons, w));
                            }

                            if (v.skip) 
                            {
                                questOverride.skip = true;
                            }
                            // check if any weapons conflicted
                            if (v.whiteListedWeapons) 
                            {
                                for (const w of v.whiteListedWeapons) 
                                {
                                    if (questOverride.blackListedWeapons.includes(w)) 
                                    {
                                        this.logger.error(`Weapon ${w} is both blacklisted and whitelisted for quest ${v.id}`);
                                    }
                                }
                            }
                        });

                        questOverridesData.BlackListedQuests.forEach(v => 
                        {
                            if (!overridedSettings.questOverrides[v]) 
                            {
                                overridedSettings.questOverrides[v] = {
                                    id: v,
                                    blackListed: true
                                }
                            }
                            else 
                            {
                                this.logger.error(`Quest ${v} is both in blacklisted quests and in quest overrides. Blacklisting will take precedence.`);
                                overridedSettings.questOverrides[v].skip = true;
                            }
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
                            this.processOverridableRecord(overridedSettings.overriddenWeapons, overriddenWeaponsData.Override, defaultBehaviour);
                        }


                        if (overriddenWeaponsData.CanBeUsedAs) 
                        {
                            for (const key in overriddenWeaponsData.CanBeUsedAs) 
                            {
                                if (!overridedSettings.canBeUsedAs[key]) 
                                {
                                    overridedSettings.canBeUsedAs[key] = [];
                                }
                                this.processOverridableArray(overridedSettings.canBeUsedAs[key], overriddenWeaponsData.CanBeUsedAs[key], defaultBehaviour);
                            }
                        }

                        if (overriddenWeaponsData.CanBeUsedAsShortNameWhitelist) 
                        {
                            this.processOverridableArray(overridedSettings.canBeUsedAsShortNameWhitelist, overriddenWeaponsData.CanBeUsedAsShortNameWhitelist, defaultBehaviour);
                        }

                        if (overriddenWeaponsData.CanBeUsedAsShortNameBlacklist) 
                        {
                            this.processOverridableArray(overridedSettings.canBeUsedAsShortNameBlackList, overriddenWeaponsData.CanBeUsedAsShortNameBlacklist, defaultBehaviour);
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
                                    category.ids = customCategory.ids ?? [];
                                    category.whiteListedKeywords = customCategory.whiteListedKeywords ?? [];
                                    category.blackListedKeywords = customCategory.blackListedKeywords ?? [];
                                    category.allowedCalibres = customCategory.allowedCalibres ?? [];
                                    category.alsoCheckDescription = customCategory.alsoCheckDescription || false;
                                }
                                else // MERGE
                                {
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
                                }
                                this.logger.log(category)
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


        this.logger.log("##### Quest Overrides #####");
        this.logger.plusIndent();
        for (const key in overridedSettings.questOverrides) 
        {
            this.logger.log(overridedSettings.questOverrides[key]);
        }
        this.logger.minusIndent();

        this.logger.log("##### #####");

        this.logger.log("##### Overridden Weapons #####");
        return overridedSettings;
    }
}