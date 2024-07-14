import {ICustomCategory} from "./ICustomCategory"

export class ItemOverrides 
{
    public CanBeUsedAs: Record<string, string[]> = {};
    public CanBeUsedAsShortNameWhitelist: string[] = [];
    public CanBeUsedAsShortNameBlacklist: string[] = [];
    public Override: Record<string, string> = {};
    public CustomCategories: ICustomCategory[] = [];
}
  
  