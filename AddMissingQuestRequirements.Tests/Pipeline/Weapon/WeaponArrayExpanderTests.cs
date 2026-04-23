using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Weapon;

/// <summary>
/// Unit tests for <see cref="WeaponArrayExpander"/> focused on caliber-filter normalization.
/// </summary>
public class WeaponArrayExpanderTests
{
    // ── Fixture helpers ──────────────────────────────────────────────────────

    private static WeaponArrayExpander MakeExpander()
    {
        return new WeaponArrayExpander(new TypeSelector(), NullNameResolver.Instance);
    }

    private static ModConfig DefaultConfig()
    {
        return new ModConfig();
    }

    /// <summary>
    /// Builds a <see cref="CategorizationResult"/> with two weapons in type
    /// <c>"cal_556x45NATO"</c>, both having SPT caliber ID <c>"Caliber556x45NATO"</c>.
    /// </summary>
    private static CategorizationResult MakeCat556()
    {
        var weaponTypes = new Dictionary<string, IReadOnlySet<string>>
        {
            ["cal_556x45NATO"] = new HashSet<string> { "weapon_a", "weapon_b" },
        };
        var weaponToType = new Dictionary<string, IReadOnlySet<string>>
        {
            ["weapon_a"] = new HashSet<string> { "cal_556x45NATO" },
            ["weapon_b"] = new HashSet<string> { "cal_556x45NATO" },
        };
        var weaponToCaliber = new Dictionary<string, string>
        {
            ["weapon_a"] = "Caliber556x45NATO",
            ["weapon_b"] = "Caliber556x45NATO",
        };

        return new CategorizationResult
        {
            WeaponTypes     = weaponTypes,
            WeaponToType    = weaponToType,
            CanBeUsedAs     = new Dictionary<string, IReadOnlySet<string>>(),
            WeaponToCaliber = weaponToCaliber,
            KnownItemIds    = new HashSet<string> { "weapon_a", "weapon_b" },
        };
    }

    /// <summary>
    /// Builds a minimal <see cref="CategorizationResult"/> with no categorized weapons.
    /// The <paramref name="knownItemIds"/> set controls which IDs are considered "in the DB".
    /// </summary>
    private static CategorizationResult MakeEmptyCat(IReadOnlySet<string> knownItemIds)
    {
        return new CategorizationResult
        {
            WeaponTypes     = new Dictionary<string, IReadOnlySet<string>>(),
            WeaponToType    = new Dictionary<string, IReadOnlySet<string>>(),
            CanBeUsedAs     = new Dictionary<string, IReadOnlySet<string>>(),
            WeaponToCaliber = new Dictionary<string, string>(),
            KnownItemIds    = knownItemIds,
        };
    }

    // ── Core scenario: display caliber string filters correctly ─────────────

