import "reflect-metadata";
import { QuestPatcher } from "../../src/questPatcher";
import { LogHelper } from "../../src/util/logHelper";
import { IAddMissingQuestRequirementConfig } from "../../src/models/IAddMissingQuestRequirementConfig";
import { DatabaseServer } from "../../types/servers/DatabaseServer";
import { OverridedSettings } from "../../src/models/OverridedSettings";
import { JsonUtil } from "@spt/utils/JsonUtil";
import { IQuest, IQuestCondition } from "@spt/models/eft/common/tables/IQuest";
import { describe, it, expect, beforeEach, jest } from "@jest/globals";

const createWeaponCondition = (weapons: string[]): IQuestCondition => ({
    id: "weapon_cond_id",
    counter: {
        id: "counter_id",
        conditions: [
            {
                weapon: weapons
                // other properties
            }
        ]
    }
    
    // other properties
} as unknown as IQuestCondition);

describe("QuestPatcher", () => 
{
    let questPatcher: QuestPatcher;
    let mockLogger: jest.Mocked<LogHelper>;
    let mockConfig: IAddMissingQuestRequirementConfig;
    let mockDatabaseServer: jest.Mocked<DatabaseServer>;
    let mockWeaponTypes: Record<string, string[]>;
    let mockWeaponToType: Record<string, string[]>;
    let mockOverridedSettings: OverridedSettings;
    let mockJsonUtil: jest.Mocked<JsonUtil>;
    let mockQuests: Record<string, IQuest>;

    beforeEach(() => 
    {
        jest.clearAllMocks();

        mockLogger = {
            log: jest.fn().mockImplementation((message) => 
            {
                // console.log("Logger Info:", message);
            }),
            error: jest.fn().mockImplementation((message) => 
            {
                console.log("Logger Error:", message);
            }),
            logDebug: jest.fn().mockImplementation((message) => 
            {
                // console.log("Logger Debug:", message);
            }),
            plusIndent: jest.fn(),
            minusIndent: jest.fn()
        } as unknown as jest.Mocked<LogHelper>;

        mockConfig = {
            debug: false,
            kindOf: { "Revolver": "Pistol" },
            BlackListedItems: [],
            BlackListedWeaponsTypes: [],
            categorizeWithLessRestrive: false,
            amountNeededForWeaponType: 0,
            delay: 0,
            logType: "none",
            logPath: ""
        } as IAddMissingQuestRequirementConfig;

        mockWeaponTypes = {
            "Pistol": ["pistol_pm", "revolver_rhino"],
            "Revolver": ["revolver_rhino"],
            "AssaultRifle": ["m4a1", "ak74"]
        };

        mockWeaponToType = {
            "pistol_pm": ["Pistol"],
            "revolver_rhino": ["Pistol", "Revolver"],
            "m4a1": ["AssaultRifle"],
            "ak74": ["AssaultRifle"]
        };

        mockOverridedSettings = new OverridedSettings();

        mockJsonUtil = {
            clone: jest.fn(x => JSON.parse(JSON.stringify(x)))
        } as unknown as jest.Mocked<JsonUtil>;

        mockQuests = {};
        mockDatabaseServer = {
            getTables: jest.fn().mockReturnValue({
                templates: { quests: mockQuests }
            })
        } as unknown as jest.Mocked<DatabaseServer>;

        questPatcher = new QuestPatcher(
            mockLogger,
            mockConfig,
            null, // weaponCategorizer not directly used in methods
            mockDatabaseServer,
            mockWeaponTypes,
            mockWeaponToType,
            mockOverridedSettings,
            mockJsonUtil
        );
    });

    it("should not expand a single-weapon condition to its full category", () => 
    {
        mockQuests["quest_1"] = {
            _id: "quest_1",
            conditions: { AvailableForStart: [createWeaponCondition(["pistol_pm"])] }
        } as IQuest;
        mockWeaponToType["pistol_pm"] = ["Pistol"];
        mockWeaponTypes["Pistol"] = ["pistol_pm", "revolver_rhino", "glock"];

        questPatcher.run();

        const patchedWeapons = mockQuests["quest_1"].conditions.AvailableForStart[0].counter.conditions[0].weapon;
        expect(patchedWeapons).toHaveLength(1);
        expect(patchedWeapons).toContain("pistol_pm");
    });

    it("should determine the most restrictive weapon type and expand", () => 
    {
        mockQuests["quest_1"] = {
            _id: "quest_1",
            conditions: { AvailableForStart: [createWeaponCondition(["pistol_pm", "revolver_rhino"])] }
        } as IQuest;
        mockWeaponToType["pistol_pm"] = ["Pistol"];
        mockWeaponToType["revolver_rhino"] = ["Pistol", "Revolver"];
        mockWeaponTypes["Pistol"] = ["pistol_pm", "revolver_rhino"];
        mockWeaponTypes["Revolver"] = ["revolver_rhino"]; // Only one revolver exists

        questPatcher.run();

        const patchedWeapons = mockQuests["quest_1"].conditions.AvailableForStart[0].counter.conditions[0].weapon;
        // It should have identified "Pistol" as the type and added all pistols.
        // Since the original list already contained all pistols, no change.
        expect(patchedWeapons).toHaveLength(2);
        expect(patchedWeapons).toContain("pistol_pm");
        expect(patchedWeapons).toContain("revolver_rhino");
        // Check that the chosen type was Pistol
        expect(mockLogger.logDebug).toHaveBeenCalledWith(expect.stringContaining("Chosen Weapon Type: Pistol"));
    });

    it("should apply 'canBeUsedAs' overrides", () => 
    {
        mockQuests["quest_1"] = {
            _id: "quest_1",
            conditions: { AvailableForStart: [createWeaponCondition(["m4a1"])] }
        } as IQuest;
        mockOverridedSettings.canBeUsedAs["m4a1"] = ["m4a1_modded"];

        questPatcher.run();

        const patchedWeapons = mockQuests["quest_1"].conditions.AvailableForStart[0].counter.conditions[0].weapon;
        expect(patchedWeapons).toContain("m4a1");
        expect(patchedWeapons).toContain("m4a1_modded");
    });

    it("should apply quest-specific whitelists and blacklists", () => 
    {
        mockQuests["quest_1"] = {
            _id: "quest_1",
            conditions: { AvailableForStart: [createWeaponCondition(["pistol_pm", "revolver_rhino"])] }
        } as IQuest;
        mockOverridedSettings.questOverrides["quest_1"] = [{
            id: "quest_1",
            whiteListedWeapons: ["whitelisted_gun"],
            blackListedWeapons: ["pistol_pm"]
        }];

        questPatcher.run();

        const patchedWeapons = mockQuests["quest_1"].conditions.AvailableForStart[0].counter.conditions[0].weapon;
        expect(patchedWeapons).not.toContain("pistol_pm");
        expect(patchedWeapons).toContain("revolver_rhino");
        expect(patchedWeapons).toContain("whitelisted_gun");
    });

    it("should skip a quest if blacklisted", () => 
    {
        mockQuests["quest_1"] = {
            _id: "quest_1",
            conditions: { AvailableForStart: [createWeaponCondition(["pistol_pm"])] }
        } as IQuest;
        mockOverridedSettings.questOverrides["quest_1"] = [{ id: "quest_1", blackListed: true }];

        questPatcher.run();

        // No changes should be made, so the weapon list is original
        const patchedWeapons = mockQuests["quest_1"].conditions.AvailableForStart[0].counter.conditions[0].weapon;
        expect(patchedWeapons).toEqual(["pistol_pm"]);
        expect(mockLogger.logDebug).toHaveBeenCalledWith(expect.stringContaining("Skipping"));
    });

    it("should only use whitelisted weapons if 'onlyUseWhiteListedWeapons' is true", () => 
    {
        mockQuests["quest_1"] = {
            _id: "quest_1",
            conditions: { AvailableForStart: [createWeaponCondition(["pistol_pm"])] }
        } as IQuest;
        mockOverridedSettings.questOverrides["quest_1"] = [{
            id: "quest_1",
            onlyUseWhiteListedWeapons: true,
            whiteListedWeapons: ["god_gun_1", "god_gun_2"]
        }];

        questPatcher.run();

        const patchedWeapons = mockQuests["quest_1"].conditions.AvailableForStart[0].counter.conditions[0].weapon;
        expect(patchedWeapons).toEqual(["pistol_pm", "god_gun_1", "god_gun_2"]); // original weapons are kept
    });

    it("should handle weapons not found in the database gracefully", () => 
    {
        mockQuests["quest_1"] = {
            _id: "quest_1",
            conditions: { AvailableForStart: [createWeaponCondition(["pistol_pm", "not_a_real_gun"])] }
        } as IQuest;

        questPatcher.run();

        const patchedWeapons = mockQuests["quest_1"].conditions.AvailableForStart[0].counter.conditions[0].weapon;
        expect(patchedWeapons).not.toContain("not_a_real_gun");
        expect(mockLogger.log).toHaveBeenCalledWith(expect.stringContaining("The following weapons were not found in database by the mod while processing quest quest_1: not_a_real_gun"));
    });
});
