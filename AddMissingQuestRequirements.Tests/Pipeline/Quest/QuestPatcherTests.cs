using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Quest;

/// <summary>
/// Tests for WeaponArrayExpander (via QuestPatcher) covering all twelve plan scenarios.
/// Each test builds a minimal CategorizationResult and OverriddenSettings, patches one quest,
/// then asserts on the resulting weapon list and/or log output.
/// </summary>
public class QuestPatcherTests
{
    // ── Shared fixture ────────────────────────────────────────────────────────

    /// <summary>
    /// Weapon catalogue used by most tests:
    ///   AssaultRifle  → ak74, ak47
    ///   Revolver      → rhino, python   (kindOf: Revolver → Pistol)
    ///   Pistol        → pm, rhino, python
    ///   Sniper        → sv98
    /// Calibers: ak74/ak47 = "556", sv98 = "762", rhino/python/pm = "357"
    /// </summary>
    private static CategorizationResult MakeCat(
        Dictionary<string, IReadOnlySet<string>>? extraWeaponTypes = null,
        Dictionary<string, IReadOnlySet<string>>? extraWeaponToType = null,
        Dictionary<string, IReadOnlySet<string>>? canBeUsedAs = null,
        Dictionary<string, string>? weaponToCaliber = null)
    {
        var weaponTypes = new Dictionary<string, IReadOnlySet<string>>
        {
            ["AssaultRifle"] = new HashSet<string> { "ak74", "ak47" },
            ["Revolver"]     = new HashSet<string> { "rhino", "python" },
            ["Pistol"]       = new HashSet<string> { "pm", "rhino", "python" },
            ["Sniper"]       = new HashSet<string> { "sv98" },
        };
        var weaponToType = new Dictionary<string, IReadOnlySet<string>>
        {
            ["ak74"]   = new HashSet<string> { "AssaultRifle" },
            ["ak47"]   = new HashSet<string> { "AssaultRifle" },
            ["rhino"]  = new HashSet<string> { "Revolver", "Pistol" },
            ["python"] = new HashSet<string> { "Revolver", "Pistol" },
            ["pm"]     = new HashSet<string> { "Pistol" },
            ["sv98"]   = new HashSet<string> { "Sniper" },
        };
        var calibers = new Dictionary<string, string>
        {
            ["ak74"]   = "556",
            ["ak47"]   = "556",
            ["rhino"]  = "357",
            ["python"] = "357",
            ["pm"]     = "357",
            ["sv98"]   = "762",
        };

        if (extraWeaponTypes is not null)
        {
            foreach (var (k, v) in extraWeaponTypes)
            {
                weaponTypes[k] = v;
            }
        }

        if (extraWeaponToType is not null)
        {
            foreach (var (k, v) in extraWeaponToType)
            {
                weaponToType[k] = v;
            }
        }

        if (weaponToCaliber is not null)
        {
            foreach (var (k, v) in weaponToCaliber)
            {
                calibers[k] = v;
            }
        }

        return new CategorizationResult
        {
            WeaponTypes     = weaponTypes,
            WeaponToType    = weaponToType,
            CanBeUsedAs     = canBeUsedAs ?? new Dictionary<string, IReadOnlySet<string>>(),
            WeaponToCaliber = calibers,
            KnownItemIds    = weaponToType.Keys.ToHashSet(),
        };
    }

    private static QuestPatcher MakePatcher()
    {
        return new QuestPatcher(
            [new WeaponArrayExpander(new TypeSelector(), NullNameResolver.Instance)],
            NullNameResolver.Instance);
    }

    private static ConditionNode MakeCondition(string id, params string[] weapons)
    {
        return new ConditionNode { Id = id, ConditionType = "CounterCreator", Weapon = [..weapons] };
    }

    private static QuestNode MakeQuest(string id, params ConditionNode[] conditions)
    {
        return new QuestNode { Id = id, Conditions = [..conditions] };
    }

    private static IQuestDatabase MakeDb(params QuestNode[] quests)
    {
        return new InMemoryQuestDatabase(quests);
    }

    private static OverriddenSettings EmptySettings(ModConfig? config = null) =>
        new() { Config = config ?? new ModConfig() };

    // ── Single-weapon: no type expansion ────────────────────────────────────

