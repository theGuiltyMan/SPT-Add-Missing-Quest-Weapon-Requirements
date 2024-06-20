import { DependencyContainer } from "tsyringe";
import { IPostDBLoadMod } from "@spt-aki/models/external/IPostDBLoadMod";
import { ILogger } from "@spt-aki/models/spt/utils/ILogger";
import { DatabaseServer } from "@spt-aki/servers/DatabaseServer";
import { VFS } from "@spt-aki/utils/VFS";

import path from "path";
import { readJson} from "./util/jsonHelper";
import {LogHelper} from "./util/logHelper";
import { IAddMissingQuestRequirementConfig } from "./models/IAddMissingQuestRequirementConfig";
import { WeaponCategorizer } from "./weaponCategorizer";
import { QuestPatcher } from "./questPatcher";
import { OverrideReader } from "./overrideReader";


class Mod implements IPostDBLoadMod 
{
    private databaseServer: DatabaseServer;
    private vfs: VFS;
    // private logger: ILogger;
    private config: any;
    private logger: ILogger;


    public postDBLoad(container: DependencyContainer): void 
    {
    // Database will be loaded, this is the fresh state of the DB so NOTHING from the AKI
    // logic has modified anything yet. This is the DB loaded straight from the JSON files

        const childContainer = container.createChildContainer();
        childContainer.register<LogHelper>("LogHelper", LogHelper);


        // read config and register it
        const config = readJson<IAddMissingQuestRequirementConfig>(path.resolve(__dirname, "../config/config.jsonc"))
        if (!config.logPath)
        {
            config.logPath = path.resolve(__dirname, "../log.log");
        }

        childContainer.registerInstance<IAddMissingQuestRequirementConfig>("AMQRConfig", config);
        childContainer.register<OverrideReader>("OverrideReader", OverrideReader)
        childContainer.register<WeaponCategorizer>("WeaponCategorizer", WeaponCategorizer)
        childContainer.register<QuestPatcher>("QuestPatcher", QuestPatcher)
        childContainer.registerInstance<string>("modDir", path.resolve(__dirname, "../../"))


        const run = () => 
        {
            childContainer.resolve<OverrideReader>("OverrideReader").run();
            childContainer.resolve<WeaponCategorizer>("WeaponCategorizer").run();   
            childContainer.resolve<QuestPatcher>("QuestPatcher").run()
        }

        if (!config.delay || config.delay <= 0)
        {
            run();
        }
        else 
        {
            setTimeout(() => run()
                , config.delay * 1000);
        }
        return;
    }
}

module.exports = { mod: new Mod() };
