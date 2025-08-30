import "reflect-metadata";
import { OverridedSettings } from "../../../src/models/OverridedSettings";
import { IQuestOverride } from "../../../src/models/IQuestOverride";
import { describe, it, expect, beforeEach } from "@jest/globals";

describe("OverridedSettings", () => 
{
    let settings: OverridedSettings;

    beforeEach(() => 
    {
        settings = new OverridedSettings();
    });

    describe("getOverrideForQuest", () => 
    {
        const questId = "test_quest";
        const specificCondId = "cond_1";
        const anotherCondId = "cond_2";

        const genericOverride: IQuestOverride = { id: questId, skip: true };
        const specificOverride: IQuestOverride = { id: questId, condition: specificCondId, onlyUseWhiteListedWeapons: true };
        const genericBlacklist: IQuestOverride = { id: questId, blackListed: true };
        const specificBlacklist: IQuestOverride = { id: questId, condition: anotherCondId, blackListed: true };
        const specificWhitelist: IQuestOverride = { id: questId, condition: specificCondId, blackListed: false, skip: true };


        it("should return null if no overrides exist for the quest", () => 
        {
            expect(settings.getOverrideForQuest(questId, specificCondId)).toBeNull();
        });

        it("should return a specific override that matches the conditionId", () => 
        {
            settings.questOverrides[questId] = [genericOverride, specificOverride];
            expect(settings.getOverrideForQuest(questId, specificCondId)).toEqual(specificOverride);
        });

        it("should return a generic override if no specific override matches the conditionId", () => 
        {
            settings.questOverrides[questId] = [genericOverride, specificOverride];
            expect(settings.getOverrideForQuest(questId, anotherCondId)).toEqual(genericOverride);
        });
        
        it("should return a generic override if no conditionId is provided", () => 
        {
            settings.questOverrides[questId] = [genericOverride, specificOverride];
            expect(settings.getOverrideForQuest(questId)).toEqual(genericOverride);
        });

        it("should return null if no generic override exists and no specific override matches", () => 
        {
            settings.questOverrides[questId] = [specificOverride];
            expect(settings.getOverrideForQuest(questId, anotherCondId)).toBeNull();
        });

        it("should prioritize specific override over generic blacklist", () => 
        {
            settings.questOverrides[questId] = [genericBlacklist, specificWhitelist];
            // For cond_1, the specific override should be returned, which is not blacklisted
            expect(settings.getOverrideForQuest(questId, specificCondId)).toEqual(specificWhitelist);
            // For cond_2, no specific override exists, so the generic blacklist should be returned
            expect(settings.getOverrideForQuest(questId, anotherCondId)).toEqual(genericBlacklist);
        });

        it("should return a specific blacklisted override", () => 
        {
            settings.questOverrides[questId] = [genericOverride, specificBlacklist];
            expect(settings.getOverrideForQuest(questId, anotherCondId)).toEqual(specificBlacklist);
        });
    });
});