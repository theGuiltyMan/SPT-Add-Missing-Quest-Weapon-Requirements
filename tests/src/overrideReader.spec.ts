import "reflect-metadata";
import { OverrideReader } from "../../src/overrideReader";
import { LogHelper } from "../../src/util/logHelper";
import { FileSystemSync } from "@spt/utils/FileSystemSync";
import { OverridedSettings } from "../../src/models/OverridedSettings";
import { IQuestOverrides } from "../../src/models/IQuestOverrides";
import { IOverriddenWeapons } from "../../src/models/IOverriddenWeapons";
import { OverrideBehaviour } from "../../src/models/OverrideBehaviour";
import * as jsonHelper from "../../src/util/jsonHelper";
import { DependencyContainer } from "tsyringe";
import { describe, it, expect } from "@jest/globals"
import path from "path";

// Mock dependencies
const mockLogger = {
    log: jest.fn(),
    error: jest.fn(),
    logDebug: jest.fn(),
    plusIndent: jest.fn(),
    minusIndent: jest.fn(),
    stringify: jest.fn(),
    convertAllToReadable: jest.fn().mockImplementation(str => str)
};

const mockVfs = {
    getDirectories: jest.fn(),
    exists: jest.fn()
} as unknown as FileSystemSync;

const modDir = "/path/to/mods";

