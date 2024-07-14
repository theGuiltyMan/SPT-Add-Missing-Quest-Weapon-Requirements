export interface IAddMissingQuestRequirementConfig 
{
    debug: any
    kindOf: Record<string, string>
    BlackListedItems: string[]
    BlackListedWeaponsTypes: string[]
    categorizeWithLessRestrive: boolean
    amountNeededForWeaponType: number
    defaultTypes: Record<string, string[]>
    delay: number
    logType: string,
    logPath: string,
}