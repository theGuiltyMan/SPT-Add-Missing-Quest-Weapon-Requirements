using AddMissingQuestRequirements.Pipeline.Weapon;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Weapon;

public class TypeSelectorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds WeaponToType and WeaponTypes from a flat "weapon → type" mapping.
    /// Each weapon maps to a single type for simplicity; tests that need multiple
    /// types per weapon build the maps manually.
    /// </summary>
    private static (IReadOnlyDictionary<string, IReadOnlySet<string>> weaponToType,
                    IReadOnlyDictionary<string, IReadOnlySet<string>> weaponTypes)
        BuildMaps(params (string weapon, string[] types)[] entries)
    {
        var weaponToType = new Dictionary<string, IReadOnlySet<string>>();
        var weaponTypes  = new Dictionary<string, HashSet<string>>();

        foreach (var (weapon, types) in entries)
        {
            weaponToType[weapon] = types.ToHashSet();
            foreach (var t in types)
            {
                if (!weaponTypes.TryGetValue(t, out var set))
                {
                    set = [];
                    weaponTypes[t] = set;
                }

                set.Add(weapon);
            }
        }

        var readOnlyTypes = weaponTypes.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlySet<string>)kvp.Value);

        return (weaponToType, readOnlyTypes);
    }

    private static readonly TypeSelector Selector = new();

    // ── Basic type coverage ──────────────────────────────────────────────────

    [Fact]
    public void Selects_type_that_covers_all_weapons()
    {
        var (weaponToType, weaponTypes) = BuildMaps(
            ("ak74", ["AssaultRifle"]),
            ("ak47", ["AssaultRifle"]));

        var result = Selector.Select(["ak74", "ak47"], weaponToType, weaponTypes, new Dictionary<string, string>());

        result.BestType.Should().Be("AssaultRifle");
    }

    [Fact]
    public void Returns_null_when_no_type_covers_all_weapons()
    {
        var (weaponToType, weaponTypes) = BuildMaps(
            ("ak74", ["AssaultRifle"]),
            ("sv98", ["BoltActionSniperRifle"]));

        var result = Selector.Select(["ak74", "sv98"], weaponToType, weaponTypes, new Dictionary<string, string>());

        result.BestType.Should().BeNull();
    }

    [Fact]
    public void Empty_weapon_list_returns_all_nulls()
    {
        var (weaponToType, weaponTypes) = BuildMaps(("ak74", ["AssaultRifle"]));

        var result = Selector.Select([], weaponToType, weaponTypes, new Dictionary<string, string>());

        result.BestType.Should().BeNull();
        result.BestCandidate.Should().BeNull();
        result.OutlierId.Should().BeNull();
    }

    // ── kindOf preference ────────────────────────────────────────────────────

    [Fact]
    public void Prefers_child_type_over_kindOf_parent_when_both_cover_all_weapons()
    {
        // rhino and python are both Revolver; because CategorizeWithLessRestrictive=true they're
        // also in Pistol. Both Revolver and Pistol cover the list. KindOf says Revolver→Pistol.
        var (weaponToType, weaponTypes) = BuildMaps(
            ("rhino",  ["Revolver", "Pistol"]),
            ("python", ["Revolver", "Pistol"]));

        var kindOf = new Dictionary<string, string> { ["Revolver"] = "Pistol" };
        var result = Selector.Select(["rhino", "python"], weaponToType, weaponTypes, kindOf);

        result.BestType.Should().Be("Revolver");
    }

    [Fact]
    public void Without_kindOf_selects_covering_type_even_if_parent_also_covers()
    {
        // No kindOf — any covering type is acceptable; just check one is chosen.
        var (weaponToType, weaponTypes) = BuildMaps(
            ("rhino",  ["Revolver", "Pistol"]),
            ("python", ["Revolver", "Pistol"]));

        var result = Selector.Select(["rhino", "python"], weaponToType, weaponTypes, new Dictionary<string, string>());

        result.BestType.Should().NotBeNull();
    }

    [Fact]
    public void Prefers_smaller_type_when_no_kindOf_relation()
    {
        // AKS-74U has 5 members; AK has 22. Quest has 4 weapons all present in both.
        // Without a kindOf mapping, the smaller type (AKS-74U) should win.
        var weaponToType = new Dictionary<string, IReadOnlySet<string>>
        {
            ["aks74u"]      = new HashSet<string> { "AKS-74U", "AK" },
            ["aks74un"]     = new HashSet<string> { "AKS-74U", "AK" },
            ["aks74ub"]     = new HashSet<string> { "AKS-74U", "AK" },
            ["ak545short"]  = new HashSet<string> { "AKS-74U", "AK" },
            ["ak545"]       = new HashSet<string> { "AKS-74U", "AK" },
        };

        // AKS-74U has 5 members, AK has 22 (padding with extra weapons that only belong to AK)
        var ak74uMembers    = new HashSet<string> { "aks74u", "aks74un", "aks74ub", "ak545short", "ak545" };
        var akMembers       = new HashSet<string>(ak74uMembers);
        for (var i = 0; i < 17; i++) { akMembers.Add($"extra_ak_{i}"); }

        var weaponTypes = new Dictionary<string, IReadOnlySet<string>>
        {
            ["AKS-74U"] = ak74uMembers,
            ["AK"]      = akMembers,
        };

        var result = Selector.Select(
            ["aks74u", "aks74un", "aks74ub", "ak545short"],
            weaponToType,
            weaponTypes,
            new Dictionary<string, string>());

        result.BestType.Should().Be("AKS-74U");
    }

    // ── Best-candidate ───────────────────────────────────────────────────────

    [Fact]
    public void Best_candidate_covers_all_but_one_weapon()
    {
        // ak74 and ak47 are AssaultRifles; sv98 is a BoltActionSniperRifle.
        // No type covers all three, but AssaultRifle covers all but sv98.
        var (weaponToType, weaponTypes) = BuildMaps(
            ("ak74", ["AssaultRifle"]),
            ("ak47", ["AssaultRifle"]),
            ("sv98", ["BoltActionSniperRifle"]));

        var result = Selector.Select(["ak74", "ak47", "sv98"], weaponToType, weaponTypes, new Dictionary<string, string>());

        result.BestType.Should().BeNull();
        result.BestCandidate.Should().Be("AssaultRifle");
        result.OutlierId.Should().Be("sv98");
    }

    [Fact]
    public void No_best_candidate_when_all_weapons_different_types()
    {
        var (weaponToType, weaponTypes) = BuildMaps(
            ("ak74", ["AssaultRifle"]),
            ("sv98", ["BoltActionSniperRifle"]),
            ("rhino", ["Revolver"]));

        var result = Selector.Select(["ak74", "sv98", "rhino"], weaponToType, weaponTypes, new Dictionary<string, string>());

        result.BestType.Should().BeNull();
        result.BestCandidate.Should().BeNull();
    }

    [Fact]
    public void Single_weapon_selects_its_type()
    {
        var (weaponToType, weaponTypes) = BuildMaps(("ak74", ["AssaultRifle"]));

        var result = Selector.Select(["ak74"], weaponToType, weaponTypes, new Dictionary<string, string>());

        result.BestType.Should().Be("AssaultRifle");
    }
}