    /// <summary>
    /// A condition whose <c>weaponCaliber</c> is <c>["5.56x45"]</c> (display form) and whose
    /// two weapons both store <c>"Caliber556x45NATO"</c> as their SPT ammoCaliber id.
    /// Before the fix, the HashSet.Contains never matched — both weapons were dropped.
    /// After the fix, both weapons must survive in the After list.
    /// </summary>
    [Fact]
    public void Display_caliber_filter_retains_matching_weapons()
    {
        var cat = MakeCat556();

        var condition = new ConditionNode
        {
            Id            = "c1",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon_a", "weapon_b"],
            WeaponCaliber = ["5.56x45"],   // display form — not a raw SPT id
        };

        MakeExpander().Expand(
            condition,
            overrideEntry: null,
            categorization: cat,
            config: DefaultConfig(),
            logger: NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(["weapon_a", "weapon_b"],
            because: "both weapons match Caliber556x45NATO which '5.56x45' normalises to");
    }

    /// <summary>
    /// Same setup but the caliber filter is already in SPT CaliberXXX form — should still work.
    /// </summary>
    [Fact]
    public void SPT_caliber_id_filter_retains_matching_weapons()
    {
        var cat = MakeCat556();

        var condition = new ConditionNode
        {
            Id            = "c2",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon_a", "weapon_b"],
            WeaponCaliber = ["Caliber556x45NATO"],   // already SPT form
        };

        MakeExpander().Expand(
            condition,
            overrideEntry: null,
            categorization: cat,
            config: DefaultConfig(),
            logger: NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(["weapon_a", "weapon_b"],
            because: "SPT id pass-through still matches stored caliber");
    }

    /// <summary>
    /// A caliber filter for a different caliber should exclude weapons that do not match.
    /// </summary>
    [Fact]
    public void Caliber_filter_excludes_non_matching_weapons()
    {
        var cat = MakeCat556();

        var condition = new ConditionNode
        {
            Id            = "c3",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon_a", "weapon_b"],
            WeaponCaliber = ["9x19"],   // display form → Caliber9x19PARA; weapons are 556
        };

        MakeExpander().Expand(
            condition,
            overrideEntry: null,
            categorization: cat,
            config: DefaultConfig(),
            logger: NullModLogger.Instance);

        condition.Weapon.Should().BeEmpty(
            because: "no weapons match the 9x19 caliber filter");
    }

    /// <summary>
    /// No caliber filter — all weapons in the expanded type are included regardless.
    /// </summary>
    [Fact]
    public void No_caliber_filter_includes_all_type_members()
    {
        var cat = MakeCat556();

        var condition = new ConditionNode
        {
            Id            = "c4",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon_a", "weapon_b"],
            WeaponCaliber = [],   // no caliber constraint
        };

        MakeExpander().Expand(
            condition,
            overrideEntry: null,
            categorization: cat,
            config: DefaultConfig(),
            logger: NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(["weapon_a", "weapon_b"]);
    }

    // ── UnknownWeaponHandling enum ───────────────────────────────────────────

    /// <summary>
    /// Builds a categorization with four pistols (weapon1..4) in one type plus optional
    /// uncategorized-in-DB IDs. Used by the enum scenario tests.
    /// </summary>
    private static CategorizationResult MakePistols(params string[] extraKnownItemIds)
    {
        var pistols = new HashSet<string> { "weapon1", "weapon2", "weapon3", "weapon4" };

        var weaponTypes = new Dictionary<string, IReadOnlySet<string>>
        {
            ["Pistol"] = pistols,
        };

        var weaponToType = new Dictionary<string, IReadOnlySet<string>>();
        foreach (var id in pistols)
        {
            weaponToType[id] = new HashSet<string> { "Pistol" };
        }

        var known = new HashSet<string>(pistols);
        foreach (var id in extraKnownItemIds)
        {
            known.Add(id);
        }

        return new CategorizationResult
        {
            WeaponTypes     = weaponTypes,
            WeaponToType    = weaponToType,
            CanBeUsedAs     = new Dictionary<string, IReadOnlySet<string>>(),
            WeaponToCaliber = new Dictionary<string, string>(),
            KnownItemIds    = known,
        };
    }

    [Fact]
    public void KeepAll_NotInDbId_SurvivesAndDoesNotBlockExpansion()
    {
        var cat = MakePistols();
        var condition = new ConditionNode
        {
            Id            = "c",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon1", "weapon2", "not_exists"],
            WeaponCaliber = [],
        };
        var config = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepAll };

        MakeExpander().Expand(condition, null, cat, config, NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(
            ["weapon1", "weapon2", "weapon3", "weapon4", "not_exists"]);
    }

    [Fact]
    public void KeepInDb_NotInDbId_IsRemovedButExpansionProceeds()
    {
        var cat = MakePistols();
        var condition = new ConditionNode
        {
            Id            = "c",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon1", "weapon2", "not_exists"],
            WeaponCaliber = [],
        };
        var config = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };
        var logger = new CapturingModLogger();

        MakeExpander().Expand(condition, null, cat, config, logger);

        condition.Weapon.Should().BeEquivalentTo(["weapon1", "weapon2", "weapon3", "weapon4"]);
        logger.Warnings.Should().Contain(w => w.Contains("not_exists"));
    }

    [Fact]
    public void KeepInDb_UncategorizedInDb_IsPreservedAndDoesNotBlockExpansion()
    {
        // weapon5 exists in DB but has no rule match.
        var cat = MakePistols("weapon5");
        var condition = new ConditionNode
        {
            Id            = "c",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon1", "weapon2", "weapon5"],
            WeaponCaliber = [],
        };
        var config = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepInDb };

        MakeExpander().Expand(condition, null, cat, config, NullModLogger.Instance);

        condition.Weapon.Should().BeEquivalentTo(
            ["weapon1", "weapon2", "weapon3", "weapon4", "weapon5"]);
    }

    [Fact]
    public void Strip_UncategorizedInDb_IsRemovedWithWarning()
    {
        var cat = MakePistols("weapon5");
        var condition = new ConditionNode
        {
            Id            = "c",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon1", "weapon2", "weapon5"],
            WeaponCaliber = [],
        };
        var config = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.Strip };
        var logger = new CapturingModLogger();

        MakeExpander().Expand(condition, null, cat, config, logger);

        condition.Weapon.Should().BeEquivalentTo(["weapon1", "weapon2", "weapon3", "weapon4"]);
        logger.Warnings.Should().Contain(w => w.Contains("weapon5"));
    }

    [Fact]
    public void KeepAll_SingleCategorizedPlusUnknown_UnknownStillKept()
    {
        // Only one categorized ID → below type-expansion threshold. Unknown still preserved.
        var cat = MakePistols();
        var condition = new ConditionNode
        {
            Id            = "c",
            ConditionType = "CounterCreator",
            Weapon        = ["weapon1", "not_exists"],
            WeaponCaliber = [],
        };
        var config = new ModConfig { UnknownWeaponHandling = UnknownWeaponHandling.KeepAll };

        MakeExpander().Expand(condition, null, cat, config, NullModLogger.Instance);

        condition.Weapon.Should().Contain("not_exists");
        condition.Weapon.Should().Contain("weapon1");
    }
}
