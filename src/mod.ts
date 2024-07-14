import {DependencyContainer} from "tsyringe";
import {IPostDBLoadMod} from "@spt-aki/models/external/IPostDBLoadMod";

import path from "path";
import {readJson} from "./util/jsonHelper";
import {LogHelper, LogType} from "./util/logHelper";
import {IAddMissingQuestRequirementConfig} from "./models/ConfigFiles/IAddMissingQuestRequirementConfig";
import {WeaponCategorizer} from "./weaponCategorizer";
import {QuestPatcher} from "./questPatcher";
import {OverrideReader} from "./overrideReader";
import {LocaleHelper} from "./util/localeHelper";
import {ItemRepository} from "./itemRepository";

// taken from https://stackoverflow.com/a/44124383
function timer() 
{
    const timeStart = new Date().getTime();
    return {
        /** <integer>s e.g 2s etc. */
        get seconds() 
        {
            return Math.ceil((new Date().getTime() - timeStart) / 1000) + "s";
        },
        /** Milliseconds e.g. 2000ms etc. */
        get ms() 
        {
            return (new Date().getTime() - timeStart) + "ms";
        }
    }
}
class Mod implements IPostDBLoadMod 
{

    
    
    public postDBLoad(container: DependencyContainer): void 
    {
        const t = timer();

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
        childContainer.registerSingleton<ItemRepository>("ItemRepository", ItemRepository)
        childContainer.registerSingleton<OverrideReader>("OverrideReader", OverrideReader)
        childContainer.registerSingleton<WeaponCategorizer>("WeaponCategorizer", WeaponCategorizer)
        childContainer.registerSingleton<QuestPatcher>("QuestPatcher", QuestPatcher)

        const run = () => 
        {
            childContainer.resolve<OverrideReader>("OverrideReader").run(childContainer);
            // todo
            // childContainer.resolve<WeaponCategorizer>("WeaponCategorizer").run(childContainer);   
            childContainer.resolve<QuestPatcher>("QuestPatcher").run()
            logger.log(`[AMQWR] Finished Patching. Took ${t.ms}`, LogType.CONSOLE, false);
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
