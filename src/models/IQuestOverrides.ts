import { IQuestOverrideSetting } from "./IQuestOverrideSetting";

export interface IQuestOverrides 
{
    BlackListedQuests: string[],
    Overrides: IQuestOverrideSetting[]
}