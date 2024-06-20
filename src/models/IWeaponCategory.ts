export interface IWeaponCategory 
{
    name : string,
    ids : Set<string>,
    whiteListedKeywords : Set<string>,
    blackListedKeywords : Set<string>,
}