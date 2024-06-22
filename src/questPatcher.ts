import { inject, injectable } from "tsyringe";
import { IAddMissingQuestRequirementConfig } from "./models/IAddMissingQuestRequirementConfig";
import { LogHelper } from "./util/logHelper";
import { WeaponCategorizer } from "./weaponCategorizer";
import { IQuest } from "@spt-aki/models/eft/common/tables/IQuest";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";
import { OverridedSettings } from "./models/OverridedSettings";
import { JsonUtil } from "@spt-aki/utils/JsonUtil";
import { pushIfNotExists } from "./util/misc";


@injectable()
export class QuestPatcher
{
    constructor(
        @inject("LogHelper") protected logger: LogHelper,
        @inject("AMQRConfig") protected config: IAddMissingQuestRequirementConfig,
        @inject("WeaponCategorizer") protected weaponCategorizer: WeaponCategorizer,
        @inject("DatabaseServer") protected databaseServer: DatabaseServer,
        @inject("WeaponTypes") protected weaponsTypes: Record<string, string[]>,
        @inject("WeaponToType") protected weaponToType: Record<string, string[]>,
        @inject("OverridedSettings") protected overridedSettings: OverridedSettings,
        @inject("JsonUtil") protected jsonUtil: JsonUtil
    ) 
    {
        
    }

    
    private quests : Record<string, IQuest> = {};

