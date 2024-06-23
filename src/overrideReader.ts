import { VFS } from "@spt-aki/utils/VFS";
import path from "path";
import { DependencyContainer, inject, injectable } from "tsyringe";
import { IOverriddenWeapons } from "./models/IOverriddenWeapons";
import { IQuestOverrides } from "./models/IQuestOverrides";
import { tryReadJson } from "./util/jsonHelper";
import { LogHelper } from "./util/logHelper";
import { OverridedSettings } from "./models/OverridedSettings";
import { pushIfNotExists } from "./util/misc";



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
 
    public run(childContainer: DependencyContainer) : void 
    {
        childContainer.registerInstance<OverridedSettings>("OverridedSettings", this.readOverrides());
    }
    
    private readOverrides() : OverridedSettings
    {   
        const overridedSettings = new OverridedSettings();
        this.logger.log("Reading overrides");
        this.vfs.getDirs(this.modsDirectory).map(m=>path.resolve(this.modsDirectory, m))
            .filter(modDir => this.vfs.exists(path.resolve(modDir, "MissingQuestWeapons")))
            .forEach(modDir => 
            {
                this.logger.log(`Processing mod: ${path.basename(modDir)}`)
                this.logger.plusIndent();
            
                try 
                {
                    const questOverridesData = tryReadJson<IQuestOverrides>(path.resolve(modDir, "MissingQuestWeapons"), "QuestOverrides");
                    if (questOverridesData) 
                    {
                        questOverridesData.Overrides.forEach((v) => 
                        {

                            if (!overridedSettings.questOverrides[v.id])
                            {
                                overridedSettings.questOverrides[v.id] =  {
                                    id : v.id,
                                    whiteListedWeapons: [],
                                    blackListedWeapons: [],
                                    skip: false,
                                    onlyUseWhiteListedWeapons: false
                                }
                            }
                            const questOverride = overridedSettings.questOverrides[v.id];

                            questOverride.onlyUseWhiteListedWeapons||= v.onlyUseWhiteListedWeapons || false;
                            this.logger.log(`Processing quest override: ${v.id})}`)

                            if (v.whiteListedWeapons)
                            {
                                v.whiteListedWeapons.forEach(w=>pushIfNotExists(questOverride.whiteListedWeapons,w));
                            }
                            if (v.blackListedWeapons)
                            {
                                v.blackListedWeapons.forEach(w=>pushIfNotExists(questOverride.blackListedWeapons,w));
                            }

                            if (v.skip)
                            {
                                questOverride.skip = true;
                            }
                            // check if any weapons conflicted
                            if (v.whiteListedWeapons )
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

                        questOverridesData.BlackListedQuests.forEach(v=>
                        {
                            if (!overridedSettings.questOverrides[v])
                            {
                                overridedSettings.questOverrides[v] =  {
                                    id : v,
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
                    const  overriddenWeaponsData = tryReadJson<IOverriddenWeapons>(path.resolve(modDir, "MissingQuestWeapons"), "OverriddenWeapons");
                    if (overriddenWeaponsData) 
                    {
                        if (overriddenWeaponsData.Override)
                        {
                            for (const key in overriddenWeaponsData.Override) 
                            {
                                overridedSettings.overriddenWeapons[key] = overriddenWeaponsData.Override[key];
                            }
                        }

                    
                        if (overriddenWeaponsData.CanBeUsedAs)
                        {
                            for (const key in overriddenWeaponsData.CanBeUsedAs) 
                            {
                                if (!overridedSettings.canBeUsedAs[key]) 
                                {
                                    overridedSettings.canBeUsedAs[key] = [];
                                }
                                overriddenWeaponsData.CanBeUsedAs[key].forEach(v=>pushIfNotExists(overridedSettings.canBeUsedAs[key],v));
                                for (const v of overriddenWeaponsData.CanBeUsedAs[key]) 
                                {
                                    if (!overridedSettings.canBeUsedAs[v]) 
                                    {
                                        overridedSettings.canBeUsedAs[v] = [];
                                    }
                                    pushIfNotExists(overridedSettings.canBeUsedAs[v],key);
                                }
                            }
                        }
                        if (overriddenWeaponsData.CustomCategories)
                        {
                            for (const customCategory of overriddenWeaponsData.CustomCategories) 
                            {

                                if (!overridedSettings.customCategories[customCategory.name]) 
                                {
                                    overridedSettings.customCategories[customCategory.name] = {
                                        name : customCategory.name,
                                        ids : [],
                                        whiteListedKeywords : [],
                                        blackListedKeywords : [],
                                        allowedCalibres : [],
                                        alsoCheckDescription : customCategory.alsoCheckDescription || false
                                    };
                                }
                        

                                // merge with existing categories except the name
                                const category = overridedSettings.customCategories[customCategory.name];
                                if (customCategory.ids)
                                {
                                    for (const id of customCategory.ids) 
                                    {
                                        pushIfNotExists(category.ids,id);
                                    }
                                }

                                if (customCategory.whiteListedKeywords)
                                {
                                    for (const id of customCategory.whiteListedKeywords) 
                                    {
                                        pushIfNotExists(category.whiteListedKeywords,id);
                                    }
                                }

                                if (customCategory.blackListedKeywords)
                                {
                                    for (const id of customCategory.blackListedKeywords) 
                                    {
                                        pushIfNotExists(category.blackListedKeywords,id);
                                      
                                    }
                                }

                                if (customCategory.allowedCalibres)
                                {
                                    for (const id of customCategory.allowedCalibres) 
                                    {
                                        pushIfNotExists(category.allowedCalibres,id);
                                    }
                                }

                                customCategory.alsoCheckDescription ||= category.alsoCheckDescription;
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