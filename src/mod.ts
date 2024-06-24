import { DependencyContainer } from "tsyringe";
import { IPostDBLoadMod } from "@spt-aki/models/external/IPostDBLoadMod";

import path from "path";
import { readJson} from "./util/jsonHelper";
import {LogHelper, LogType} from "./util/logHelper";
import { IAddMissingQuestRequirementConfig } from "./models/IAddMissingQuestRequirementConfig";
import { WeaponCategorizer } from "./weaponCategorizer";
import { QuestPatcher } from "./questPatcher";
import { OverrideReader } from "./overrideReader";
import { LocaleHelper } from "./util/localeHelper";


class Mod implements IPostDBLoadMod 
{

    

    public postDBLoad(container: DependencyContainer): void 
    {


        // Database will be loaded, this is the fresh state of the DB so NOTHING from the AKI
        // logic has modified anything yet. This is the DB loaded straight from the JSON files

        const childContainer = container.createChildContainer();
        


        // read config and register it
        const config = readJson<IAddMissingQuestRequirementConfig>(path.resolve(__dirname, "../config/config.jsonc"))
        if (!config.logPath)
        {
            config.logPath = path.resolve(__dirname, "../log.log");
        }

        childContainer.registerInstance<IAddMissingQuestRequirementConfig>("AMQRConfig", config);
        childContainer.registerSingleton<LocaleHelper>("LocaleHelper", LocaleHelper)
        const logger = childContainer.registerSingleton<LogHelper>("LogHelper", LogHelper)
            .resolve<LogHelper>("LogHelper");

        logger.log("Starting mod");
        
        childContainer.registerInstance<string>("modDir", path.resolve(__dirname, "../../"))
        childContainer.registerSingleton<OverrideReader>("OverrideReader", OverrideReader)
        childContainer.registerSingleton<WeaponCategorizer>("WeaponCategorizer", WeaponCategorizer)
        childContainer.registerSingleton<QuestPatcher>("QuestPatcher", QuestPatcher)

        const run = () => 
        {
            childContainer.resolve<OverrideReader>("OverrideReader").run(childContainer);
            childContainer.resolve<WeaponCategorizer>("WeaponCategorizer").run(childContainer);   
            childContainer.resolve<QuestPatcher>("QuestPatcher").run()
            logger.log("[AMQWR] Finished Patching", LogType.CONSOLE, false);
            childContainer.dispose();

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
