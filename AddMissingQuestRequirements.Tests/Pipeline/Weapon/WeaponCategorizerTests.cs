using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Weapon;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Weapon;

public class WeaponCategorizerTests
{
    // ── Shared test tree (12 nodes) ──────────────────────────────────────────
    // root → weapon → sniper_rifle → sv98  (BoltAction=true)
    //                             → dvl   (BoltAction=true)
    //                             → vss   (BoltAction=false)
    //              → assault_rifle → ak74
    //                             → ak47
    //              → pistol → revolver → rhino
    //                      → pm
    //              → shotgun → mp133 (name="MP-133 pump-action 12ga shotgun")
    private static InMemoryItemDatabase MakeDb() => new(
    [
        new ItemNode { Id = "root",          Name = "Item",         ParentId = null,           NodeType = "Node" },
        new ItemNode { Id = "weapon",        Name = "Weapon",       ParentId = "root",          NodeType = "Node" },
        new ItemNode { Id = "sniper_rifle",  Name = "SniperRifle",  ParentId = "weapon",        NodeType = "Node" },
        new ItemNode { Id = "assault_rifle", Name = "AssaultRifle", ParentId = "weapon",        NodeType = "Node" },
        new ItemNode { Id = "pistol",        Name = "Pistol",       ParentId = "weapon",        NodeType = "Node" },
        new ItemNode { Id = "revolver",      Name = "Revolver",     ParentId = "pistol",        NodeType = "Node" },
        new ItemNode { Id = "shotgun",       Name = "Shotgun",      ParentId = "weapon",        NodeType = "Node" },
        new ItemNode { Id = "sv98",  Name = "sv98",  ParentId = "sniper_rifle",  NodeType = "Item",
            Props = new() { ["BoltAction"] = Bool(true) } },
        new ItemNode { Id = "dvl",   Name = "dvl",   ParentId = "sniper_rifle",  NodeType = "Item",
            Props = new() { ["BoltAction"] = Bool(true) } },
        new ItemNode { Id = "vss",   Name = "vss",   ParentId = "sniper_rifle",  NodeType = "Item",
            Props = new() { ["BoltAction"] = Bool(false) } },
        new ItemNode { Id = "ak74",  Name = "ak74",  ParentId = "assault_rifle", NodeType = "Item", Props = [] },
        new ItemNode { Id = "ak47",  Name = "ak47",  ParentId = "assault_rifle", NodeType = "Item", Props = [] },
        new ItemNode { Id = "pm",    Name = "pm",    ParentId = "pistol",        NodeType = "Item", Props = [] },
        new ItemNode { Id = "rhino", Name = "rhino", ParentId = "revolver",      NodeType = "Item", Props = [] },
        new ItemNode { Id = "mp133", Name = "mp133", ParentId = "shotgun",       NodeType = "Item", Props = [] },
    ],
    localeNames: new()
    {
        ["sv98"]  = "SV-98 bolt-action sniper rifle",
        ["dvl"]   = "DVL-10 bolt-action sniper rifle",
        ["vss"]   = "VSS Vintorez 9x39 special sniper rifle",
        ["ak74"]  = "AKS-74U 5.45x39 assault rifle",
        ["ak47"]  = "AKM 7.62x39 assault rifle",
        ["pm"]    = "PM pistol",
        ["rhino"] = "Chiappa Rhino 200DS revolver",
        ["mp133"] = "MP-133 pump-action 12ga shotgun",
    });

