import { IWeaponCategory } from "./IWeaponCategory"

export interface IOverriddenWeapons 
{
    CanBeUsedAsShortNameWhitelist: string[],
    CanBeUsedAsShortNameBlacklist: string[],
    Override: Record<string, string>
    CanBeUsedAs: Record<string, string[]>,
    CustomCategories : IWeaponCategory[]
}
  
  