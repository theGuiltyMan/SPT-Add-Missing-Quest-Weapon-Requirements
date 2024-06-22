import { IQuestOverride } from "./IQuestOverride";
import { IWeaponCategory } from "./IWeaponCategory";

export class OverridedSettings 
{
    public questOverrides: Record<string, IQuestOverride> = {};
    public overriddenWeapons: Record<string, string> = {};
    public canBeUsedAs: Record<string, string[]> = {};
    public customCategories: Record<string, IWeaponCategory> = {};
}