    // Default rules matching the C# version's defaults.jsonc
    private static readonly TypeRule[] DefaultRules =
    [
        new TypeRule
        {
            Conditions = new()
            {
                ["hasAncestor"] = Str("SniperRifle"),
                ["properties"]    = JsonDocument.Parse("{\"BoltAction\":true}").RootElement
            },
            Type = "BoltActionSniperRifle",
            AlsoAs = ["SniperRifle"]
        },
        new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("Shotgun"), ["nameContains"] = Str("pump") },
            Type = "PumpActionShotgun",
            AlsoAs = ["Shotgun"]
        },
        new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("Revolver") },
            Type = "Revolver",
            AlsoAs = ["Pistol"]
        },
        new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("Weapon") },
            Type = "{directChildOf:Weapon}",
            AlsoAs = []
        },
    ];

    private static OverriddenSettings EmptySettings() => new();

    // ── categorizeWithLessRestrictive ────────────────────────────────────────

    [Fact]
    public void BoltAction_sniper_appears_in_both_types_when_lessRestrictive_true()
    {
        var config = new ModConfig { IncludeParentCategories = true };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings(), config);

        result.WeaponTypes["BoltActionSniperRifle"].Should().Contain("sv98");
        result.WeaponTypes["SniperRifle"].Should().Contain("sv98");
    }

    [Fact]
    public void BoltAction_sniper_in_both_types_even_when_lessRestrictive_false()
    {
        // With all-rules-fire: sv98/dvl match the BoltActionSniperRifle rule AND the
        // catch-all {directChildOf:Weapon} rule (→ "SniperRifle"). AlsoAs=["SniperRifle"]
        // also adds them. IncludeParentCategories only gates config.ParentTypes traversal.
        var config = new ModConfig { IncludeParentCategories = false };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings(), config);

        result.WeaponTypes["BoltActionSniperRifle"].Should().BeEquivalentTo(["sv98", "dvl"]);
        result.WeaponTypes["SniperRifle"].Should().BeEquivalentTo(["sv98", "dvl", "vss"]);
    }

    [Fact]
    public void Revolver_appears_in_Revolver_and_Pistol_when_lessRestrictive_true()
    {
        var config = new ModConfig { IncludeParentCategories = true };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings(), config);

        result.WeaponTypes["Revolver"].Should().Contain("rhino");
        result.WeaponTypes["Pistol"].Should().Contain("rhino");
    }

    [Fact]
    public void Non_bolt_sniper_VSS_classified_as_SniperRifle_only()
    {
        var config = new ModConfig { IncludeParentCategories = true };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings(), config);

        result.WeaponToType["vss"].Should().BeEquivalentTo(["SniperRifle"]);
    }

    [Fact]
    public void PumpAction_shotgun_detected_by_name()
    {
        var config = new ModConfig { IncludeParentCategories = true };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings(), config);

        result.WeaponTypes["PumpActionShotgun"].Should().Contain("mp133");
        result.WeaponTypes["Shotgun"].Should().Contain("mp133");
    }

    // ── Manual Override ──────────────────────────────────────────────────────

    [Fact]
    public void Manual_Override_merges_with_core_rules_but_suppresses_non_core()
    {
        // Behaviour change (Task 4): manual overrides no longer replace all rule output.
        // Core rules (hasAncestor, caliber, properties) are merged on top of the override;
        // non-core rules (nameContains, nameMatches, etc.) are suppressed.
        //
        // ak74 has override "Pistol".
        // DefaultRules catch-all: { hasAncestor: Weapon } → AssaultRifle  — CORE → merges in.
        // DefaultRules pump-action: { hasAncestor: Shotgun, nameContains: pump } — NOT CORE,
        //   but ak74 doesn't match it anyway (not under Shotgun).
        // So ak74 final types: { Pistol, AssaultRifle }.
        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["ak74"] = "Pistol" }
        };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings, new ModConfig());

        result.WeaponToType["ak74"].Should().BeEquivalentTo(["Pistol", "AssaultRifle"]);
        result.WeaponTypes["Pistol"].Should().Contain("ak74");
        // Core catch-all fires and assigns AssaultRifle even though override said Pistol.
        result.WeaponTypes["AssaultRifle"].Should().Contain("ak74");
    }

    [Fact]
    public void Manual_Override_types_walk_parentTypes_chain()
    {
        // ak74 gets manualTypeOverride "GrenadeLauncher". config.ParentTypes maps
        // GrenadeLauncher → explosive. The manual-override path must walk parents
        // so ak74's final type set includes `explosive`, otherwise umbrella types
        // like "explosive" (rolling up grenade + launcher families) fail for any
        // weapon whose only source of the parent-bearing type is a manual override.
        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["ak74"] = "GrenadeLauncher" }
        };
        var config = new ModConfig
        {
            IncludeParentCategories = true,
            ParentTypes = new() { ["GrenadeLauncher"] = "explosive" }
        };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings, config);

        result.WeaponToType["ak74"].Should().Contain("GrenadeLauncher");
        result.WeaponToType["ak74"].Should().Contain("explosive");
        result.WeaponTypes.Should().ContainKey("explosive");
        result.WeaponTypes["explosive"].Should().Contain("ak74");
    }

    [Fact]
    public void Manual_Override_parent_walk_respects_IncludeParentCategories_false()
    {
        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["ak74"] = "GrenadeLauncher" }
        };
        var config = new ModConfig
        {
            IncludeParentCategories = false,
            ParentTypes = new() { ["GrenadeLauncher"] = "explosive" }
        };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings, config);

        result.WeaponToType["ak74"].Should().Contain("GrenadeLauncher");
        result.WeaponToType["ak74"].Should().NotContain("explosive");
    }

    [Fact]
    public void Manual_Override_comma_separated_types_adds_to_multiple_and_merges_catch_all()
    {
        // Override "Knife,Tool" seeds the type set. The catch-all {hasAncestor:Weapon} rule
        // is core (uses only hasAncestor), so its resolved type ("Pistol" via {directChildOf:Weapon})
        // MUST be added under the merge semantics — proving the merge is actually taking effect.
        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["pm"] = "Knife,Tool" }
        };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings, new ModConfig());

        result.WeaponToType["pm"].Should().BeEquivalentTo(["Knife", "Tool", "Pistol"]);
        result.WeaponTypes["Knife"].Should().Contain("pm");
        result.WeaponTypes["Tool"].Should().Contain("pm");
        result.WeaponTypes["Pistol"].Should().Contain("pm");
    }

    // ── Abstract nodes skipped ───────────────────────────────────────────────

    [Fact]
    public void Abstract_nodes_are_not_categorized()
    {
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings(), new ModConfig());

        result.WeaponToType.Should().NotContainKey("weapon");
        result.WeaponToType.Should().NotContainKey("sniper_rifle");
        result.WeaponToType.Should().NotContainKey("root");
    }

    // ── WeaponToType bidirectional map ───────────────────────────────────────

    [Fact]
    public void WeaponToType_contains_all_categorized_items()
    {
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings(), new ModConfig { IncludeParentCategories = true });

        result.WeaponToType.Keys.Should().BeEquivalentTo(
            ["sv98", "dvl", "vss", "ak74", "ak47", "pm", "rhino", "mp133"]);
    }

    // ── canBeUsedAs alias group expansion ───────────────────────────────────

    [Fact]
    public void Alias_group_expansion_cross_links_transitive_members()
    {
        // A→[B] and C→[B] — after expansion A↔B↔C all linked
        var settings = new OverriddenSettings
        {
            CanBeUsedAs = new()
            {
                ["ak74"]  = ["ak47"],
                ["ak47"] = ["sv98"]
            }
        };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings, new ModConfig());

        // All three should be cross-linked
        result.CanBeUsedAs["ak74"].Should().Contain("sv98");
        result.CanBeUsedAs["sv98"].Should().Contain("ak74");
    }

    // ── Short-name alias matching ────────────────────────────────────────────

    [Fact]
    public void Short_name_alias_links_items_with_matching_name_after_stripping_whitelist_words()
    {
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",   Name = "Item",   ParentId = null,     NodeType = "Node" },
            new ItemNode { Id = "weapon", Name = "Weapon", ParentId = "root",    NodeType = "Node" },
            new ItemNode { Id = "ar",     Name = "AR",     ParentId = "weapon",  NodeType = "Node" },
            new ItemNode { Id = "m4",     Name = "m4",     ParentId = "ar",      NodeType = "Item", Props = [] },
            new ItemNode { Id = "m4_fde", Name = "m4_fde", ParentId = "ar",     NodeType = "Item", Props = [] },
        ],
        localeNames: new()
        {
            ["m4"]     = "M4A1 assault rifle",
            ["m4_fde"] = "M4A1 FDE assault rifle",
        });

        var settings = new OverriddenSettings
        {
            AliasNameStripWords = ["FDE"]
        };
        var catchAll = new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = JsonDocument.Parse("\"Weapon\"").RootElement },
            Type = "{directChildOf:Weapon}", AlsoAs = []
        };
        var result = new WeaponCategorizer([catchAll])
            .Categorize(db, settings, new ModConfig());

        // "M4A1 FDE" stripped of "FDE" → "M4A1" which matches "M4A1" → they're aliases
        result.CanBeUsedAs.Should().ContainKey("m4");
        result.CanBeUsedAs["m4"].Should().Contain("m4_fde");
        result.CanBeUsedAs["m4_fde"].Should().Contain("m4");
    }

    // ── kindOf parent-type resolution ────────────────────────────────────────

    [Fact]
    public void KindOf_adds_parent_type_when_lessRestrictive_true()
    {
        // Rule has no alsoAs — kindOf must provide the parent link
        var rule = new TypeRule
        {
            Conditions = new()
            {
                ["hasAncestor"] = Str("SniperRifle"),
                ["properties"]    = JsonDocument.Parse("{\"BoltAction\":true}").RootElement
            },
            Type   = "BoltActionSniperRifle",
            AlsoAs = []
        };
        var config = new ModConfig
        {
            IncludeParentCategories = true,
            ParentTypes = new() { ["BoltActionSniperRifle"] = "SniperRifle" }
        };
        var result = new WeaponCategorizer([rule])
            .Categorize(MakeDb(), EmptySettings(), config);

        result.WeaponTypes["BoltActionSniperRifle"].Should().Contain("sv98");
        result.WeaponTypes["SniperRifle"].Should().Contain("sv98");
    }

    [Fact]
    public void KindOf_not_applied_when_lessRestrictive_false()
    {
        var rule = new TypeRule
        {
            Conditions = new()
            {
                ["hasAncestor"] = Str("SniperRifle"),
                ["properties"]    = JsonDocument.Parse("{\"BoltAction\":true}").RootElement
            },
            Type   = "BoltActionSniperRifle",
            AlsoAs = []
        };
        var config = new ModConfig
        {
            IncludeParentCategories = false,
            ParentTypes = new() { ["BoltActionSniperRifle"] = "SniperRifle" }
        };
        var result = new WeaponCategorizer([rule])
            .Categorize(MakeDb(), EmptySettings(), config);

        result.WeaponTypes["BoltActionSniperRifle"].Should().Contain("sv98");
        result.WeaponTypes.Should().NotContainKey("SniperRifle");
    }

    [Fact]
    public void KindOf_walks_chain_transitively()
    {
        var rule = new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("SniperRifle") },
            Type   = "CustomSniper",
            AlsoAs = []
        };
        var config = new ModConfig
        {
            IncludeParentCategories = true,
            ParentTypes = new()
            {
                ["CustomSniper"]          = "BoltActionSniperRifle",
                ["BoltActionSniperRifle"] = "SniperRifle"
            }
        };
        var result = new WeaponCategorizer([rule])
            .Categorize(MakeDb(), EmptySettings(), config);

        result.WeaponTypes["CustomSniper"].Should().Contain("sv98");
        result.WeaponTypes["BoltActionSniperRifle"].Should().Contain("sv98");
        result.WeaponTypes["SniperRifle"].Should().Contain("sv98");
    }

    // ── TypeRules from settings ──────────────────────────────────────────────

    [Fact]
    public void TypeRules_from_settings_are_applied_alongside_constructor_rules()
    {
        var userRule = new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("AssaultRifle") },
            Type   = "UserDefinedAR",
            AlsoAs = []
        };
        var settings = new OverriddenSettings { TypeRules = [userRule] };

        // WeaponCategorizer constructed with NO built-in rules
        var result = new WeaponCategorizer([])
            .Categorize(MakeDb(), settings, new ModConfig());

        result.WeaponTypes.Should().ContainKey("UserDefinedAR");
        result.WeaponTypes["UserDefinedAR"].Should().Contain("ak74");
    }

    [Fact]
    public void ManualTypeOverrides_and_TypeRules_combine_into_same_type_group()
    {
        var userRule = new TypeRule
        {
            Conditions = new() { ["nameMatches"] = Str("AKM") },
            Type = "AKM_Group",
            AlsoAs = []
        };
        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["ak74"] = "AKM_Group" },
            TypeRules = [userRule]
        };

        var result = new WeaponCategorizer([])
            .Categorize(MakeDb(), settings, new ModConfig());

        // ak74 added via manual type override (which was what 'ids' migrated to)
        // ak47 added via TypeRule (locale name contains "AKM")
        result.WeaponTypes["AKM_Group"].Should().Contain("ak74");
        result.WeaponTypes["AKM_Group"].Should().Contain("ak47");
        result.WeaponTypes["AKM_Group"].Should().HaveCount(2);
    }

    // ── Alias graph — unknown ID guard ───────────────────────────────────────

    [Fact]
    public void Unknown_weapon_id_in_manual_alias_is_not_introduced_as_graph_key()
    {
        var settings = new OverriddenSettings
        {
            CanBeUsedAs = new() { ["ak74"] = ["completely_unknown_id"] }
        };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings, new ModConfig());

        result.CanBeUsedAs.Should().NotContainKey("completely_unknown_id");
    }

    // ── Non-weapon items never categorized ──────────────────────────────────

    /// <summary>
    /// A user TypeRule without hasAncestor guard (e.g. nameContains only) must NOT match
    /// ammo or mod items even though they share the same database.
    /// Regression: before the ancestry pre-filter was added, the rule engine received ALL
    /// leaf items, so any rule whose conditions matched by name/caliber/property would
    /// also categorize ammo/mods.
    /// </summary>
    [Fact]
    public void Ammo_and_mod_items_are_not_categorized_even_when_user_rule_has_no_ancestor_guard()
    {
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",      Name = "Item",         ParentId = null,      NodeType = "Node" },
            new ItemNode { Id = "weapon",    Name = "Weapon",       ParentId = "root",    NodeType = "Node" },
            new ItemNode { Id = "ar",        Name = "AssaultRifle", ParentId = "weapon",  NodeType = "Node" },
            new ItemNode { Id = "ammo_node", Name = "Ammo",         ParentId = "root",    NodeType = "Node" },
            new ItemNode { Id = "ammo_cat",  Name = "Ammo545",      ParentId = "ammo_node", NodeType = "Node" },
            new ItemNode { Id = "mod_node",  Name = "Mod",          ParentId = "root",    NodeType = "Node" },
            new ItemNode { Id = "sight_cat", Name = "Sights",       ParentId = "mod_node", NodeType = "Node" },
            // Leaf items
            new ItemNode { Id = "ak74",   Name = "AKS74U", ParentId = "ar",       NodeType = "Item", Props = [] },
            new ItemNode { Id = "bullet", Name = "545BP",  ParentId = "ammo_cat", NodeType = "Item", Props = [] },
            new ItemNode { Id = "scope",  Name = "PSO1",   ParentId = "sight_cat",NodeType = "Item", Props = [] },
        ],
        localeNames: new()
        {
            ["ak74"]   = "AKS-74U 5.45x39 assault rifle",
            ["bullet"] = "5.45x39mm BP gs",
            ["scope"]  = "PSO-1 scope",
        });

        // A user rule with no hasAncestor guard — matches by name substring only.
        // "5.45" appears in both the weapon locale and the ammo locale.
        var userRule = new TypeRule
        {
            Conditions = new() { ["nameContains"] = Str("5.45") },
            Type = "AK545",
            AlsoAs = []
        };

        var result = new WeaponCategorizer([userRule])
            .Categorize(db, EmptySettings(), new ModConfig());

        result.WeaponToType.Should().ContainKey("ak74");
        result.WeaponToType.Should().NotContainKey("bullet");
        result.WeaponToType.Should().NotContainKey("scope");
    }

    // ── AlsoAs always applied ────────────────────────────────────────────────

    [Fact]
    public void AlsoAs_types_applied_even_when_lessRestrictive_false()
    {
        // AlsoAs is a rule-level explicit multi-type declaration and must always apply.
        // IncludeParentCategories should only gate the config.ParentTypes transitive walk.
        var config = new ModConfig { IncludeParentCategories = false };
        var result = new WeaponCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings(), config);

        // BoltActionSniperRifle rule has AlsoAs = ["SniperRifle"]
        result.WeaponTypes["BoltActionSniperRifle"].Should().Contain("sv98");
        result.WeaponTypes["SniperRifle"].Should().Contain("sv98");
        // Revolver rule has AlsoAs = ["Pistol"]
        result.WeaponTypes["Revolver"].Should().Contain("rhino");
        result.WeaponTypes["Pistol"].Should().Contain("rhino");
    }

    // ── WeaponLikeAncestors pre-filter ───────────────────────────────────────

    [Fact]
    public void Melee_weapon_under_MeleeWeapon_ancestor_is_categorized()
    {
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",  Name = "Item",        ParentId = null,   NodeType = "Node" },
            new ItemNode { Id = "melee", Name = "MeleeWeapon", ParentId = "root", NodeType = "Node" },
            new ItemNode { Id = "knife1", Name = "knife1", ParentId = "melee", NodeType = "Item", Props = [] },
        ],
        localeNames: new() { ["knife1"] = "Combat knife" });

        var rules = new TypeRule[]
        {
            new()
            {
                Conditions = new() { ["hasAncestor"] = Str("MeleeWeapon") },
                Type = "Melee",
                AlsoAs = []
            }
        };

        var config = new ModConfig
        {
            IncludeParentCategories = true,
            WeaponLikeAncestors = ["Weapon", "MeleeWeapon", "ThrowWeap"]
        };

        var result = new WeaponCategorizer(rules).Categorize(db, EmptySettings(), config);

        result.WeaponToType.Should().ContainKey("knife1");
        result.WeaponTypes["Melee"].Should().Contain("knife1");
    }

    [Fact]
    public void Grenade_under_ThrowWeap_ancestor_is_categorized()
    {
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",  Name = "Item",       ParentId = null,   NodeType = "Node" },
            new ItemNode { Id = "throw", Name = "ThrowWeap",  ParentId = "root", NodeType = "Node" },
            new ItemNode { Id = "f1", Name = "f1", ParentId = "throw", NodeType = "Item", Props = [] },
        ],
        localeNames: new() { ["f1"] = "F-1 grenade" });

        var rules = new TypeRule[]
        {
            new()
            {
                Conditions = new() { ["hasAncestor"] = Str("ThrowWeap") },
                Type = "Grenade",
                AlsoAs = []
            }
        };

        var config = new ModConfig
        {
            IncludeParentCategories = true,
            WeaponLikeAncestors = ["Weapon", "MeleeWeapon", "ThrowWeap"]
        };

        var result = new WeaponCategorizer(rules).Categorize(db, EmptySettings(), config);

        result.WeaponToType.Should().ContainKey("f1");
        result.WeaponTypes["Grenade"].Should().Contain("f1");
    }

    [Fact]
    public void Items_outside_configured_ancestors_are_excluded()
    {
        // Sanity: leaving WeaponLikeAncestors at ["Weapon"] keeps melee items out.
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",  Name = "Item",        ParentId = null,   NodeType = "Node" },
            new ItemNode { Id = "melee", Name = "MeleeWeapon", ParentId = "root", NodeType = "Node" },
            new ItemNode { Id = "knife1", Name = "knife1", ParentId = "melee", NodeType = "Item", Props = [] },
        ],
        localeNames: new() { ["knife1"] = "Combat knife" });

        var rules = new TypeRule[]
        {
            new()
            {
                Conditions = new() { ["hasAncestor"] = Str("MeleeWeapon") },
                Type = "Melee",
                AlsoAs = []
            }
        };

        var config = new ModConfig
        {
            IncludeParentCategories = true,
            WeaponLikeAncestors = ["Weapon"]
        };

        var result = new WeaponCategorizer(rules).Categorize(db, EmptySettings(), config);

        result.WeaponToType.Should().NotContainKey("knife1");
    }

    // ── AliasNameExcludeWeapons ──────────────────────────────────────────────

    /// <summary>
    /// Three items A, B, C all share the same normalized name "Acme".
    /// Excluding B by its weapon ID means A↔C are cross-linked, but B is not
    /// linked to either A or C in either direction.
    /// </summary>
    [Fact]
    public void Alias_exclude_by_id_skips_both_directions_of_cross_link()
    {
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",   Name = "Item",   ParentId = null,    NodeType = "Node" },
            new ItemNode { Id = "weapon", Name = "Weapon", ParentId = "root",  NodeType = "Node" },
            new ItemNode { Id = "ar",     Name = "AR",     ParentId = "weapon", NodeType = "Node" },
            new ItemNode { Id = "acme_a", Name = "acme_a", ParentId = "ar",    NodeType = "Item", Props = [] },
            new ItemNode { Id = "acme_b", Name = "acme_b", ParentId = "ar",    NodeType = "Item", Props = [] },
            new ItemNode { Id = "acme_c", Name = "acme_c", ParentId = "ar",    NodeType = "Item", Props = [] },
        ],
        localeNames: new()
        {
            ["acme_a"] = "Acme rifle",
            ["acme_b"] = "Acme rifle",
            ["acme_c"] = "Acme rifle",
        });

        var settings = new OverriddenSettings
        {
            // Exclude B by its weapon ID
            AliasNameExcludeWeapons = ["acme_b"]
        };

        var catchAll = new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("Weapon") },
            Type = "{directChildOf:Weapon}",
            AlsoAs = []
        };

        var result = new WeaponCategorizer([catchAll])
            .Categorize(db, settings, new ModConfig());

        // A↔C should be cross-linked
        result.CanBeUsedAs.Should().ContainKey("acme_a");
        result.CanBeUsedAs["acme_a"].Should().Contain("acme_c");
        result.CanBeUsedAs.Should().ContainKey("acme_c");
        result.CanBeUsedAs["acme_c"].Should().Contain("acme_a");

        // B must not appear as a source or target in the alias graph from name-matching
        result.CanBeUsedAs.Should().NotContainKey("acme_b");
        result.CanBeUsedAs["acme_a"].Should().NotContain("acme_b");
        result.CanBeUsedAs["acme_c"].Should().NotContain("acme_b");
    }

    /// <summary>
    /// Three items all share normalized name "Acme". Excluding by the name "Acme" means
    /// all three are excluded — the group dissolves entirely (no cross-links at all).
    /// </summary>
    [Fact]
    public void Alias_exclude_by_name_skips_both_directions_of_cross_link()
    {
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",   Name = "Item",   ParentId = null,    NodeType = "Node" },
            new ItemNode { Id = "weapon", Name = "Weapon", ParentId = "root",  NodeType = "Node" },
            new ItemNode { Id = "ar",     Name = "AR",     ParentId = "weapon", NodeType = "Node" },
            new ItemNode { Id = "acme_a", Name = "acme_a", ParentId = "ar",    NodeType = "Item", Props = [] },
            new ItemNode { Id = "acme_b", Name = "acme_b", ParentId = "ar",    NodeType = "Item", Props = [] },
            new ItemNode { Id = "acme_c", Name = "acme_c", ParentId = "ar",    NodeType = "Item", Props = [] },
        ],
        localeNames: new()
        {
            ["acme_a"] = "Acme rifle",
            ["acme_b"] = "Acme rifle",
            ["acme_c"] = "Acme rifle",
        });

        var settings = new OverriddenSettings
        {
            // Exclude by normalized name — strip "rifle" → "Acme" remains; configure "rifle" as
            // a strip word so the normalized name is just "Acme", which we then exclude.
            AliasNameStripWords = ["rifle"],
            AliasNameExcludeWeapons = ["Acme"]  // case-insensitive match against normalized name
        };

        var catchAll = new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("Weapon") },
            Type = "{directChildOf:Weapon}",
            AlsoAs = []
        };

        var result = new WeaponCategorizer([catchAll])
            .Categorize(db, settings, new ModConfig());

        // All three excluded from name-alias loop → no cross-links from name matching
        result.CanBeUsedAs.Should().NotContainKey("acme_a");
        result.CanBeUsedAs.Should().NotContainKey("acme_b");
        result.CanBeUsedAs.Should().NotContainKey("acme_c");
    }

    /// <summary>
    /// B is excluded from the auto-alias name loop, but an explicit manual
    /// CanBeUsedAs entry A→[B] must still be honoured.
    /// </summary>
    [Fact]
    public void Manual_can_be_used_as_still_applies_for_excluded_weapons()
    {
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",   Name = "Item",   ParentId = null,    NodeType = "Node" },
            new ItemNode { Id = "weapon", Name = "Weapon", ParentId = "root",  NodeType = "Node" },
            new ItemNode { Id = "ar",     Name = "AR",     ParentId = "weapon", NodeType = "Node" },
            new ItemNode { Id = "acme_a", Name = "acme_a", ParentId = "ar",    NodeType = "Item", Props = [] },
            new ItemNode { Id = "acme_b", Name = "acme_b", ParentId = "ar",    NodeType = "Item", Props = [] },
        ],
        localeNames: new()
        {
            ["acme_a"] = "Acme rifle",
            ["acme_b"] = "Acme rifle",
        });

        var settings = new OverriddenSettings
        {
            // Exclude B from the auto-alias loop by ID
            AliasNameExcludeWeapons = ["acme_b"],
            // But explicitly link A→B manually
            CanBeUsedAs = new() { ["acme_a"] = ["acme_b"] }
        };

        var catchAll = new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("Weapon") },
            Type = "{directChildOf:Weapon}",
            AlsoAs = []
        };

        var result = new WeaponCategorizer([catchAll])
            .Categorize(db, settings, new ModConfig());

        // Manual alias A→B must survive
        result.CanBeUsedAs.Should().ContainKey("acme_a");
        result.CanBeUsedAs["acme_a"].Should().Contain("acme_b");
    }

    /// <summary>
    /// If an AliasNameExcludeWeapons entry is composed entirely of strip-words (e.g. the
    /// user writes "FDE" while AliasNameStripWords = ["FDE"]), the normalized result is "".
    /// That empty string must NOT be added to excludeNames — otherwise it would match any
    /// weapon whose name also normalizes to "", blocking all normal grouping.
    /// Regression for the .Where(!IsNullOrWhiteSpace) guard added in WeaponCategorizer.
    /// </summary>
    [Fact]
    public void Alias_exclude_entry_composed_entirely_of_strip_words_is_ignored()
    {
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",   Name = "Item",   ParentId = null,     NodeType = "Node" },
            new ItemNode { Id = "weapon", Name = "Weapon", ParentId = "root",   NodeType = "Node" },
            new ItemNode { Id = "ar",     Name = "AR",     ParentId = "weapon", NodeType = "Node" },
            new ItemNode { Id = "acme_a", Name = "acme_a", ParentId = "ar",     NodeType = "Item", Props = [] },
            new ItemNode { Id = "acme_b", Name = "acme_b", ParentId = "ar",     NodeType = "Item", Props = [] },
        ],
        localeNames: new()
        {
            ["acme_a"] = "Acme rifle",
            ["acme_b"] = "Acme rifle",
        });

        var settings = new OverriddenSettings
        {
            // "FDE" is the only strip word; "FDE" as an exclude entry normalizes to "" after stripping.
            // The guard must drop "" so acme_a and acme_b are still cross-linked normally.
            AliasNameStripWords = ["FDE"],
            AliasNameExcludeWeapons = ["FDE"]
        };

        var catchAll = new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("Weapon") },
            Type = "{directChildOf:Weapon}",
            AlsoAs = []
        };

        var result = new WeaponCategorizer([catchAll])
            .Categorize(db, settings, new ModConfig());

        // Neither weapon has "FDE" in its name, so their normalized names are identical:
        // "Acme rifle" → strip nothing → "Acme rifle".  They share a group and must be
        // cross-linked even though the exclude list contained the (empty-after-strip) entry.
        result.CanBeUsedAs.Should().ContainKey("acme_a");
        result.CanBeUsedAs["acme_a"].Should().Contain("acme_b");
        result.CanBeUsedAs.Should().ContainKey("acme_b");
        result.CanBeUsedAs["acme_b"].Should().Contain("acme_a");
    }

    // ── Merge-semantics: manual override + core rules ────────────────────────

    /// <summary>
    /// Acceptance criterion 1: weapon with manual override "Shotgun,Revolver" AND a core
    /// caliber rule → final types include both the override types AND the caliber-rule type.
    /// </summary>
    [Fact]
    public void Manual_override_merges_with_caliber_rule_match()
    {
        // Add a weapon node with ammoCaliber = "Caliber12g" to the standard DB tree.
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",    Name = "Item",    ParentId = null,    NodeType = "Node" },
            new ItemNode { Id = "weapon",  Name = "Weapon",  ParentId = "root",  NodeType = "Node" },
            new ItemNode { Id = "shotgun", Name = "Shotgun", ParentId = "weapon", NodeType = "Node" },
            new ItemNode
            {
                Id = "sg_mod", Name = "sg_mod", ParentId = "shotgun", NodeType = "Item",
                Props = new() { ["ammoCaliber"] = JsonDocument.Parse("\"Caliber12g\"").RootElement }
            },
        ],
        localeNames: new() { ["sg_mod"] = "Modded 12ga shotgun" });

        var caliberRule = new TypeRule
        {
            Conditions = new() { ["caliber"] = Str("Caliber12g") },
            Type = "12ga",
            AlsoAs = []
        };

        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["sg_mod"] = "Shotgun,Revolver" }
        };

        var result = new WeaponCategorizer([caliberRule])
            .Categorize(db, settings, new ModConfig());

        // Override types + core caliber rule type must all be present.
        result.WeaponToType["sg_mod"].Should().BeEquivalentTo(["Shotgun", "Revolver", "12ga"]);
    }

    /// <summary>
    /// Acceptance criterion 2: weapon with manual override "russian_ar" AND a core hasAncestor
    /// rule → final types include both the override type AND the ancestor-rule type.
    /// </summary>
    [Fact]
    public void Manual_override_merges_with_hasAncestor_rule_match()
    {
        // ar_mod sits under assault_rifle → under weapon, so hasAncestor=Weapon fires.
        var catchAllRule = new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = Str("Weapon") },
            Type = "AssaultRifle",
            AlsoAs = []
        };

        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["ak74"] = "russian_ar" }
        };

        var result = new WeaponCategorizer([catchAllRule])
            .Categorize(MakeDb(), settings, new ModConfig());

        // russian_ar from manual override + AssaultRifle from core hasAncestor rule.
        result.WeaponToType["ak74"].Should().BeEquivalentTo(["russian_ar", "AssaultRifle"]);
    }

    /// <summary>
    /// Acceptance criterion (properties): weapon with manual override AND a core properties
    /// rule → final types include both.
    /// </summary>
    [Fact]
    public void Manual_override_merges_with_properties_rule_match()
    {
        // sv98 has BoltAction=true.
        var boltRule = new TypeRule
        {
            Conditions = new() { ["properties"] = JsonDocument.Parse("{\"BoltAction\":true}").RootElement },
            Type = "BoltAction",
            AlsoAs = []
        };

        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["sv98"] = "CustomSniper" }
        };

        var result = new WeaponCategorizer([boltRule])
            .Categorize(MakeDb(), settings, new ModConfig());

        // CustomSniper from manual override + BoltAction from core properties rule.
        result.WeaponToType["sv98"].Should().BeEquivalentTo(["CustomSniper", "BoltAction"]);
    }

    /// <summary>
    /// Acceptance criterion 3: weapon with manual override "X" AND a non-core nameMatches rule
    /// → final types == { X } only (nameMatches suppressed when override is present).
    /// </summary>
    [Fact]
    public void Manual_override_suppresses_nameMatches_rule()
    {
        // ak47 locale name is "AKM 7.62x39 assault rifle" — nameMatches "AKM" would fire.
        var nameRule = new TypeRule
        {
            Conditions = new() { ["nameMatches"] = Str("AKM") },
            Type = "AKM_Group",
            AlsoAs = []
        };

        var settings = new OverriddenSettings
        {
            ManualTypeOverrides = new() { ["ak47"] = "X" }
        };

        var result = new WeaponCategorizer([nameRule])
            .Categorize(MakeDb(), settings, new ModConfig());

        // Non-core nameMatches rule must be suppressed; only manual override type "X" remains.
        result.WeaponToType["ak47"].Should().BeEquivalentTo(["X"]);
        result.WeaponTypes.Should().NotContainKey("AKM_Group");
    }

    /// <summary>
    /// Acceptance criterion 4 (no override path): when no manual override is present,
    /// all rules fire as before — no behaviour change.
    /// </summary>
    [Fact]
    public void No_manual_override_all_rules_fire_unchanged()
    {
        var nameRule = new TypeRule
        {
            Conditions = new() { ["nameMatches"] = Str("AKM") },
            Type = "AKM_Group",
            AlsoAs = []
        };

        // ak47 locale: "AKM 7.62x39 assault rifle" — matches nameMatches rule.
        var result = new WeaponCategorizer([nameRule])
            .Categorize(MakeDb(), EmptySettings(), new ModConfig());

        // No override → nameMatches fires normally.
        result.WeaponToType.Should().ContainKey("ak47");
        result.WeaponToType["ak47"].Should().Contain("AKM_Group");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JsonElement Str(string v) =>
        JsonDocument.Parse($"\"{v}\"").RootElement;

    private static JsonElement Bool(bool v) =>
        JsonDocument.Parse(v ? "true" : "false").RootElement;
}
