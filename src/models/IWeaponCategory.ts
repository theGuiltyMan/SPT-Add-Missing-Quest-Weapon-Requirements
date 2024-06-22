export interface IWeaponCategory 
{
    name : string, // Identifier for the category
    ids? : string[],  // if a weapon is in this list, it will be considered as this category no matter what
    alsoCheckDescription? : boolean, // if true, the description of the weapon will be checked for keywords
    whiteListedKeywords? : string[], // regex. required keywords for the weapon to be considered as this category, if empty skipped
    blackListedKeywords? : string[], // regex. if any of these keywords are found, the weapon will not be considered as this category, if empty skipped
    allowedCalibres?: string[] // only weapons with these calibres will be considered as this category, if empty skipped
}