    [Fact]
    public void Single_weapon_condition_is_not_type_expanded()
    {
        var condition = MakeCondition("c1", "ak74");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), MakeCat(), NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(["ak74"]);
    }

    [Fact]
    public void Single_weapon_canBeUsedAs_aliases_are_still_added()
    {
        var cat = MakeCat(canBeUsedAs: new()
        {
            ["ak74"] = new HashSet<string> { "ak74_mod" },
        },
        extraWeaponToType: new()
        {
            ["ak74_mod"] = new HashSet<string> { "AssaultRifle" },
        });
        var condition = MakeCondition("c1", "ak74");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), cat, NullModLogger.Instance);

        condition.Weapon.Should().Contain("ak74_mod");
    }

    // ── Two same-type weapons expand to full type set ────────────────────────

    [Fact]
    public void Two_same_type_weapons_expand_to_full_type_set()
    {
        var condition = MakeCondition("c1", "ak74", "ak47");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), MakeCat(), NullModLogger.Instance);

        // AssaultRifle contains ak74 and ak47 — no others in fixture so stays same
        condition.Weapon.Should().BeEquivalentTo(["ak74", "ak47"]);
    }

    [Fact]
    public void Expansion_adds_all_type_members_when_type_has_more_weapons()
    {
        // Add a third assault rifle to the catalogue
        var cat = MakeCat(
            extraWeaponTypes: new() { ["AssaultRifle"] = new HashSet<string> { "ak74", "ak47", "m4" } },
            extraWeaponToType: new() { ["m4"] = new HashSet<string> { "AssaultRifle" } },
            weaponToCaliber: new() { ["m4"] = "556" });

        var condition = MakeCondition("c1", "ak74", "ak47");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), cat, NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(["ak74", "ak47", "m4"]);
    }

    // ── parentTypes preference: Revolver not Pistol ──────────────────────────

    [Fact]
    public void ParentTypes_preference_expands_as_Revolver_not_Pistol()
    {
        // rhino and python are both Revolver and Pistol; parentTypes says Revolver → Pistol.
        // The expander should select Revolver (more specific) and expand to its members only.
        var config    = new ModConfig { ParentTypes = new() { ["Revolver"] = "Pistol" } };
        var condition = MakeCondition("c1", "rhino", "python");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(config), MakeCat(), NullModLogger.Instance);

        // Expanded to Revolver set: rhino, python — NOT pm (which is only Pistol)
        condition.Weapon.Should().BeEquivalentTo(["rhino", "python"]);
        condition.Weapon.Should().NotContain("pm");
    }

    // ── No common type: no expansion ────────────────────────────────────────

    [Fact]
    public void No_common_type_leaves_weapons_unchanged()
    {
        var condition = MakeCondition("c1", "ak74", "sv98");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), MakeCat(), NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(["ak74", "sv98"]);
    }

    // ── Unknown weapon ID ────────────────────────────────────────────────────

    [Fact]
    public void Unknown_weapon_id_is_removed_and_warning_logged()
    {
        // Strict mode: not-in-DB IDs are stripped with a warning.
        var logger    = new CapturingModLogger();
        var condition = MakeCondition("c1", "ak74", "totally_unknown");
        var db        = MakeDb(MakeQuest("q1", condition));
        var config    = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };
        MakePatcher().Patch(db, EmptySettings(config), MakeCat(), logger);

        condition.Weapon.Should().NotContain("totally_unknown");
        logger.Warnings.Should().ContainMatch("*totally_unknown*");
    }

    // ── Override: NoExpansion ────────────────────────────────────────────────

    [Fact]
    public void Override_NoExpansion_suppresses_type_expansion_and_whitelist()
    {
        var settings = new OverriddenSettings
        {
            QuestOverrides = new()
            {
                ["q1"] =
                [
                    new QuestOverrideEntry
                    {
                        Id             = "q1",
                        ExpansionMode  = ExpansionMode.NoExpansion,
                        IncludedWeapons = ["sv98"],   // must NOT be added in NoExpansion mode
                    }
                ]
            }
        };

        // Add a third AR to prove type expansion is also skipped
        var cat = MakeCat(
            extraWeaponTypes: new() { ["AssaultRifle"] = new HashSet<string> { "ak74", "ak47", "m4" } },
            extraWeaponToType: new() { ["m4"] = new HashSet<string> { "AssaultRifle" } },
            weaponToCaliber: new() { ["m4"] = "556" });

        var condition = MakeCondition("c1", "ak74", "ak47");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, settings, cat, NullModLogger.Instance);

        // No type expansion (m4 not added), no whitelist (sv98 not added)
        condition.Weapon.Should().BeEquivalentTo(["ak74", "ak47"]);
        condition.Weapon.Should().NotContain("m4");
        condition.Weapon.Should().NotContain("sv98");
    }

    [Fact]
    public void Override_NoExpansion_still_adds_canBeUsedAs_aliases()
    {
        var settings = new OverriddenSettings
        {
            QuestOverrides = new()
            {
                ["q1"] =
                [
                    new QuestOverrideEntry { Id = "q1", ExpansionMode = ExpansionMode.NoExpansion }
                ]
            }
        };
        var cat = MakeCat(canBeUsedAs: new()
        {
            ["ak74"] = new HashSet<string> { "ak74_mod" },
        },
        extraWeaponToType: new()
        {
            ["ak74_mod"] = new HashSet<string> { "AssaultRifle" },
        });
        var condition = MakeCondition("c1", "ak74");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, settings, cat, NullModLogger.Instance);

        condition.Weapon.Should().Contain("ak74_mod");
    }

    // ── Override: WhitelistOnly ──────────────────────────────────────────────

    [Fact]
    public void Override_WhitelistOnly_suppresses_type_expansion()
    {
        var settings = new OverriddenSettings
        {
            QuestOverrides = new()
            {
                ["q1"] =
                [
                    new QuestOverrideEntry
                    {
                        Id              = "q1",
                        ExpansionMode   = ExpansionMode.WhitelistOnly,
                        IncludedWeapons = ["sv98"],
                    }
                ]
            }
        };
        var condition = MakeCondition("c1", "ak74", "ak47");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, settings, MakeCat(), NullModLogger.Instance);

        // No type expansion; only includedWeapons are used
        condition.Weapon.Should().Contain("sv98");
        condition.Weapon.Should().NotContain("ak74");
        condition.Weapon.Should().NotContain("ak47");
    }

    // ── Processing order: blacklist always last ───────────────────────────────

    [Fact]
    public void Blacklist_removes_weapon_added_by_whitelist()
    {
        var settings = new OverriddenSettings
        {
            QuestOverrides = new()
            {
                ["q1"] =
                [
                    new QuestOverrideEntry
                    {
                        Id              = "q1",
                        IncludedWeapons = ["sv98"],   // added
                        ExcludedWeapons = ["sv98"],   // then removed
                    }
                ]
            }
        };
        var condition = MakeCondition("c1", "ak74", "ak47");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, settings, MakeCat(), NullModLogger.Instance);

        condition.Weapon.Should().NotContain("sv98");
    }

    // ── Category name in whitelist/blacklist ─────────────────────────────────

    [Fact]
    public void Category_name_in_whitelist_expands_to_all_member_ids()
    {
        var settings = new OverriddenSettings
        {
            QuestOverrides = new()
            {
                ["q1"] =
                [
                    new QuestOverrideEntry
                    {
                        Id              = "q1",
                        IncludedWeapons = ["Sniper"],   // type name, not ID
                    }
                ]
            }
        };
        var condition = MakeCondition("c1", "ak74", "ak47");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, settings, MakeCat(), NullModLogger.Instance);

        // "Sniper" expands to its members: sv98
        condition.Weapon.Should().Contain("sv98");
    }

    [Fact]
    public void Category_name_in_blacklist_removes_all_member_ids()
    {
        var settings = new OverriddenSettings
        {
            QuestOverrides = new()
            {
                ["q1"] =
                [
                    new QuestOverrideEntry
                    {
                        Id              = "q1",
                        ExcludedWeapons = ["AssaultRifle"],   // type name
                    }
                ]
            }
        };
        // Condition has one AR and one sniper; blacklist should remove AK74 (via type)
        var condition = MakeCondition("c1", "ak74", "sv98");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, settings, MakeCat(), NullModLogger.Instance);

        condition.Weapon.Should().NotContain("ak74");
        condition.Weapon.Should().Contain("sv98");
    }

    // ── Best-candidate expansion ─────────────────────────────────────────────

    [Fact]
    public void Best_candidate_expansion_off_leaves_weapons_unchanged()
    {
        // ak74, ak47, sv98: AssaultRifle covers all-but-sv98. Config OFF → no expansion.
        var config    = new ModConfig { BestCandidateExpansion = false };
        var condition = MakeCondition("c1", "ak74", "ak47", "sv98");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(config), MakeCat(), NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(["ak74", "ak47", "sv98"]);
    }

    [Fact]
    public void Best_candidate_expansion_on_expands_and_logs_outlier()
    {
        var logger = new CapturingModLogger();
        var config = new ModConfig { BestCandidateExpansion = true };

        // Add m4 to AssaultRifle so expansion actually adds something new
        var cat = MakeCat(
            extraWeaponTypes: new() { ["AssaultRifle"] = new HashSet<string> { "ak74", "ak47", "m4" } },
            extraWeaponToType: new() { ["m4"] = new HashSet<string> { "AssaultRifle" } },
            weaponToCaliber: new() { ["m4"] = "556" });

        var condition = MakeCondition("c1", "ak74", "ak47", "sv98");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(config), cat, logger);

        // Expanded to AssaultRifle (best candidate) — sv98 is the outlier
        condition.Weapon.Should().Contain("m4");
        condition.Weapon.Should().NotContain("sv98");
        logger.Infos.Should().ContainMatch("*sv98*");
    }

    // ── weaponCaliber filter ─────────────────────────────────────────────────

    [Fact]
    public void WeaponCaliber_filter_excludes_weapons_of_wrong_caliber_during_expansion()
    {
        // Expand ak74 + ak47 → AssaultRifle, but add m4 (caliber "223") as a third member.
        // Condition has weaponCaliber = ["556"], so m4 (223) should be excluded.
        var cat = MakeCat(
            extraWeaponTypes: new() { ["AssaultRifle"] = new HashSet<string> { "ak74", "ak47", "m4" } },
            extraWeaponToType: new() { ["m4"] = new HashSet<string> { "AssaultRifle" } },
            weaponToCaliber: new() { ["m4"] = "223" });     // wrong caliber

        var condition = new ConditionNode
        {
            Id            = "c1",
            ConditionType = "CounterCreator",
            Weapon        = ["ak74", "ak47"],
            WeaponCaliber = ["556"],
        };
        var db = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), cat, NullModLogger.Instance);

        condition.Weapon.Should().Contain("ak74");
        condition.Weapon.Should().Contain("ak47");
        condition.Weapon.Should().NotContain("m4");
    }

    // ── Condition with no weapon array: expander is a no-op ─────────────────

    [Fact]
    public void Condition_with_no_weapon_array_is_no_op()
    {
        var condition = new ConditionNode
        {
            Id                  = "c1",
            ConditionType       = "CounterCreator",
            Weapon              = [],
            WeaponModsInclusive = [["mod_a"]],
        };
        var db = MakeDb(MakeQuest("q1", condition));

        var act = () => MakePatcher().Patch(db, EmptySettings(), MakeCat(), NullModLogger.Instance);

        act.Should().NotThrow();
        condition.Weapon.Should().BeEmpty();
    }

    // ── Blacklisted quest skipped ────────────────────────────────────────────

    [Fact]
    public void Blacklisted_quest_is_skipped_entirely()
    {
        var settings = new OverriddenSettings
        {
            ExcludedQuests = ["q1"],
        };

        // Without blacklist this would expand; with it the list stays as-is
        var condition = MakeCondition("c1", "ak74", "ak47");
        var db        = MakeDb(MakeQuest("q1", condition));

        var cat = MakeCat(
            extraWeaponTypes: new() { ["AssaultRifle"] = new HashSet<string> { "ak74", "ak47", "m4" } },
            extraWeaponToType: new() { ["m4"] = new HashSet<string> { "AssaultRifle" } },
            weaponToCaliber: new() { ["m4"] = "556" });

        MakePatcher().Patch(db, settings, cat, NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(["ak74", "ak47"]);
    }

    // ── Non-CounterCreator condition ignored ─────────────────────────────────

    [Fact]
    public void Non_CounterCreator_condition_is_not_processed()
    {
        var condition = new ConditionNode
        {
            Id            = "c1",
            ConditionType = "Elimination",   // not CounterCreator
            Weapon        = ["ak74"],
        };
        var db = MakeDb(MakeQuest("q1", condition));

        // If it were processed we'd try to look up "ak74" in the DB
        var act = () => MakePatcher().Patch(db, EmptySettings(), MakeCat(), NullModLogger.Instance);

        act.Should().NotThrow();
        condition.Weapon.Should().BeEquivalentTo(["ak74"]);
    }

    // ── MP-shotguns partial-list expansion (with and without sub-type rule) ────

    [Fact]
    public void PartialShotgunList_NoCustomRule_ExpandsToAllShotguns()
    {
        // Without a sub-type rule, all four shotguns share only "Shotgun".
        // TypeSelector finds BestType = Shotgun and adds every member,
        // including M870 which was not in the original list.
        // This test documents the expected (if sometimes undesirable) behaviour.
        var cat = new CategorizationResult
        {
            WeaponTypes = new Dictionary<string, IReadOnlySet<string>>
            {
                ["Shotgun"] = new HashSet<string> { "mp133", "mp153", "mp155", "m870" }
            },
            WeaponToType = new Dictionary<string, IReadOnlySet<string>>
            {
                ["mp133"] = new HashSet<string> { "Shotgun" },
                ["mp153"] = new HashSet<string> { "Shotgun" },
                ["mp155"] = new HashSet<string> { "Shotgun" },
                ["m870"]  = new HashSet<string> { "Shotgun" },
            },
            CanBeUsedAs     = new Dictionary<string, IReadOnlySet<string>>(),
            WeaponToCaliber = new Dictionary<string, string>(),
            KnownItemIds    = new HashSet<string> { "mp133", "mp153", "mp155", "m870" },
        };

        var condition = MakeCondition("c1", "mp133", "mp153");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), cat, NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(
            new[] { "mp133", "mp153", "mp155", "m870" },
            "all Shotgun members are added when no sub-type rule exists");
    }

    [Fact]
    public void PartialShotgunList_WithMPShotgunRule_ExpandsOnlyToMPFamily()
    {
        // With a rule that assigns "MP-Shotgun" to the three MP-family weapons,
        // TypeSelector finds BestType = MP-Shotgun for [mp133, mp153].
        // M870 is only "Shotgun" — it is NOT added.
        var cat = new CategorizationResult
        {
            WeaponTypes = new Dictionary<string, IReadOnlySet<string>>
            {
                ["Shotgun"]    = new HashSet<string> { "mp133", "mp153", "mp155", "m870" },
                ["MP-Shotgun"] = new HashSet<string> { "mp133", "mp153", "mp155" },
            },
            WeaponToType = new Dictionary<string, IReadOnlySet<string>>
            {
                ["mp133"] = new HashSet<string> { "Shotgun", "MP-Shotgun" },
                ["mp153"] = new HashSet<string> { "Shotgun", "MP-Shotgun" },
                ["mp155"] = new HashSet<string> { "Shotgun", "MP-Shotgun" },
                ["m870"]  = new HashSet<string> { "Shotgun" },
            },
            CanBeUsedAs     = new Dictionary<string, IReadOnlySet<string>>(),
            WeaponToCaliber = new Dictionary<string, string>(),
            KnownItemIds    = new HashSet<string> { "mp133", "mp153", "mp155", "m870" },
        };

        var condition = MakeCondition("c1", "mp133", "mp153");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), cat, NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(
            new[] { "mp133", "mp153", "mp155" },
            "MP-Shotgun sub-type excludes M870 which lacks that type");
        condition.Weapon.Should().NotContain("m870");
    }

    // ── canBeUsedAs type-name resolution ────────────────────────────────────

    [Fact]
    public void CanBeUsedAs_TypeNameValue_AddsAllTypeMembers()
    {
        // The TS config used: "5447a9cd4bdc2dbd208b4567": ["M4A1"]
        // where "M4A1" is a type name, not a weapon ID.
        // All members of the M4A1 type should be added when the vanilla gun is in the condition.
        var cat = new CategorizationResult
        {
            WeaponTypes = new Dictionary<string, IReadOnlySet<string>>
            {
                ["AssaultRifle"] = new HashSet<string> { "vanillaM4", "moddedM4a", "moddedM4b" },
                ["M4A1"]         = new HashSet<string> { "vanillaM4", "moddedM4a", "moddedM4b" },
            },
            WeaponToType = new Dictionary<string, IReadOnlySet<string>>
            {
                ["vanillaM4"] = new HashSet<string> { "AssaultRifle", "M4A1" },
                ["moddedM4a"] = new HashSet<string> { "AssaultRifle", "M4A1" },
                ["moddedM4b"] = new HashSet<string> { "AssaultRifle", "M4A1" },
            },
            // canBeUsedAs: vanillaM4 → ["M4A1"] (a type name, not a weapon ID)
            CanBeUsedAs = new Dictionary<string, IReadOnlySet<string>>
            {
                ["vanillaM4"] = new HashSet<string> { "M4A1" }
            },
            WeaponToCaliber = new Dictionary<string, string>(),
            KnownItemIds    = new HashSet<string> { "vanillaM4", "moddedM4a", "moddedM4b" },
        };

        var condition = MakeCondition("c1", "vanillaM4");
        var db        = MakeDb(MakeQuest("q1", condition));
        MakePatcher().Patch(db, EmptySettings(), cat, NullModLogger.Instance);

        // canBeUsedAs runs even for single-weapon conditions;
        // "M4A1" type name expands to all members
        condition.Weapon.Should().BeEquivalentTo(
            new[] { "vanillaM4", "moddedM4a", "moddedM4b" });
    }
}
