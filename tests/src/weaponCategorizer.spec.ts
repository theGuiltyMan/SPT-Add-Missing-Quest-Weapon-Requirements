import "reflect-metadata";
import { WeaponCategorizer } from "../../src/weaponCategorizer";
import { LogHelper } from "../../src/util/logHelper";
import { DatabaseServer } from "@spt/servers/DatabaseServer";
import { IAddMissingQuestRequirementConfig } from "../../src/models/IAddMissingQuestRequirementConfig";
import { OverridedSettings } from "../../src/models/OverridedSettings";
import { LocaleHelper } from "../../src/util/localeHelper";
import { DependencyContainer } from "tsyringe";
import { ITemplateItem, ItemType as _ItemType } from "@spt/models/eft/common/tables/ITemplateItem";
import { describe, it, expect, beforeEach, jest } from "@jest/globals";




// Mock data
const mockItems: Record<string, ITemplateItem> = {
    "pistol_pm": { _id: "pistol_pm", _name: "pistol_pm", _parent: "5447b5694bdc2d6f028b4567", _props: {}, _type: "Item" as _ItemType  },
    "revolver_rhino": { _id: "revolver_rhino", _name: "revolver_rhino", _parent: "5447b5694bdc2d6f028b4567", _props: {}, _type: "Item" as _ItemType },
    "shotgun_saiga": { _id: "shotgun_saiga", _name: "shotgun_saiga", _parent: "5447b6094bdc2d87028b4567", _props: {}, _type: "Item" as _ItemType },
    "pump_shotgun_mp133": { _id: "pump_shotgun_mp133", _name: "pump_shotgun_mp133", _parent: "5447b6094bdc2d87028b4567", _props: {}, _type: "Item" as _ItemType },
    "sniper_m700": { _id: "sniper_m700", _name: "sniper_m700", _parent: "5447b5f14bdc2d61278b4567", _props: { BoltAction: true }, _type: "Item" as _ItemType },
    "overridden_weapon": { _id: "overridden_weapon", _name: "overridden_weapon", _parent: "5447b5694bdc2d6f028b4567", _props: {}, _type: "Item" as _ItemType },
    "custom_ak": { _id: "custom_ak", _name: "custom_ak", _parent: "5447b54c4bdc2d16278b4567", _props: { ammoCaliber: "Caliber762x39" }, _type: "Item" as _ItemType },
    "m4a1": { _id: "m4a1", _name: "m4a1", _parent: "5447b54c4bdc2d16278b4567", _props: {}, _type: "Item" as _ItemType },
    "m4a1_fde": { _id: "m4a1_fde", _name: "m4a1_fde", _parent: "5447b54c4bdc2d16278b4567", _props: {}, _type: "Item" as _ItemType },
    "blacklisted_item": { _id: "blacklisted_item", _name: "blacklisted_item", _parent: "5447b5694bdc2d6f028b4567", _props: {}, _type: "Item" as _ItemType },

    // Hierarchy
    "5447b5694bdc2d6f028b4567": { _id: "5447b5694bdc2d6f028b4567", _name: "Pistol", _parent: "5447b5004bdc2d65028b4567", _props: {}, _type: "Item" as _ItemType },
    "5447b6094bdc2d87028b4567": { _id: "5447b6094bdc2d87028b4567", _name: "Shotgun", _parent: "5447b5004bdc2d65028b4567", _props: {}, _type: "Item" as _ItemType },
    "5447b5f14bdc2d61278b4567": { _id: "5447b5f14bdc2d61278b4567", _name: "SniperRifle", _parent: "5447b5004bdc2d65028b4567", _props: {}, _type: "Item" as _ItemType },
    "5447b54c4bdc2d16278b4567": { _id: "5447b54c4bdc2d16278b4567", _name: "AssaultRifle", _parent: "5447b5004bdc2d65028b4567", _props: {}, _type: "Item" as _ItemType },
    "5447b5004bdc2d65028b4567": { _id: "5447b5004bdc2d65028b4567", _name: "Weapon", _parent: "57864a66245977548f04a81f", _props: {}, _type: "Item" as _ItemType },
    "57864a66245977548f04a81f": { _id: "57864a66245977548f04a81f", _name: "Item", _parent: "", _props: {}, _type: "Item" as _ItemType }
};

const mockLocales = {
    "pump_shotgun_mp133 Name": "MP-133 pump-action shotgun",
    "revolver_rhino Name": "Chiappa Rhino revolver",
    "m4a1 ShortName": "M4A1",
    "m4a1_fde ShortName": "M4A1 FDE",
    "custom_ak Name": "Custom AK Rifle"
};

