import { IQuestOverrideSetting } from "./IQuestOverrideSetting";
import { OverrideBehaviour } from "./OverrideBehaviour";

export interface IQuestOverrides 
{
    OverrideBehaviour?: OverrideBehaviour,
    BlackListedQuests: string[],
    Overrides: IQuestOverrideSetting[]
}