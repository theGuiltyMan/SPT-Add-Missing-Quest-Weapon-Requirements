import { LogBackgroundColor } from "@spt-aki/models/spt/logging/LogBackgroundColor";
import { LogTextColor } from "@spt-aki/models/spt/logging/LogTextColor";
import { ILogger } from "@spt-aki/models/spt/utils/ILogger";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";
import { IAddMissingQuestRequirementConfig } from "../models/IAddMissingQuestRequirementConfig";
import { inject, injectable } from "tsyringe";
import fs, { WriteStream } from "fs";
import { LocaleHelper } from "./localeHelper";
import { VFS } from "@spt-aki/utils/VFS";

@injectable()
export class LogHelper
{
    private logger: ILogger
    private logType: LogType
    private indentLiteral: string = "\t";
    private indent: string ="";
    private defaultTextColor : LogTextColor = LogTextColor.CYAN
    private defaultBackgroundColor : LogBackgroundColor = LogBackgroundColor.MAGENTA
    private logStream : WriteStream

    private logModName : string = "[Add Missing Quest Weapon Requirements]" // would be better to get this from the mod name

    constructor(
        @inject("DatabaseServer") private db: DatabaseServer,
        @inject("WinstonLogger") private winstonLogger: ILogger,
        @inject("AMQRConfig") private config: IAddMissingQuestRequirementConfig,
        @inject("LocaleHelper") private localeHelper: LocaleHelper,
        @inject("VFS") private vfs: VFS
    )
    {
        this.logger = winstonLogger;
        this.logType = LogType[this.config.logType.toUpperCase() as keyof typeof LogType] || LogType.FILE;
        
        // delete the log file if it exists
        if (fs.existsSync(this.config.logPath))
        {
            fs.rmSync(this.config.logPath);
        }
        this.logStream = fs.createWriteStream(this.config.logPath, { flags: "a" });
    }

    stringify(obj: any): string
    {
        return JSON.stringify(obj, 
            (_key, value) => (value instanceof Set ? [...value] : value), 4)
    }

    // convertAllToReadable(str: string) : string
    // {
    //     // for every word in the string, try to convert it to readable, for debugging purposes
    //     const words = str.split(" ");
    //     let newStr = "";
    //     for (const word of words)
    //     {
    //         // strip any special characters
    //         const stripped = word.match(/[a-zA-Z0-9]+/g);
            
    //         const [converted, success] = this.tryToConvertToReadable(stripped ? stripped[0] : word);
    //         newStr += success ? (stripped? word.replace(/[a-zA-Z0-9]+/g, converted) : converted) : word;
    //         newStr += " ";
    //     }

    //     return newStr;
    // }


    convertAllToReadable(str: string): string 
    {
        // Split by any whitespace character, including spaces, tabs, and new lines
        const words = str.split(/\s+/);
        const allSpaces = str.match(/\s+/g) || [];
        // sanity check to ensure there is one more word than spaces to not blow up the code for i have no idea how regex works
        const addSpaces = words.length === allSpaces.length + 1; 
        let newStr = "";
        // this.log(str, LogType.FILE, false)
        for (let i = 0; i < words.length; i++) 
        {   
            const word = words[i];
            // Strip any special characters
            const stripped = word.match(/[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*/g);
    
            const [converted, success] = this.tryToConvertToReadable(stripped ? stripped[0] : word);
            newStr += success ? (stripped ? word.replace(/[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*/g, converted) : converted) : word;
            if (!addSpaces) newStr += " ";
            else if (i < allSpaces.length) newStr += allSpaces[i]; // Add the original whitespace between words
        }
    
        return newStr;
        // // Preserve original whitespace by replacing spaces between words with the original whitespace found in the input
        // let whitespaceIndex = 0;
        // let preservedStr = "";
        // for (const match of str.matchAll(/\s+/g)) 
        // {
        //     const whitespace = match[0];
        //     const nextSlice = newStr.slice(whitespaceIndex, whitespaceIndex + whitespace.length);
        //     preservedStr += nextSlice + whitespace;
        //     whitespaceIndex += nextSlice.length + whitespace.length;
        // }
        // preservedStr += newStr.slice(whitespaceIndex); // Add the remaining part of the newStr
    
        // return preservedStr;
    }

    error(error: string)  : void
    {

        error = error || "Unknown error";
        error = `${this.logModName} ${error}`;
        this.logger.error("An error occurred in AddMissingQuestRequirements mod. Please check the 'log.log' file in the mod directory  for more information.")
        try 
        {
            this.logger.error(this.convertAllToReadable(error));
            this.logToFile(this.convertAllToReadable(error))
        }
        catch (e)
        {
            this.logger.error(error);
            this.logToFile(error)
        }
    }

    private blackListedIds: string[] = ["weapon"]
    tryToConvertToReadable(potentialId: string) : [string, boolean]
    {
        if (this.blackListedIds.includes(potentialId))
        {
            return [potentialId, false];
        }
        const name = this.localeHelper.getName(potentialId) || "Unknown";
        return [`${name} (${potentialId})`, name !== "Unknown"];
    }

    asReadable(id: string) : string 
    {
        return this.tryToConvertToReadable(id)[0];
    }

    logDebug(s: string | object, forceType : LogType = LogType.NONE, asReadable :boolean= true): void 
    {
        if (this.config.debug)
        {
            this.log(s, forceType, asReadable);
        }
    }

    // log to file
    logToFile(s: string) :void 
    {
        this.logStream.write(s);
    }

    log(s: string | object, forceType : LogType = LogType.NONE, asReadable :boolean= true) : void
    {
        const logType = forceType !== LogType.NONE  ? forceType  : this.logType ;
        if (logType === LogType.NONE) return

        if (typeof s !== "string" && typeof s === "object")
        {
            s = this.stringify(s);
        }


        if (asReadable)
        {
            s = this.convertAllToReadable(s);
        }

        if ((logType & LogType.FILE) === LogType.FILE)
        {
            // add indent to all lines
            // s = s.toString().split("\n").map((line) => this.indent + line).join("\n");
            this.logToFile(s + "\n");
        }
        if ((logType & LogType.CONSOLE) === LogType.CONSOLE)
        {
            s = `${this.logModName} ${s}`
            this.logger.logWithColor(s, this.defaultTextColor, this.defaultBackgroundColor);
        }
    }

    // why?
    plusIndent() : void
    {
        this.indent += this.indentLiteral;
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