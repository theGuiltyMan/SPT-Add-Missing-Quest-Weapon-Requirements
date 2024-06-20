import { VFS } from "@spt-aki/utils/VFS";
import path from "path";
import { inject, injectable } from "tsyringe";
import { IOverriddenWeapons } from "./models/IOverriddenWeapons";
import { IQuestOverrides } from "./models/IQuestOverrides";
import { tryReadJson } from "./util/jsonHelper";
import { LogHelper } from "./util/logHelper";
import { IQuestOverride } from "./models/IQuestOverride";
import { IWeaponCategory } from "./models/IWeaponCategory";

@injectable()
export class OverrideReader 
{

    constructor(
        @inject("LogHelper") protected logger: LogHelper,
        @inject("VFS") protected vfs: VFS,
        @inject("modDir") protected modsDirectory: string
    )
    {
    }
 
    public run() : void 
    {
        this.readOverrides();
        // todo:  categorize based on read data
    }
    
    private readOverrides() : void 
    {
        this.logger.addLog("Reading overrides");
        this.vfs.getDirs(this.modsDirectory).map(m=>path.resolve(this.modsDirectory, m))
            .filter(modDir => this.vfs.exists(path.resolve(modDir, "MissingQuestWeapons")))
            .forEach(modDir => 
            {
                this.logger.addLog(`Processing mod: ${path.basename(modDir)}`)
                this.logger.plusIndent();
            
                const questOverridesData = tryReadJson<IQuestOverrides>(path.resolve(modDir, "MissingQuestWeapons"), "QuestOverrides");
                const questOverrides : Record<string, IQuestOverride> = {};
                const overriddenWeapons : Record<string, string> = {};
                const canBeUsedAs : Record<string, Set<string>> = {};
                const customCategories : Record<string, IWeaponCategory> = {}; // todo actual type
                if (questOverridesData) 
                {
                    questOverridesData.Overrides.forEach((v) => 
                    {

                        if (!questOverrides[v.questId])
                        {
                            questOverrides[v.questId] =  {
                                id : v.questId,
                                whiteListedWeapons: new Set<string>,
                                blackListedWeapons: new Set<string>,
                                skip: false,
                                onlyUseWhiteListedWeapons: v.disabled
                            }
                        }
                        const questOverride = questOverrides[v.questId];

                        this.logger.addLog(`Processing quest override: ${this.logger.asReadable(v.questId)}`)

                        if (v.whiteListedWeapons)
                        {
                            v.whiteListedWeapons.forEach(w=>questOverride.whiteListedWeapons.add(w));
                        }
                        if (v.blackListedWeapons)
                        {
                            v.blackListedWeapons.forEach(w=>questOverride.blackListedWeapons.add(w));
                        }

                        // check if any weapons conflicted
                        for (const w of v.whiteListedWeapons) 
                        {
                            if (questOverride.blackListedWeapons.has(w)) 
                            {
                                this.logger.error(`Weapon ${w} is both blacklisted and whitelisted for quest ${v.questId}`);
                            }
                        }
                    });

                    questOverridesData.BlackListedQuests.forEach(v=>
                    {
                        if (!questOverrides[v])
                        {
                            questOverrides[v] =  {
                                id : v,
                                skip: true,
                                blackListedWeapons: new Set<string>,
                                onlyUseWhiteListedWeapons: false,
                                whiteListedWeapons: new Set<string>()
                            }
                        }
                        else 
                        {
                            this.logger.error(`Quest ${v} is both in blacklisted quests and in quest overrides. Blacklisting will take precedence.`);
                            questOverrides[v].skip = true;
                        }
                    })
                }

                const  overriddenWeaponsData = tryReadJson<IOverriddenWeapons>(path.resolve(modDir, "MissingQuestWeapons"), "OverriddenWeapons");
                if (overriddenWeaponsData) 
                {
                    if (overriddenWeaponsData.Override)
                    {
                        for (const key in overriddenWeaponsData.Override) 
                        {
                            overriddenWeapons[key] = overriddenWeaponsData.Override[key];
                        }
                    }

                    
                    if (overriddenWeaponsData.CanBeUsedAs)
                    {
                        for (const key in overriddenWeaponsData.CanBeUsedAs) 
                        {
                            if (!canBeUsedAs[key]) 
                            {
                                canBeUsedAs[key] = new Set<string>();
                            }
                            overriddenWeaponsData.CanBeUsedAs[key].forEach(v=>canBeUsedAs[key].add(v));
                            for (const v of overriddenWeaponsData.CanBeUsedAs[key]) 
                            {
                                if (!canBeUsedAs[v]) 
                                {
                                    canBeUsedAs[v] = new Set<string>();
                                }
                                canBeUsedAs[v].add(key);
                            }
                        }
                    }
                    if (overriddenWeaponsData.CustomCategories)
                    {
                        for (const customCategory of overriddenWeaponsData.CustomCategories) 
                        {
                            this.logger.addLog(customCategory);
                            if (!customCategories[customCategory.name]) 
                            {
                                customCategories[customCategory.name] = {
                                    name : customCategory.name,
                                    ids : new Set<string>(),
                                    whiteListedKeywords : new Set<string>(),
                                    blackListedKeywords : new Set<string>()
                                };
                            }

                            // merge with existing categories except the name
                            const category = customCategories[customCategory.name];
                            if (customCategory.ids)
                            {
                                for (const id of customCategory.ids) 
                                {
                                    category.ids.add(id);
                                }
                            }
                            if (customCategory.whiteListedKeywords)
                            {
                                for (const id of customCategory.whiteListedKeywords) 
                                {
                                    category.whiteListedKeywords.add(id);
                                }
                            }

                            if (customCategory.blackListedKeywords)
                            {
                                for (const id of customCategory.blackListedKeywords) 
                                {
                                    category.blackListedKeywords.add(id);
                                      
                                }
                            }
                            this.logger.addLog(category)
                        }
                    }
                }
                this.logger.minusIndent();
            })
    }
}