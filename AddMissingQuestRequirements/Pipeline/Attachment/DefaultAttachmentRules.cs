using System.Text.Json;
using AddMissingQuestRequirements.Models;

namespace AddMissingQuestRequirements.Pipeline.Attachment;

/// <summary>
/// Built-in catch-all rules for attachment categorization. One rule per Mod-subtree
/// ancestor node: <c>hasAncestor: X</c> → type <c>{directChildOf:X}</c>. The rule engine
/// fires every matching rule, so each attachment gets typed at every tree depth where it
/// has an ancestor in this list. <see cref="Pipeline.Weapon.TypeSelector"/> later picks the
/// smallest covering type at expansion time — ties break alphabetically.
/// </summary>
public static class DefaultAttachmentRules
{
    /// <summary>
    /// Ancestor nodes whose direct children form useful attachment categories.
    /// Ordered from shallowest to deepest — the order has no runtime effect since the
    /// engine evaluates all rules, but it mirrors the Mod subtree layout for readability.
    /// </summary>
    public static readonly string[] Ancestors =
    [
        "Mod",              // FunctionalMod / GearMod / MasterMod
        "FunctionalMod",    // Muzzle / Sights / Foregrip / Bipod / Flashlight / LightLaser / TacticalCombo / ...
        "GearMod",          // Magazine / Stock / Mount / Shaft / Charge / Launcher
        "MasterMod",        // Barrel / Handguard / Receiver / PistolGrip
        "Muzzle",           // Silencer / Compensator / FlashHider / MuzzleCombo / Pms
        "Sights",           // IronSight / Collimator / CompactCollimator / AssaultScope / OpticScope / SpecialScope
        "SpecialScope",     // NightVision / ThermalVision
        "Magazine",         // CylinderMagazine
        "Stock",            // stock-only groups would otherwise collapse to the coarse GearMod type
        "Mount",            // same rationale — avoid GearMod as a fallback
        "Handguard",        // MasterMod children all need their own leaf type for the same reason
        "PistolGrip",
        "Barrel",
        "Receiver",
    ];

    public static readonly TypeRule[] Rules =
    [
        ..Ancestors.Select(ancestor => new TypeRule
        {
            Conditions = new Dictionary<string, JsonElement>
            {
                ["hasAncestor"] = JsonSerializer.SerializeToElement(ancestor)
            },
            Type = $"{{directChildOf:{ancestor}}}"
        }),
        ..MuzzleFunctionalRules(),
    ];

    // Functional muzzle-device types keyed off _props.muzzleModType. BSG's structural
    // parents under Muzzle conflate function (the FlashHider node holds brakes and
    // compensators; the Silencer node holds both "silencer" and "pms" suppressor
    // variants). These rules add a second, function-scoped type so TypeSelector can
    // pick the semantically-correct smallest type for singleton expansion.
    //
    // Gap: `muzzleModType: "conpensator"` with large negative Loudness (e.g. Noveske
    // KX3, -20) reads as a suppressor functionally but keeps the structural type.
    // Handle per-item via ManualAttachmentTypeOverrides until the rule engine grows
    // a numeric-comparison condition.
    private static IEnumerable<TypeRule> MuzzleFunctionalRules()
    {
        (string Value, string Type)[] mappings =
        [
            ("silencer",    "Suppressor"),    // Western-pattern sound suppressors
            ("pms",         "Suppressor"),    // Warsaw-pact-pattern sound suppressors
            ("brake",       "MuzzleBrake"),
            ("conpensator", "Compensator"),   // BSG typo in source data, preserved
            ("muzzleCombo", "MuzzleAdapter"),
        ];

        foreach (var (value, type) in mappings)
        {
            yield return new TypeRule
            {
                Conditions = new Dictionary<string, JsonElement>
                {
                    ["hasAncestor"] = JsonSerializer.SerializeToElement("Muzzle"),
                    ["properties"]  = JsonSerializer.SerializeToElement(new Dictionary<string, string>
                    {
                        ["muzzleModType"] = value,
                    }),
                },
                Type = type,
            };
        }
    }
}
