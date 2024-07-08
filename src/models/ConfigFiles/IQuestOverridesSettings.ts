import { IQuestOverride } from "../Overrides/IQuestOverride";

export interface IQuestOverridesSettings 
{
    BlackListedQuests: string[];
    Overrides: IQuestOverride[];
}