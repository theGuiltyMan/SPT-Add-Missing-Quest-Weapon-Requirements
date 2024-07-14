import { DatabaseServer } from "@spt/servers/DatabaseServer";
import { inject, injectable } from "tsyringe";

@injectable()
export class LocaleHelper
{

    private locale: Record<string, string>

    constructor(
        @inject("DatabaseServer") private db: DatabaseServer
    )
    {
        this.locale = db.getTables().locales.global["en"]; // todo currently hardcoded
    }


    public getName(id: string) : string
    {
        return this.locale[`${id} name`] || this.locale[`${id} Name`] || "";
    }
    public getDescription(id: string) : string
    {
        return this.locale[`${id} description`] || this.locale[`${id} Description`] || "";

    }

    public getShortName(id: string) : string
    {
        return this.locale[`${id} shortName`] || this.locale[`${id} ShortName`] || "";
    }
}