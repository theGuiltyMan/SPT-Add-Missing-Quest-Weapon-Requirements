using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Rules;
using AddMissingQuestRequirements.Pipeline.Shared;

namespace AddMissingQuestRequirements.Pipeline.Weapon;

/// <summary>
/// Categorizes weapon items via a rule chain + manual overrides + alias matching.
/// Delegates shared logic to <see cref="CategorizerCore"/>; keeps only the
/// weapon-specific pre-filter (WeaponLikeAncestors), parent-type walk inside the
/// getTypes callback, and caliber extraction post-step.
/// </summary>
public sealed class WeaponCategorizer(IEnumerable<TypeRule> rules)
{
    private readonly IReadOnlyList<TypeRule> _rules = [..rules];

    public CategorizationResult Categorize(IItemDatabase db, OverriddenSettings settings, ModConfig config)
    {
        var engine = new RuleEngine(_rules.Concat(settings.TypeRules), db);

        // Weapon-specific pre-filter: any leaf item whose ancestry passes through
        // one of the configured weapon-like ancestor nodes.
        var weaponLikeAncestors = config.WeaponLikeAncestors;
        var leafWeapons = db.Items.Values.Where(i =>
            i.NodeType == "Item" &&
            weaponLikeAncestors.Any(a => engine.Ancestry.HasAncestorWithName(i.Id, a, db)));

        // Shared parent-chain walk for both rule-derived and manual-override types.
        IEnumerable<string> WalkParents(string start)
        {
            var current = start;
            while (config.ParentTypes.TryGetValue(current, out var parentType))
            {
                yield return parentType;
                current = parentType;
            }
        }

        IEnumerable<string> GetTypes(RuleMatch match)
        {
            yield return match.Type;
            foreach (var also in match.AlsoAs)
            {
                yield return also;
            }

            if (config.IncludeParentCategories)
            {
                foreach (var parent in WalkParents(match.Type))
                {
                    yield return parent;
                }
            }
        }

        IEnumerable<string> ExpandManualType(string t)
        {
            yield return t;
            if (config.IncludeParentCategories)
            {
                foreach (var parent in WalkParents(t))
                {
                    yield return parent;
                }
            }
        }

        var input = new CategorizerInput(
            ManualOverrides:   settings.ManualTypeOverrides,
            CanBeUsedAsSeeds:  settings.CanBeUsedAs,
            GetTypes:          GetTypes,
            AliasStripWords:   settings.AliasNameStripWords,
            AliasExcludeIds:   settings.AliasNameExcludeWeapons,
            ExpandManualType:  ExpandManualType);

        var (weaponToType, typeToWeapons, canBeUsedAs) =
            CategorizerCore.Categorize(db, leafWeapons, engine, input);

        // Weapon-specific post-step: caliber extraction from _props.ammoCaliber
        var weaponToCaliber = new Dictionary<string, string>();
        foreach (var id in weaponToType.Keys)
        {
            var item = db.Items[id];
            if (item.Props.TryGetValue("ammoCaliber", out var caliberElem) &&
                caliberElem.ValueKind == JsonValueKind.String)
            {
                var caliber = caliberElem.GetString();
                if (!string.IsNullOrEmpty(caliber))
                {
                    weaponToCaliber[id] = caliber;
                }
            }
        }

        return new CategorizationResult
        {
            WeaponTypes     = CategorizationHelper.AsReadOnly(typeToWeapons),
            WeaponToType    = CategorizationHelper.AsReadOnly(weaponToType),
            CanBeUsedAs     = CategorizationHelper.AsReadOnly(canBeUsedAs),
            WeaponToCaliber = weaponToCaliber,
            KnownItemIds    = db.Items.Keys.ToHashSet(),
        };
    }
}
