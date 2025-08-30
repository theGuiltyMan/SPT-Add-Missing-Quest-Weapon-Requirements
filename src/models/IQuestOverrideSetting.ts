import { Overridable } from "./Overridable";
import { OverrideBehaviour } from "./OverrideBehaviour";

export interface IQuestOverrideSetting
{
    OverrideBehaviour?: OverrideBehaviour,
    id : string,                     // id or partial name of the quest
    skip?: boolean,                                  // will skip this quest if true (only weapons in the CanBeUsedAs will be added to the quest)
    onlyUseWhiteListedWeapons?: boolean,            // if true, only weapons in whiteListedWeapons will be used
    whiteListedWeapons?: Overridable<string>[],              // these weapons will be added to the quest no matter what
    blackListedWeapons?:  Overridable<string>[], 
    conditions?: string[] // the id of the conditions to apply this override to
}