    run():void
    {
        try 
        {
            this.logger.log("######### Patching quests ########");
            const db = this.databaseServer.getTables();
            this.quests = db.templates.quests;
        
            const questOverrides = this.overridedSettings.questOverrides;
            const canBeUsedAs = this.overridedSettings.canBeUsedAs;
           
            // iterate through all quests
            for (const questId in this.quests) 
            {
                if (questOverrides[questId] && questOverrides[questId].blackListed)
                {
                    this.logger.log(`Skipping quest ${questId} due to blacklisted`);
                    continue;
                }
                // if (questId !== "Scorpion_10_1_1")
                // {
                //     continue;
                // }
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

                        for (let i = condition.counter.conditions.length - 1; i >= 0; i--) 
                        {
                            const counterCondition = condition.counter.conditions[i];
                            const newWeaponCondition = this.jsonUtil.clone(counterCondition.weapon);
                            const original = this.jsonUtil.clone(counterCondition.weapon);
                            let weaponsChangesLog = "";
                            try 
                            {
                                

                                if (!counterCondition.weapon || !newWeaponCondition.length) 
                                {
                                    continue;
                                }

                                const processBlackListed = (questId:string) :void =>
                                {
                                    if (questOverrides[questId] && questOverrides[questId].blackListedWeapons?.length > 0)
                                    {
                                        questOverrides[questId].blackListedWeapons.forEach(w=> 
                                        {
                                            const index = newWeaponCondition.indexOf(w);
                                            if (index !== -1) 
                                            {
                                                newWeaponCondition.splice(index, 1);
                                                weaponsChangesLog += `Removed blacklisted weapon: ${(w)} \n`;
                                            }
                                        });
                                    }
                                }
                                const processWhiteListed = (questId:string) :void =>
                                {
                                    if (questOverrides[questId] && questOverrides[questId].whiteListedWeapons?.length > 0)
                                    {
                                        questOverrides[questId].whiteListedWeapons.forEach(w=> pushIfNotExists(newWeaponCondition,w));
                                    }
                                }
                    
                                const processCanBeUsedAs = () :void =>
                                {
                                    for (let i = newWeaponCondition.length - 1; i >= 0; i--) 
                                    {
                                        if (canBeUsedAs[newWeaponCondition[i]])
                                        {
                                            canBeUsedAs[newWeaponCondition[i]].forEach(v=>pushIfNotExists(newWeaponCondition,v));
                                        }
                                    }
                                }
                                if (questOverrides[questId] && questOverrides[questId].skip)
                                {
                                // only add weapons that can be used as the weapon
                                    processCanBeUsedAs();
                                    processBlackListed(questId);

                                }

                                else 
                                {
                                    if (!(questOverrides[questId] && questOverrides[questId].onlyUseWhiteListedWeapons) && newWeaponCondition.length > 1)       
                                    {
                                        let weaponType = null;
                                        // select the most restrictive weapon type if all weapons are of the same type
                                        // for example if all weapons are revolvers, then the type is revolver
                                        // but if there are revolvers and pistols, then the type is pistol

                                        const potentialTypes : Record<string, string[]> = {};
                                        for (const weaponId of newWeaponCondition) 
                                        {
                                            if (!this.weaponToType[weaponId] || this.weaponToType[weaponId].length === 0) 
                                            {
                                                this.logger.error(`Weapon (${weaponId}) not found in weaponToType for quest ${questId}`);
                                                break;
                                            }

                                            for (const w of this.weaponToType[weaponId]) 
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

                       
                                        // this.logger.log(potentialTypes)
                                        let bestCandidate = null;
                                        // check if there is a weapon type that all weapons are of
                                        for (const w in potentialTypes) 
                                        {
                                            if (potentialTypes[w].length === newWeaponCondition.length) 
                                            {
                                                if (weaponType == null)
                                                {
                                                    weaponType = w;
                                                }
                                                else 
                                                {
                                                    // if there are multiple types, then select the most restrictive
                                                    if (this.config.kindOf[w] == weaponType || this.weaponsTypes[weaponType].length > this.weaponsTypes[w].length)
                                                    {
                                                        weaponType = w;
                                                    }
                                                }
                                            }
                                            else if (newWeaponCondition.length - potentialTypes[w].length === 1)
                                            {
                                                bestCandidate = {
                                                    type: w,
                                                    weapons: potentialTypes[w],
                                                    missing: newWeaponCondition.filter(i=>!potentialTypes[w].includes(i))
                                                };
                                            }
                                        }

                                        this.logger.logDebug(`Quest ${questId} Potential types: `)
                                        this.logger.plusIndent();
                                        this.logger.logDebug(potentialTypes)
                                        this.logger.minusIndent();
                                        if (weaponType == null && bestCandidate != null)
                                        {
                                            this.logger.log(`Quest ${questId} best candidate: \n\tType: ${bestCandidate.type}\n\tWeapons: ${bestCandidate.weapons.join(", ")}\n\tMissing: ${bestCandidate.missing.join(", ")}`);
                                        }
                                        const lastEntry = newWeaponCondition.length;
                                        if (weaponType)
                                        {
                                            // add the missing weapons
                                            for (const w of this.weaponsTypes[weaponType]) 
                                            {
                                                // probably just assign the array directly but just to be sure 
                                                if (newWeaponCondition.indexOf(w) === -1) 
                                                {
                                                    newWeaponCondition.push(w);
                                                }
                                            }
                                            if (lastEntry !== newWeaponCondition.length)
                                            {
                                                weaponsChangesLog += `Added ${newWeaponCondition.length - lastEntry} weapons of type ${weaponType}:`;
                                            }
                                        }
                                    }
                                

                                    if (newWeaponCondition.length === 1)
                                    {
                                        this.logger.log(`Quest ${questId} has only one weapon: ${counterCondition.weapon[0]}. Only adding whilelisted or can be used as weapons`);
                                    }
      
                                    processWhiteListed(questId);
                                    processCanBeUsedAs();
                                    processBlackListed(questId);
                                }


                                const formatToPrint = (orig: string[], newW: string[]) :string  =>  
                                {
                                    let str = "";
                                    orig.sort();

                                    newW.sort((a,b) => 
                                    {
                                        const aIndex = orig.indexOf(a);
                                        const bIndex = orig.indexOf(b);
                                        if (aIndex === -1 && bIndex === -1)
                                        {
                                            return a.localeCompare(b);
                                        }
                                        if (aIndex === -1)
                                        {
                                            return 1;
                                        }
                                        if (bIndex === -1)
                                        {
                                            return -1;
                                        }
                                        return aIndex - bIndex;
                                    });

                                    let i = 0;
                                    for (; i < orig.length; i++) 
                                    {
                                        str += `\t${orig[i]}\n`;
                                    }

                                    for (; i < newW.length; i++) 
                                    {
                                        str += `\t\t+++ ${newW[i]}\n`;
                                    }
                                    return str;
                                }
                                if (weaponsChangesLog.length > 0)
                                {
                                    // this.logger.log(`Quest: ${questId}\n\tOriginal: ${original.join(", ")}\n\t${weaponsChangesLog}`);
                                    this.logger.log(`Quest: ${questId} -- ${weaponsChangesLog}\n${formatToPrint(original,newWeaponCondition)}`);
                                }
                                else if (newWeaponCondition.length !== newWeaponCondition.length)
                                {
                                    this.logger.log(`Quest: ${questId}\n${formatToPrint(original,newWeaponCondition)}}`);
                                }
                                else 
                                {
                                    this.logger.logDebug(`Quest: ${questId} - No changes\n\tOriginal: ${original.join(", ")} `)
                                }
                            }
                            finally 
                            {
                                // this.logger.log(`Quest ${questId} \n Original: ${counterCondition.weapon.join(", ")} \n New: ${newWeaponCondition.join(", ")}`);
                                condition.counter.conditions[i].weapon = newWeaponCondition;
                                if (weaponsChangesLog.length > 0)
                                {

                                    // this.logger.logDebug("\n\n")
                                    // this.logger.logDebug(quest, LogType.NONE, false)
                                }
                            }
                        }
                    }
                })            
            }    

        }
        catch (e)
        {
            this.logger.error("An error occurred in AddMissingQuestRequirements mod. Please check the 'log.log' file in the mod directory  for more information.")
            throw e;
        }
    }
}