import { inject, injectable } from "tsyringe";
import { LogHelper, LogType } from "./util/logHelper";

@injectable()
export  class WeaponCategorizer 
{
    constructor(
        @inject("LogHelper") protected logHelper: LogHelper,
        @inject("DatabaseServer") protected databaseServer: DatabaseServer,
        @inject("AMQRConfig") protected config: IAddMissingQuestRequirementConfig
    )
    {

    }
    run():void
    {
        this.logHelper.addLog("Hello from WeaponCategorizer", LogType.CONSOLE);
    }
}