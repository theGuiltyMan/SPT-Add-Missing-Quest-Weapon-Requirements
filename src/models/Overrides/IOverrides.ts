import {ItemOverrides} from "../ConfigFiles/ItemOverrides";
import {QuestOverrides} from "./QuestOverrides";

export interface IOverrides 
{
    questOverrides: QuestOverrides; // in case we add more overrides for quests
    weaponOverrides: ItemOverrides;
}

