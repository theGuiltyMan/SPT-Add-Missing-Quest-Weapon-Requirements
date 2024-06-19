export interface IQuestOverride 
{
    id : string, // id or partial name of the quest
    skip: boolean, // will skip this quest if true
    onlyUseWhiteListedWeapons: boolean, // if true, only weapons in whiteListedWeapons will be used
    whiteListedWeapons: Set<string>, // these weapons will be added to the quest no matter what
    blackListedWeapons: Set<string>,  // these weapons will be removed from the quest no matter what
}