describe("WeaponCategorizer", () => 
{
    let weaponCategorizer: WeaponCategorizer;
    let mockLogger: jest.Mocked<LogHelper>;
    let mockDatabaseServer: jest.Mocked<DatabaseServer>;
    let mockConfig: IAddMissingQuestRequirementConfig;
    let mockOverridedSettings: OverridedSettings;
    let mockLocaleHelper: jest.Mocked<LocaleHelper>;
    let mockDependencyContainer: jest.Mocked<DependencyContainer>;

    beforeEach(() => 
    {
        jest.clearAllMocks();

        mockLogger = {
            log: jest.fn(),
            error: jest.fn().mockImplementation((message) => 
            {
                console.log("Logger error:", message);
            }),
            logDebug: jest.fn(),
            plusIndent: jest.fn(),
            minusIndent: jest.fn()
        } as unknown as jest.Mocked<LogHelper>;

        mockDatabaseServer = {
            getTables: jest.fn().mockReturnValue({
                templates: { items: mockItems },
                locales: { global: { "en": mockLocales } }
            })
        } as unknown as jest.Mocked<DatabaseServer>;

        mockConfig = {
            BlackListedWeaponsTypes: [],
            BlackListedItems: ["blacklisted_item"],
            categorizeWithLessRestrive: true
        } as IAddMissingQuestRequirementConfig;

        mockOverridedSettings = new OverridedSettings();

        mockLocaleHelper = {
            getShortName: jest.fn((id) => mockLocales[`${id} ShortName`] || ""),
            getName: jest.fn((id) => mockLocales[`${id} Name`] || ""),
            getDescription: jest.fn(() => "")
        } as unknown as jest.Mocked<LocaleHelper>;

        mockDependencyContainer = {
            registerInstance: jest.fn()
        } as unknown as jest.Mocked<DependencyContainer>;

        weaponCategorizer = new WeaponCategorizer(
            mockLogger,
            mockDatabaseServer,
            mockConfig,
            mockOverridedSettings,
            mockLocaleHelper
        );
    });

    const getWeaponTypes = () => (mockDependencyContainer.registerInstance.mock.calls.find(call => call[0] === "WeaponTypes")?.[1]) as Record<string, string[]>;
    const getWeaponToType = () => (mockDependencyContainer.registerInstance.mock.calls.find(call => call[0] === "WeaponToType")?.[1]) as Record<string, string[]>;

    it("should categorize a bolt-action sniper and also as a regular sniper rifle", () => 
    {
        weaponCategorizer.run(mockDependencyContainer);
        const weaponTypes = getWeaponTypes();
        expect(weaponTypes["SniperRifle"]).toContain("sniper_m700");
        expect(weaponTypes["BoltActionSniperRifle"]).toContain("sniper_m700");
    });

    it("should use overrides from OverridedSettings", () => 
    {
        mockOverridedSettings.overriddenWeapons["overridden_weapon"] = "Smg,AssaultCarbine";
        weaponCategorizer.run(mockDependencyContainer);
        const weaponTypes = getWeaponTypes();
        expect(weaponTypes["Smg"]).toContain("overridden_weapon");
        expect(weaponTypes["AssaultCarbine"]).toContain("overridden_weapon");
        expect(weaponTypes["Pistol"]).not.toContain("overridden_weapon");
    });

    it("should handle custom categories based on keywords and caliber", () => 
    {
        mockOverridedSettings.customCategories["AKM"] = {
            name: "AKM",
            whiteListedKeywords: ["\\b(AK)\\w*"],
            allowedCalibres: ["Caliber762x39"]
        };
        weaponCategorizer.run(mockDependencyContainer);
        const weaponTypes = getWeaponTypes();
        expect(weaponTypes["AKM"]).toContain("custom_ak");
    });

    it("should group weapons with similar short names under 'canBeUsedAs'", () => 
    {
        mockOverridedSettings.canBeUsedAsShortNameWhitelist = ["FDE"];
        weaponCategorizer.run(mockDependencyContainer);
        expect(mockOverridedSettings.canBeUsedAs["m4a1"]).toContain("m4a1_fde");
        expect(mockOverridedSettings.canBeUsedAs["m4a1_fde"]).toContain("m4a1");
    });

    it("should not categorize blacklisted items", () => 
    {
        weaponCategorizer.run(mockDependencyContainer);
        const weaponToType = getWeaponToType();
        expect(weaponToType["blacklisted_item"]).toBeUndefined();
    });

    it("should register WeaponTypes and WeaponToType in the container", () => 
    {
        weaponCategorizer.run(mockDependencyContainer);
        expect(mockDependencyContainer.registerInstance).toHaveBeenCalledWith("WeaponTypes", expect.any(Object));
        expect(mockDependencyContainer.registerInstance).toHaveBeenCalledWith("WeaponToType", expect.any(Object));
    });
});
