﻿import {inject, injectable} from "tsyringe";
import {ITemplateItem} from "@spt/models/eft/common/tables/ITemplateItem";
import {DatabaseServer} from "@spt/servers/DatabaseServer";
import {IOverrides} from "./models/Overrides/IOverrides";
import {LocaleHelper} from "./util/localeHelper";
import {LogHelper} from "./util/logHelper";
import {IAddMissingQuestRequirementConfig} from "./models/ConfigFiles/IAddMissingQuestRequirementConfig";

interface ItemTypeDefinition 
{
    base: string;
    regex: RegExp;
}

export interface ItemType 
{
    base: string,
    type: string
}

@injectable()
export class ItemRepository 
{
    private readonly _allItems: Record<string, ITemplateItem>;
    private readonly _typeDefinitions: ItemTypeDefinition[] = [];
    private readonly _pathCache: Record<string, [string, ITemplateItem[]]> = {};
    private readonly _typeCache: Record<string, ItemType> = {};

    constructor(
        @inject("DatabaseServer") protected databaseServer: DatabaseServer,
        @inject("OverridedSettings") protected overridedSettings: IOverrides,
        @inject("LocaleHelper") protected localeHelper: LocaleHelper,
        @inject("LogHelper") protected logger: LogHelper,
        @inject("AMQRConfig") protected config: IAddMissingQuestRequirementConfig
    ) 
    {
        this._allItems = Object.freeze({...this.databaseServer.getTables().templates.items});
        for (const key in config.defaultTypes) 
        {

            for (const matchString of config.defaultTypes[key]) 
            {
                this._typeDefinitions.push({base: key, regex: new RegExp(matchString, "i")});
            }
        }
    }

    public isItem(id: string): boolean 
    {
        const item = this._allItems[id];
        
        return item && (item._type !== "Item" || !item._props)
    }

    
    
    private asItem(item: ITemplateItem | string): ITemplateItem 
    {
        if (typeof item === "string") 
        {
            return this._allItems[item];
        }
        return item;
    }

    public get allItems(): Readonly<Record<string, ITemplateItem>> 
    {
        return this._allItems;
    }

    public getItem(id: string): ITemplateItem 
    {
        return this._allItems[id];
    }

    public getParent(item: ITemplateItem | string): ITemplateItem 
    {
        try 
        {
            item = this.asItem(item);
            return this._allItems[item._parent];
        }
        catch (error) 
        {
            return null;
        }
    }

    public tryGetType(item: ITemplateItem | string): [boolean, ItemType] 
    {
        item = this.asItem(item);
        if (!item) 
        {
            return [false, null];
        }
        const cached = this._typeCache[item._id];
        if (cached !== undefined) 
        {
            return [cached != null, this._typeCache[item._id]];

        }
        const [path, _] = this.getParentPath(item);
        if (!path) 
        {
            this._typeCache[item._id] = null;
            return [false, null];
        }

        for (const typeDef of this._typeDefinitions) 
        {
            const match = typeDef.regex.exec(path);
            if (match) 
            {
                const t = {base: typeDef.base, type: match[0]};
                this._typeCache[item._id] = t;
                this.logger.logDebug(`Item: ${item._id} - ${item._name} - ${t.type}`);
                return [true, t];
            }
        }
        this._typeCache[item._id] = null;
        return [false, null];
    }

    public getParentPath(item: ITemplateItem | string): [string, ITemplateItem[]] 
    {
        item = this.asItem(item);
        if (!item) 
        {
            return null;
        }

        if (this._pathCache[item._id]) 
        {
            return this._pathCache[item._id];
        }
        const path = [];
        let current = this.asItem(item);
        while (current) 
        {
            path.push(current);
            current = this.getParent(current);
        }

        path.reverse()
        const pathAsStrings = path.map(p => `${p._name || p._props.Name}`).join("/");
        this._pathCache[item._id] = [pathAsStrings, path];
        return this._pathCache[item._id];
    }
}