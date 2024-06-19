import { IWeaponCategory } from "./IWeaponCategory"

export interface IOverriddenWeapons 
{
    Override: Record<string, string>
    CanBeUsedAs: Record<string, string[]>,
    CustomCategories : IWeaponCategory[]
}
  
  