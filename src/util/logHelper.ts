import { LogBackgroundColor } from "@spt-aki/models/spt/logging/LogBackgroundColor";
import { LogTextColor } from "@spt-aki/models/spt/logging/LogTextColor";
import { ILogger } from "@spt-aki/models/spt/utils/ILogger";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";
import { IAddMissingQuestRequirementConfig } from "../models/IAddMissingQuestRequirementConfig";
import { inject, injectable } from "tsyringe";
import fs from "fs";
import path from "path";

@injectable()
export class LogHelper
{

    private locale: Record<string, string>
    private logger: ILogger
    private logType: LogType
    private log :string ="";
    private indent: string ="";
    private defaultTextColor : LogTextColor = LogTextColor.CYAN
    private defaultBackgroundColor : LogBackgroundColor = LogBackgroundColor.MAGENTA


    private writtenToFile : boolean = false;
    private logFile: fs.WriteStream;

    constructor(
        @inject("DatabaseServer") private db: DatabaseServer,
        @inject("WinstonLogger") private winstonLogger: ILogger,
        @inject("AMQRConfig") private config: IAddMissingQuestRequirementConfig
    )
    {
        this.locale = db.getTables().locales.global["en"]; 
        this.logger = winstonLogger;
        this.logType = LogType[this.config.logType.toUpperCase() as keyof typeof LogType] || LogType.FILE;
        
        // delete the log file if it exists
        if (fs.existsSync(this.config.logPath))
        {
            fs.rmSync(this.config.logPath);
        }
        this.logFile = fs.createWriteStream(this.config.logPath, {flags: "a"});
    }

    stringify(obj: any): string
    {
        return this.convertAllToReadable(JSON.stringify(obj, 
            (_key, value) => (value instanceof Set ? [...value] : value), 4));
    }

    convertAllToReadable(str: string) : string
    {
        // for every word in the string, try to convert it to readable, for debugging purposes
        const words = str.split(" ");
        let newStr = "";
        for (const word of words)
        {
            const [converted, success] = this.tryToConvertToReadable(word);
            newStr += success ? converted : word;
            newStr += " ";
        }

        return newStr;
    }

    error(error: string)  : void
    {
        this.logger.error(this.convertAllToReadable(error));
    }

    tryToConvertToReadable(potentialId: string) : [string, boolean]
    {
        const name = this.locale[`${potentialId} Name`] || this.locale[`${potentialId} name`] || "Unknown";
        return [`${name} (${potentialId})`, name !== "Unknown"];
    }

    asReadable(id: string) : string 
    {
        return this.tryToConvertToReadable(id)[0];
    }

    addLog(s: string | object, forceType : LogType = LogType.NONE ) : void
    {
        if (typeof s !== "string" &&typeof s === "object")
        {
            s = this.stringify(s);
        }
        const logType = forceType !== LogType.NONE  ? forceType  : this.logType ;
        if ((logType & LogType.FILE) === LogType.FILE)
        {
            this.addLog(`Writing to log file... ${this.logFile.path}`, LogType.CONSOLE);
            // this.log += this.indent + s + "\n";
            this.logFile.write(this.indent + s + "\n");
        }
        if ((logType & LogType.CONSOLE) === LogType.CONSOLE)
        {
            this.logger.logWithColor(s, this.defaultTextColor, this.defaultBackgroundColor);
        }
    }

    plusIndent() : void
    {
        this.indent += "\t";
    }

    minusIndent() : void
    {
        if (this.indent.length > 0)
            this.indent = this.indent.slice(0, -1);
    }
}

export enum LogType 
    {
    NONE = 0,
    CONSOLE = 1 << 0,
    FILE  = 1 << 1,
    ALL  = CONSOLE | FILE
}
