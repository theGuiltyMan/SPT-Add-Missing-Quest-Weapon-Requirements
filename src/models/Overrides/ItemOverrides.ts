import {IWeaponCategory} from "../ConfigFiles/IWeaponCategory"

export class ItemOverrides 
{
    public CanBeUsedAsShortNameWhitelist: string[] = [];
    public CanBeUsedAsShortNameBlacklist: string[] = [];
    public Override: Record<string, string> = {};
    public CanBeUsedAs: Record<string, string[]> = {};
    public CustomCategories: IWeaponCategory[] = [];
}
  
  