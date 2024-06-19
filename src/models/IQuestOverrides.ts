import { IOverriddenQuest } from "./IOverriddenQuests";

export interface IQuestOverrides 
{
    BlackListedQuests: string[],
    Overrides: IOverriddenQuest[]
}