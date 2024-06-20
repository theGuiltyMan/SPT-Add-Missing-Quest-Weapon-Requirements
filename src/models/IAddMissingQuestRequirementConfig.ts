export interface IAddMissingQuestRequirementConfig 
{
    kindOf: Record<string, string>
    BlackListedItems: string[]
    BlackListedWeaponsTypes: string[]
    categorizeWithLessRestrive: boolean
    amountNeededForWeaponType: number
    delay: number
    logType: string,
    logPath: string,
}