import { inject, injectable } from "tsyringe";
import { IAddMissingQuestRequirementConfig } from "./models/IAddMissingQuestRequirementConfig";
import { LogHelper } from "./util/logHelper";
import { WeaponCategorizer } from "./weaponCategorizer";
import { IQuest } from "@spt-aki/models/eft/common/tables/IQuest";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";
import { OverridedSettings } from "./models/OverridedSettings";
import { JsonUtil } from "@spt-aki/utils/JsonUtil";
import { pushIfNotExists } from "./util/misc";
import {LogType} from "./util/logHelper";

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
                    this.logger.logDebug(`Skipping quest ${questId} due to blacklisted`);
                    continue;
                }
                // if (questId !== "5bc4776586f774512d07cf05")
                // {
                //     continue;
                // }

                this.logger.log(`Patching quest ${questId}`);
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
                        try 
                        {
                            if (!condition.counter )
                            {
                                continue;
                            }

                            this.logger.logDebug(`Patching quest ${questId} - Condition: ${condition.id}`);

                            const doForWeaponOrType = (id: string, func: (id: string) => void) :void => 
                            {
                                if (this.weaponsTypes[id])
                                {
                                    this.weaponsTypes[id].forEach(v=>func(v));
                                }
                                else 
                                {
                                    func(id);
                                }
                            }

                            for (let cI = condition.counter.conditions.length - 1; cI >= 0; cI--) 
                            {
                                if (!condition.counter.conditions[cI].weapon || !condition.counter.conditions[cI].weapon.length) 
                                {
                                    continue;
                                }
                                const newWeaponCondition = this.jsonUtil.clone(condition.counter.conditions[cI].weapon);
                                const original = this.jsonUtil.clone(condition.counter.conditions[cI].weapon);
                                try 
                                {    
                                    //#region process white/black listed weapons
                                    const processBlackListed = (questId:string) :void =>
                                    {
                                        if (questOverrides[questId] && questOverrides[questId].blackListedWeapons?.length > 0)
                                        {
                                            questOverrides[questId].blackListedWeapons.forEach(w=> 
                                            {
                                                const attempToRemove = (id : string) :void => 
                                                {
                                                    if (newWeaponCondition.indexOf(id) !== -1)
                                                    {
                                                        newWeaponCondition.splice(newWeaponCondition.indexOf(id), 1);
                                                        this.logger.logDebug(`Removed blacklisted weapon: ${id} \n`);
                                                    }
                                                }
                                                // check if the a weapon or weapon type
                                                doForWeaponOrType(w,attempToRemove);
                                            });
                                        }
                                    }
                                    const processWhiteListed = (questId:string) :void =>
                                    {
                                        if (questOverrides[questId] && questOverrides[questId].whiteListedWeapons?.length > 0)
                                        {
                                            for (const w of questOverrides[questId].whiteListedWeapons) 
                                            {                              
                                                doForWeaponOrType(w, (id) => 
                                                {
                                                    if (pushIfNotExists(newWeaponCondition, id))
                                                    {
                                                        this.logger.logDebug(`Added white listed weapon: ${id}\n`);
                                                    }
                                            
                                                })
                                            }
                                        }
                                    }
                    
                                    const processCanBeUsedAs = () :void =>
                                    {
                                        for (let i = newWeaponCondition.length - 1; i >= 0; i--) 
                                        {

                                            if (canBeUsedAs[newWeaponCondition[i]])
                                            {
                                                for (const w of canBeUsedAs[newWeaponCondition[i]]) 
                                                {
                                                    doForWeaponOrType(w, (id) => 
                                                    {

                                                        if (pushIfNotExists(newWeaponCondition, id))
                                                        {
                                                            this.logger.logDebug(`Added can be used as weapon: ${id}\n`);
                                                        }
                                                    })
                                                }
                                            }
                                        }
                                    }
                                    let weaponType = null;

                                    //#endregion
                                    if (questOverrides[questId] && questOverrides[questId].skip)
                                    {
                                    // only add weapons that can be used as the weapon
                                        weaponType = "Skipped";
                                        processCanBeUsedAs();
                                        processBlackListed(questId);
                                    }
                                    else 
                                    {
                                        if (!(questOverrides[questId] && questOverrides[questId].onlyUseWhiteListedWeapons) && newWeaponCondition.length > 1)       
                                        {
                                            //#region finding the weapon type
                                            // select the most restrictive weapon type if all weapons are of the same type
                                            // for example if all weapons are revolvers, then the type is revolver
                                            // but if there are revolvers and pistols, then the type is pistol

                                            let error = false;
                                            const potentialTypes : Record<string, string[]> = {};
                                            this.logger.log("-------------")
                                            for (let i = newWeaponCondition.length - 1; i >= 0; i--) 
                                            {
                                                const weaponId = newWeaponCondition[i];
                                    
                                    
                                                if (!this.weaponToType[weaponId] || this.weaponToType[weaponId].length === 0) 
                                                {
                                                    this.logger.error(`Weapon (${weaponId}) not found in weaponToType for quest ${questId}`);
                                                    error = true;
                                                    break;
                                                }

                                                for (const w of this.weaponToType[weaponId]) 
                                                {
                                                    if (!potentialTypes[w]) 
                                                    {
                                                        potentialTypes[w] = [];
                                                    }
                                                    pushIfNotExists(potentialTypes[w], weaponId);
                                                    if (canBeUsedAs[weaponId])
                                                    {
                                                        canBeUsedAs[weaponId].forEach(v=>
                                                        {
                                                            pushIfNotExists( potentialTypes[w],v);
                                                            pushIfNotExists(newWeaponCondition,v);
                                                        });
                                                    }
                                                }
                                            }
                                            if (error) // probably not needed
                                            {
                                                break;
                                            }
                                            // this.logger.log(potentialTypes)
                                            let bestCandidate = null;
                                            // check if there is a weapon type that all weapons are of
                                            for (const w in potentialTypes) 
                                            {
                                                this.logger.log(`Checking type ${w}`)
                                                this.logger.log(`potentialTypes[${w}].length ${potentialTypes[w].length} newWeaponCondition.length ${newWeaponCondition.length}`)

                                                if (potentialTypes[w].length > newWeaponCondition.length)
                                                {
                                                    this.logger.log(`Type ${w} has more weapons than the condition`)
                                                    this.logger.plusIndent();
                                                    this.logger.log(potentialTypes[w])
                                                    this.logger.log("---")
                                                    this.logger.log(newWeaponCondition)
                                                    this.logger.minusIndent();
                                                    this.logger.log(`Extra weapons: ${potentialTypes[w].filter(i=>!newWeaponCondition.includes(i)).join(", ")}`)
                                                    this.logger.log("\n\n")
                                                }
                                                if (potentialTypes[w].length === newWeaponCondition.length) 
                                                {
                                                    if (weaponType == null)
                                                    {
                                                        weaponType = w;
                                                    }
                                                    else 
                                                    {
                                                        
                                                        if (this.config.kindOf[w] == weaponType // take the most restrictive type
                                                            ||this.weaponsTypes[weaponType].length > this.weaponsTypes[w].length // take the most restrictive type
                                                            ||this.overridedSettings.customCategories[w] && !this.overridedSettings.customCategories[weaponType] // take the custom category
                                                        )
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
                                            //#endregion
                                            if (weaponType == null && bestCandidate != null)
                                            {
                                                this.logger.log(`Quest ${questId} best candidate: \n\tType: ${bestCandidate.type}\n\tWeapons: ${bestCandidate.weapons.join(", ")}\n\tMissing: ${bestCandidate.missing.join(", ")}`);
                                            }
                                            if (weaponType)
                                            {
                                            // add the missing weapons
                                                for (const w of this.weaponsTypes[weaponType]) 
                                                {
                                                // probably just assign the array directly but just to be sure 
                                                    if (pushIfNotExists(newWeaponCondition, w))
                                                    {
                                                        this.logger.logDebug(`Added weapon of type ${weaponType}: ${w}\n`);
                                                    }
                                                }
                                            }
                                        }
                                        else if (newWeaponCondition.length === 1)
                                        {
                                            this.logger.log(`Quest ${questId} has only one weapon: ${condition.counter.conditions[cI].weapon[0]}. Only adding/processing white/black listed weapons`);
                                        }
      
                                        processWhiteListed(questId);
                                        processCanBeUsedAs();
                                        processBlackListed(questId);
                                    }


                                    // could've just intersected the arrays but that would be keeping it simple and we don't do that here
                                    const checkChangesAndformatToPrint = (orig: string[], newW: string[]) : [boolean, string]  =>  
                                    {
                                        let str = "";

                                        orig.sort((a,b) => 
                                        {
                                            const aIndex = newW.indexOf(a);
                                            const bIndex = newW.indexOf(b);

                                            if (aIndex === -1 && bIndex === -1)
                                            {
                                                return a.localeCompare(b);
                                            }

                                            if (aIndex === -1)
                                            {
                                                return -1;
                                            }

                                            if (bIndex === -1)
                                            {
                                                return 1;
                                            }

                                            return a.localeCompare(b);
                                        });

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

                                        let iO = 0;
                                        let iD = 0;

                                        let k = 0
                                        for (; iO < orig.length && iD < newW.length;) 
                                        {
                                            if (orig[iO] === newW[iD]) 
                                            {
                                                str += `\t${orig[iO]}\n`;
                                                iO++;
                                                iD++;
                                                k = iO;
                                            }
                                            else if (k===0)
                                            {
                                                str += `\t\t--- ${orig[iO]}\n`;
                                                iO++;
                                            } 
                                            else 
                                            {
                                            // this should never happen
                                                this.logger.error(`Quest ${questId} Condition: ${condition.id} (${condition.counter.conditions[cI].id}) - \nError in checkChangesAndformatToPrint. iO: ${iO} iD: ${iD} k: ${k}\n`);
                                                this.logger.log(orig, LogType.FILE, true);
                                                this.logger.log(newW, LogType.FILE, true);
                                                break;
                                            }
                                        }

                                        const isSame = iO === orig.length && iD === newW.length;
                                        // loggically this should never happen
                                        for (; iO < orig.length; iO++) 
                                        {
                                            str += `\t\t--- ${orig[iO]}\n`;
                                        }

                                        for (; iD < newW.length; iD++) 
                                        {
                                            str += `\t\t+++ ${newW[iD]}\n`;
                                        }

                                        return [isSame, str];
                                    }

                                    const [isSame, weaponsChangesLog] = checkChangesAndformatToPrint(original,newWeaponCondition);

                                    if (!isSame)
                                    {
                                        this.logger.log(`Quest: ${questId} - Chosen Weapon Type: ${weaponType}\n${weaponsChangesLog}}`);
                                        condition.counter.conditions[cI].weapon = newWeaponCondition;
                                    }
                                    else 
                                    {
                                        this.logger.logDebug(`Quest: ${questId} - No changes\n\tOriginal: ${original.join(", ")} `)
                                    }
                                }
                                catch (e)
                                {
                                    this.logger.error(`An error occurred in quest ${questId} - Condition: ${condition.id}`);
                                    this.logger.log(condition.counter.conditions[cI], LogType.FILE, false);
                                    this.logger.error(e.stack);
                                }
                            } 
                        }
                        catch (e) 
                        {
                            this.logger.error(`An error occurred in quest ${questId} - Condition: ${condition.id}`);
                            this.logger.error(e.stack);
                        }
                    }
                    
                })            
            }    
        }
        catch (e)
        {
            this.logger.error(e.stack);
        }
    }
}