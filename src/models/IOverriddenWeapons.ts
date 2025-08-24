import { IWeaponCategory } from "./IWeaponCategory"
import { OverrideBehaviour } from "./OverrideBehaviour"
import { Overridable } from "./Overridable";

export interface IOverriddenWeapons 
{
    OverrideBehaviour?: OverrideBehaviour,
    CanBeUsedAsShortNameWhitelist: Overridable<string>[],
    CanBeUsedAsShortNameBlacklist: Overridable<string>[],
    Override: Record<string, Overridable<string>>
    CanBeUsedAs: Record<string, Overridable<string>[]>,
    CustomCategories : Overridable<IWeaponCategory>[]
}