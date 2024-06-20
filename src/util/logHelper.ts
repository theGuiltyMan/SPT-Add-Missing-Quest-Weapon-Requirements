import { ILogger } from "@spt-aki/models/spt/utils/ILogger";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";
import { inject, injectable } from "tsyringe";

@injectable()
export class LogHelper
{
    constructor(
    )
    {
        console.log("@@@@@@@@@@@@@LogHelper constructor@@@@@@@@@@@@@");
    }

    stringify(obj: any): string
    {
        return JSON.stringify(obj, 
            (_key, value) => (value instanceof Set ? [...value] : value), 4);
    }

    getPrintable(id: string) : string 
    {
        return id; //todo
        // const name = this.locale[`${id} Name`] || this.locale[`${id} name`] || "Unknown";
        // return `${name} (${id})`;
    }
}

