export interface IQuestOverride 
{
    id : string, // id or partial name of the quest
    skip?: boolean, // ill skip this quest if true (only weapons in the CanBeUsedAs will be added to the quest)
    onlyUseWhiteListedWeapons?: boolean, // if true, only weapons in whiteListedWeapons will be used
    whiteListedWeapons?: string[], // these weapons will be added to the quest no matter what
    blackListedWeapons?: string[],  // these weapons will be removed from the quest no matter what
    blackListed?: boolean // if true, this quest will not be processed
}