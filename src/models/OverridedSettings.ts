import { IQuestOverride } from "./IQuestOverride";
import { IWeaponCategory } from "./IWeaponCategory";

export class OverridedSettings 
{
    public questOverrides: Record<string, IQuestOverride[]> = {};
    public overriddenWeapons: Record<string, string> = {};
    public canBeUsedAs: Record<string, string[]> = {};
    public customCategories: Record<string, IWeaponCategory> = {};
    public canBeUsedAsShortNameWhitelist: string[] = [];
    public canBeUsedAsShortNameBlackList: string[] = [];

    public getOverrideForQuest(questId: string, conditionId?: string): IQuestOverride 
    {
        const overrides = this.questOverrides[questId];
        if (!overrides || overrides.length === 0) 
        {
            return null;
        }

        // Look for a specific override for the given conditionId
        if (conditionId) 
        {
            const specificOverride = overrides.find(o => o.condition === conditionId);
            if (specificOverride) 
            {
                return specificOverride;
            }
        }

        // If no specific override is found (or no conditionId was provided), look for a generic one.
        const genericOverride = overrides.find(o => !o.condition || o.condition.length === 0);
        
        return genericOverride || null;
    }
}