export interface IQuestOverrideSetting
{
    id : string,                                     // id or partial name of the quest
    skip?: boolean,                                  // will skip this quest if true (only weapons in the CanBeUsedAs will be added to the quest)
    onlyUseWhiteListedWeapons?: boolean,            // if true, only weapons in whiteListedWeapons will be used
    whiteListedWeapons?: string[],              // these weapons will be added to the quest no matter what
    blackListedWeapons?: string[],     
}