describe("OverrideReader", () => 
{
    let overrideReader: OverrideReader;
    let container: DependencyContainer;

    beforeEach(() => 
    {
        jest.clearAllMocks();
        overrideReader = new OverrideReader(mockLogger as any, mockVfs, modDir);

        // a bit of a hack to get access to the private method
        (overrideReader as any).readOverrides = overrideReader["readOverrides"].bind(overrideReader);

        container = {
            registerInstance: jest.fn()
        } as unknown as DependencyContainer;
    });

    it("should be created", () => 
    {
        expect(overrideReader).toBeDefined();
    });

    describe("run", () => 
    {
        it("should call readOverrides and register the result", () => 
        {
            const settings = new OverridedSettings();
            const readOverridesSpy = jest.spyOn(overrideReader as any, "readOverrides").mockReturnValue(settings);

            overrideReader.run(container);

            expect(readOverridesSpy).toHaveBeenCalled();
            expect(container.registerInstance).toHaveBeenCalledWith("OverridedSettings", settings);
        });
    });

    describe("readOverrides", () => 
    {
        it("should return empty settings if no mods with overrides are found", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(false);

            const result = (overrideReader as any).readOverrides();

            expect(result).toEqual(new OverridedSettings());
            expect(mockVfs.getDirectories).toHaveBeenCalledWith(modDir);
            expect(mockVfs.exists).toHaveBeenCalledWith(expect.stringContaining(path.join("mod1", "MissingQuestWeapons")));
            expect(mockVfs.exists).toHaveBeenCalledWith(expect.stringContaining(path.join("mod2", "MissingQuestWeapons")));
        });

        it("should process quest overrides from a single mod", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const questOverrides: IQuestOverrides = {
                BlackListedQuests: ["quest_blacklist_1"],
                Overrides: [
                    { id: "quest1", whiteListedWeapons: ["weapon1"] }
                ]
            };

            const tryReadJsonSpy = jest.spyOn(jsonHelper, "tryReadJson")
                .mockReturnValueOnce(questOverrides)
                .mockReturnValueOnce(null);

            const result = (overrideReader as any).readOverrides() as OverridedSettings;


            expect(result.getOverrideForQuest("quest1")).toBeDefined();
            expect(result.getOverrideForQuest("quest1").whiteListedWeapons).toContain("weapon1");
            expect(result.getOverrideForQuest("quest_blacklist_1")).toBeDefined();
            expect(result.getOverrideForQuest("quest_blacklist_1").blackListed).toBe(true);
            expect(tryReadJsonSpy).toHaveBeenCalledWith(expect.stringContaining(path.join("mod1", "MissingQuestWeapons")), "QuestOverrides", mockLogger as any);
        });

        it("should process weapon overrides from a single mod", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const weaponOverrides: IOverriddenWeapons = {
                Override: { "weapon1": "new_type" },
                CanBeUsedAs: { "weapon2": ["weapon3"] },
                CanBeUsedAsShortNameWhitelist: ["whitelisted_name"],
                CanBeUsedAsShortNameBlacklist: ["blacklisted_name"],
                CustomCategories: [{ name: "custom_cat", ids: ["weapon4"] }]
            };

            const tryReadJsonSpy = jest.spyOn(jsonHelper, "tryReadJson")
                .mockReturnValueOnce(null)
                .mockReturnValueOnce(weaponOverrides);

            const result = (overrideReader as any).readOverrides() as OverridedSettings;

            expect(result.overriddenWeapons["weapon1"]).toBe("new_type");
            expect(result.canBeUsedAs["weapon2"]).toContain("weapon3");
            expect(result.canBeUsedAsShortNameWhitelist).toContain("whitelisted_name");
            expect(result.canBeUsedAsShortNameBlackList).toContain("blacklisted_name");
            expect(result.customCategories["custom_cat"]).toBeDefined();
            expect(result.customCategories["custom_cat"].ids).toContain("weapon4");
            expect(tryReadJsonSpy).toHaveBeenCalledWith(expect.stringContaining(path.join("mod1", "MissingQuestWeapons")), "OverriddenWeapons", mockLogger as any);
        });

        it("should merge overrides from multiple mods with MERGE behaviour", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const questOverrides1: IQuestOverrides = {
                Overrides: [{ id: "quest1", whiteListedWeapons: ["weapon1"] }],
                BlackListedQuests: []
            };
            const weaponOverrides1: IOverriddenWeapons = {
                Override: { "weapon1": "type1" },
                CanBeUsedAs: {},
                CustomCategories: [],
                CanBeUsedAsShortNameBlacklist: [],
                CanBeUsedAsShortNameWhitelist: []
            };

            const questOverrides2: IQuestOverrides = {
                OverrideBehaviour: OverrideBehaviour.MERGE,
                Overrides: [{ id: "quest1", whiteListedWeapons: ["weapon2"], blackListedWeapons: ["weapon3"] }],
                BlackListedQuests: []
            };
            const weaponOverrides2: IOverriddenWeapons = {
                OverrideBehaviour: OverrideBehaviour.MERGE,
                Override: { "weapon4": "type2" },
                CanBeUsedAs: {},
                CustomCategories: [],
                CanBeUsedAsShortNameBlacklist: [],
                CanBeUsedAsShortNameWhitelist: []
            };

            jest.spyOn(jsonHelper, "tryReadJson")
                .mockImplementation((filePath, fileName) => 
                {
                    if (filePath.includes("mod1")) 
                    {
                        if (fileName === "QuestOverrides") return questOverrides1;
                        if (fileName === "OverriddenWeapons") return weaponOverrides1;
                    }
                    if (filePath.includes("mod2")) 
                    {
                        if (fileName === "QuestOverrides") return questOverrides2;
                        if (fileName === "OverriddenWeapons") return weaponOverrides2;
                    }
                    return null;
                });

            const result = (overrideReader as any).readOverrides() as OverridedSettings;

            // Quest overrides merging
            expect(result.getOverrideForQuest("quest1")).toBeDefined();
            expect(result.getOverrideForQuest("quest1").whiteListedWeapons).toEqual(["weapon1", "weapon2"]);
            expect(result.getOverrideForQuest("quest1").blackListedWeapons).toEqual(["weapon3"]);

            // Weapon overrides merging
            expect(result.overriddenWeapons["weapon1"]).toBe("type1"); // From mod1, default behaviour is IGNORE
            expect(result.overriddenWeapons["weapon4"]).toBe("type2"); // From mod2
        });

        describe("Condition-based Overrides", () =>
        {
            it("should handle overrides per condition",()=> 
            {
                (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1"]);
                (mockVfs.exists as jest.Mock).mockReturnValue(true);
                const questOverrides1: IQuestOverrides = {
                    BlackListedQuests: [],
                    Overrides: [
                        { id: "quest1", whiteListedWeapons: ["weapon1"], conditions: ["cond1", "cond3"] },
                        { id: "quest1", whiteListedWeapons: ["weapon2", "weapon6"], conditions: ["cond2"] },
                        { id: "quest1", whiteListedWeapons: ["weapon3"], blackListedWeapons: ["weapon4"] } // generic
                    ]
                };

                jest.spyOn(jsonHelper, "tryReadJson")
                    .mockReturnValueOnce(questOverrides1)
                    .mockReturnValueOnce({})

                const result = (overrideReader as any).readOverrides() as OverridedSettings;

                const questResult1 = result.getOverrideForQuest("quest1", "cond1");
                expect(questResult1).toBeDefined();
                expect(questResult1.whiteListedWeapons).toEqual(["weapon1"]);

                const questResult2 = result.getOverrideForQuest("quest1", "cond2");
                expect(questResult2).toBeDefined();
                expect(questResult2.whiteListedWeapons).toEqual(["weapon2", "weapon6"]);

                const questResult3 = result.getOverrideForQuest("quest1", "cond3");
                expect(questResult3).toBeDefined();
                expect(questResult3.whiteListedWeapons).toEqual(["weapon1"]);

                const questResult4 = result.getOverrideForQuest("quest1", "cond4");
                expect(questResult4).toBeDefined();
                expect(questResult4.whiteListedWeapons).toEqual(["weapon3"]);
                expect(questResult4.blackListedWeapons).toEqual(["weapon4"]);

                const questResultGeneric = result.getOverrideForQuest("quest1");
                expect(questResultGeneric).toBeDefined();
                expect(questResultGeneric.whiteListedWeapons).toEqual(["weapon3"]);
                expect(questResultGeneric.blackListedWeapons).toEqual(["weapon4"]);
            });
            it("should handle overhauls per condition",()=>
            {
                (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2"]);
                (mockVfs.exists as jest.Mock).mockReturnValue(true);

                const questOverrides1: IQuestOverrides = {
                    BlackListedQuests: [],
                    Overrides: [
                        { id: "quest1", whiteListedWeapons: ["weapon1"], conditions: ["cond1", "cond3"] },
                        { id: "quest1", whiteListedWeapons: ["weapon2", "weapon6"], conditions: ["cond2"] },
                        { id: "quest1", whiteListedWeapons: ["weapon1", "weapon2"], conditions: ["cond4"] },
                        { id: "quest1", whiteListedWeapons: ["weapon1", "weapon2"], conditions: ["cond_del"] },
                        { id: "quest1", whiteListedWeapons: ["weapon3"], blackListedWeapons: ["weapon4"] } // generic
                    ]
                };
                const questOverrides2: IQuestOverrides = {
                    BlackListedQuests: [],
                    Overrides: [
                        { id: "quest1", whiteListedWeapons: ["weapon5"], conditions: ["cond1"], OverrideBehaviour: OverrideBehaviour.MERGE },
                        { id: "quest1", whiteListedWeapons: ["weapon2"], conditions: ["cond2"], OverrideBehaviour: OverrideBehaviour.DELETE },
                        { id: "quest1", whiteListedWeapons: ["weapon2"], conditions: ["cond_del"], OverrideBehaviour: OverrideBehaviour.DELETE },
                        { id: "quest1", whiteListedWeapons: ["weapon7"], conditions: ["cond2"], OverrideBehaviour: OverrideBehaviour.MERGE },
                        { id: "quest1", whiteListedWeapons: [{value: "weapon1", behaviour: OverrideBehaviour.DELETE}, "weapon3"], conditions: ["cond4"], OverrideBehaviour: OverrideBehaviour.MERGE },
                        { id: "quest1", whiteListedWeapons: ["weapon3"] } // generic
                    ]
                };
                jest.spyOn(jsonHelper, "tryReadJson")
                    .mockReturnValueOnce(questOverrides1)
                    .mockReturnValueOnce({})
                    .mockReturnValueOnce(questOverrides2)
                    .mockReturnValueOnce({})

                const result = (overrideReader as any).readOverrides() as OverridedSettings;

                const questResult1 = result.getOverrideForQuest("quest1", "cond1");
                expect(questResult1).toBeDefined();
                expect(questResult1.whiteListedWeapons).toEqual(["weapon1", "weapon5"]);

                const questResult2 = result.getOverrideForQuest("quest1", "cond2");
                expect(questResult2).toBeDefined();
                expect(questResult2.whiteListedWeapons).toEqual(["weapon7"]);

                const questResult3 = result.getOverrideForQuest("quest1", "cond3");
                expect(questResult3).toBeDefined();
                expect(questResult3.whiteListedWeapons).toEqual(["weapon1"]);

                const questResult4 = result.getOverrideForQuest("quest1", "cond4");
                expect(questResult4).toBeDefined();
                expect(questResult4.whiteListedWeapons).toEqual(["weapon2", "weapon3"]);

                // Generic override (no condition)

                const questResultGeneric = result.getOverrideForQuest("quest1");
                expect(questResultGeneric).toBeDefined();
                expect(questResultGeneric.whiteListedWeapons).toEqual(["weapon3"]);

                const questResultGeneric2 = result.getOverrideForQuest("quest1","not_existing_condition");
                expect(questResultGeneric2).toBeDefined();
                expect(questResultGeneric2.whiteListedWeapons).toEqual(["weapon3"]);

                const questResultDel = result.getOverrideForQuest("quest1", "cond_del");
                expect(questResultDel).toBeDefined();
                expect(questResultDel).toEqual(result.getOverrideForQuest("quest1")); // generic
            })
        })
        it("should handle IGNORE override behaviour for quests", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const questOverrides1: IQuestOverrides = {
                Overrides: [{ id: "quest1", whiteListedWeapons: ["weapon1"] }],
                BlackListedQuests: []
            };
            const questOverrides2: IQuestOverrides = {
                // Default behaviour is IGNORE
                Overrides: [{ id: "quest1", whiteListedWeapons: ["weapon2"] }],
                BlackListedQuests: []
            };

            jest.spyOn(jsonHelper, "tryReadJson")
                .mockImplementation((filePath, fileName) => 
                {
                    if (fileName !== "QuestOverrides") return null;
                    if (filePath.includes("mod1")) return questOverrides1;
                    if (filePath.includes("mod2")) return questOverrides2;
                    return null;
                });

            const result = (overrideReader as any).readOverrides() as OverridedSettings;

            expect(result.getOverrideForQuest("quest1")).toBeDefined();
            expect(result.getOverrideForQuest("quest1").whiteListedWeapons).toEqual(["weapon1"]);
            expect(result.getOverrideForQuest("quest1").whiteListedWeapons).not.toContain("weapon2");
        });

        it("should handle REPLACE override behaviour for quests", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const questOverrides1: IQuestOverrides = {
                Overrides: [{ id: "quest1", whiteListedWeapons: ["weapon1"], skip: false }],
                BlackListedQuests: []
            };
            const questOverrides2: IQuestOverrides = {
                Overrides: [{ id: "quest1", whiteListedWeapons: ["weapon2"], skip: true, OverrideBehaviour: OverrideBehaviour.REPLACE }],
                BlackListedQuests: []
            };

            jest.spyOn(jsonHelper, "tryReadJson")
                .mockImplementation((filePath, fileName) => 
                {
                    if (fileName !== "QuestOverrides") return null;
                    if (filePath.includes("mod1")) return questOverrides1;
                    if (filePath.includes("mod2")) return questOverrides2;
                    return null;
                });

            const result = (overrideReader as any).readOverrides() as OverridedSettings;

            expect(result.getOverrideForQuest("quest1")).toBeDefined();
            expect(result.getOverrideForQuest("quest1").whiteListedWeapons).toEqual(["weapon2"]);
            expect(result.getOverrideForQuest("quest1").whiteListedWeapons).not.toContain("weapon1");
            expect(result.getOverrideForQuest("quest1").skip).toBe(true);
        });

        it("should handle DELETE override behaviour for quests", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const questOverrides1: IQuestOverrides = {
                Overrides: [{ id: "quest1", whiteListedWeapons: ["weapon1"] }],
                BlackListedQuests: []
            };
            const questOverrides2: IQuestOverrides = {
                Overrides: [{ id: "quest1", OverrideBehaviour: OverrideBehaviour.DELETE }],
                BlackListedQuests: []
            };

            jest.spyOn(jsonHelper, "tryReadJson")
                .mockImplementation((filePath, fileName) => 
                {
                    if (fileName !== "QuestOverrides") return null;
                    if (filePath.includes("mod1")) return questOverrides1;
                    if (filePath.includes("mod2")) return questOverrides2;
                    return null;
                });

            const result = (overrideReader as any).readOverrides();

            expect(result.questOverrides["quest1"]).toStrictEqual([]);
        });

        it("should handle DELETE override behaviour for custom categories", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const weaponOverrides1: IOverriddenWeapons = {
                CustomCategories: [{ name: "custom_cat", ids: ["weapon1"] }],
                Override: {}, CanBeUsedAs: {}, CanBeUsedAsShortNameBlacklist: [], CanBeUsedAsShortNameWhitelist: []
            };
            const weaponOverrides2: IOverriddenWeapons = {
                CustomCategories: [{ value: { name: "custom_cat" }, behaviour: OverrideBehaviour.DELETE }],
                Override: {}, CanBeUsedAs: {}, CanBeUsedAsShortNameBlacklist: [], CanBeUsedAsShortNameWhitelist: []
            };

            jest.spyOn(jsonHelper, "tryReadJson")
                .mockImplementation((filePath, fileName) => 
                {
                    if (fileName !== "OverriddenWeapons") return null;
                    if (filePath.includes("mod1")) return weaponOverrides1;
                    if (filePath.includes("mod2")) return weaponOverrides2;
                    return null;
                });

            const result = (overrideReader as any).readOverrides() as OverridedSettings;

            expect(result.customCategories["custom_cat"]).toBeUndefined();
        });

        it("should handle REPLACE override behaviour for custom categories", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const weaponOverrides1: IOverriddenWeapons = {
                CustomCategories: [{ name: "custom_cat", ids: ["weapon1"], whiteListedKeywords: ["keyword1"] }],
                Override: {}, CanBeUsedAs: {}, CanBeUsedAsShortNameBlacklist: [], CanBeUsedAsShortNameWhitelist: []
            };
            const weaponOverrides2: IOverriddenWeapons = {
                OverrideBehaviour: OverrideBehaviour.REPLACE,
                CustomCategories: [{ name: "custom_cat", ids: ["weapon2"], blackListedKeywords: ["keyword2"] }],
                Override: {}, CanBeUsedAs: {}, CanBeUsedAsShortNameBlacklist: [], CanBeUsedAsShortNameWhitelist: []
            };

            jest.spyOn(jsonHelper, "tryReadJson")
                .mockImplementation((filePath, fileName) => 
                {
                    if (fileName !== "OverriddenWeapons") return null;
                    if (filePath.includes("mod1")) return weaponOverrides1;
                    if (filePath.includes("mod2")) return weaponOverrides2;
                    return null;
                });

            const result = (overrideReader as any).readOverrides() as OverridedSettings;

            expect(result.customCategories["custom_cat"]).toBeDefined();
            expect(result.customCategories["custom_cat"].ids).toEqual(["weapon2"]);
            expect(result.customCategories["custom_cat"].whiteListedKeywords).toEqual([]);
            expect(result.customCategories["custom_cat"].blackListedKeywords).toEqual(["keyword2"]);
        });

        it("should handle different override behaviours for CanBeUsedAs", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2", "mod3"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);

            const weaponOverrides1: IOverriddenWeapons = {
                CanBeUsedAs: { "weapon1": ["weapon2"] },
                Override: {}, CustomCategories: [], CanBeUsedAsShortNameBlacklist: [], CanBeUsedAsShortNameWhitelist: []
            };
            const weaponOverrides2: IOverriddenWeapons = {
                OverrideBehaviour: OverrideBehaviour.MERGE,
                CanBeUsedAs: { "weapon1": ["weapon3"] },
                Override: {}, CustomCategories: [], CanBeUsedAsShortNameBlacklist: [], CanBeUsedAsShortNameWhitelist: []
            };
            const weaponOverrides3: IOverriddenWeapons = {
                OverrideBehaviour: OverrideBehaviour.MERGE,
                CanBeUsedAs: { "weapon1": [{ value: "weapon2", behaviour: OverrideBehaviour.DELETE }] },
                Override: {}, CustomCategories: [], CanBeUsedAsShortNameBlacklist: [], CanBeUsedAsShortNameWhitelist: []
            };

            jest.spyOn(jsonHelper, "tryReadJson")
                .mockImplementation((filePath, fileName) => 
                {
                    if (fileName !== "OverriddenWeapons") return null;
                    if (filePath.includes("mod1")) return weaponOverrides1;
                    if (filePath.includes("mod2")) return weaponOverrides2;
                    if (filePath.includes("mod3")) return weaponOverrides3;
                    return null;
                });

            const result = (overrideReader as any).readOverrides();

            expect(result.canBeUsedAs["weapon1"]).toBeDefined();
            expect(result.canBeUsedAs["weapon1"]).toEqual(["weapon3"]);
        });

        it("should handle different override behaviours for Override", () => 
        {
            (mockVfs.getDirectories as jest.Mock).mockReturnValue(["mod1", "mod2", "mod3"]);
            (mockVfs.exists as jest.Mock).mockReturnValue(true);
            const weaponOverrides1: IOverriddenWeapons = {
                Override: {"weapon1": "type1", "weapon2": "type2", "weapon3": "type3,type4", "weapon6": "type6"},
                CanBeUsedAs: {},
                CustomCategories: [],
                CanBeUsedAsShortNameBlacklist: [],
                CanBeUsedAsShortNameWhitelist: []
            }
            const weaponOverrides2: IOverriddenWeapons = {
                Override: {"weapon1": "type2", "weapon2": {"behaviour": OverrideBehaviour.REPLACE, "value": "type5"}, "weapon4": "type4"},
                CanBeUsedAs: {},
                CustomCategories: [],
                CanBeUsedAsShortNameBlacklist: [],
                CanBeUsedAsShortNameWhitelist: []
            }
            const weaponOverrides3: IOverriddenWeapons = {
                Override: {"weapon1": "type3", "weapon3": {"behaviour": OverrideBehaviour.REPLACE, "value": "type6"}, "weapon6": {"behaviour": OverrideBehaviour.DELETE, "value": ""}},
                CanBeUsedAs: {},
                CustomCategories: [],
                CanBeUsedAsShortNameBlacklist: [],
                CanBeUsedAsShortNameWhitelist: []
            }
            jest.spyOn(jsonHelper, "tryReadJson")
                .mockImplementation((filePath, fileName) => 
                {
                    if (fileName !== "OverriddenWeapons") return null;
                    if (filePath.includes("mod1")) return weaponOverrides1;
                    if (filePath.includes("mod2")) return weaponOverrides2;
                    if (filePath.includes("mod3")) return weaponOverrides3;
                    return null;
                });

            const result = (overrideReader as any).readOverrides();

            expect(result.overriddenWeapons["weapon1"]).toBe("type1");
            expect(result.overriddenWeapons["weapon2"]).toBe("type5");
            expect(result.overriddenWeapons["weapon3"]).toBe("type6");
            expect(result.overriddenWeapons["weapon4"]).toBe("type4");
            expect(result.overriddenWeapons["weapon6"]).toBeUndefined();
        })
    });
});