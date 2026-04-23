using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Attachment;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Pipeline.Shared;
using AddMissingQuestRequirements.Pipeline.Weapon;
using AddMissingQuestRequirements.Reporting;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Inspector;

public static class PipelineRunner
{
    // Catch-all rules: one per configured weapon-like ancestor. For "Weapon" we assign each
    // item to the node directly under Weapon (giving Pistol / AssaultRifle / Shotgun / etc.)
    // because the SPT weapon tree has an intermediate category layer. For other ancestors
    // (Knife, ThrowWeap, …) the tree is flat — the item sits directly under the ancestor
    // node — so {directChildOf:X} would resolve to the item's own _name and produce a unique
    // type per item. Those ancestors instead get the ancestor name as a literal shared type.
    private static TypeRule[] BuildDefaultWeaponRules(IReadOnlyList<string> weaponLikeAncestors)
    {
        return [..weaponLikeAncestors.Select(ancestor => new TypeRule
        {
            Conditions = new Dictionary<string, JsonElement>
            {
                ["hasAncestor"] = JsonSerializer.SerializeToElement(ancestor),
            },
            Type = ancestor == "Weapon" ? "{directChildOf:Weapon}" : ancestor,
        })];
    }

    // Attachment categorization rules live in the core project — see
    // AddMissingQuestRequirements.Pipeline.Attachment.DefaultAttachmentRules.

    public static InspectorResult Run(LoadResult loaded, IModLogger logger)
    {
        var settings = loaded.Settings;
        var config = loaded.Config;
        var itemDb = loaded.ItemDb;

        // ── Categorize weapons ────────────────────────────────────────────────
        var weaponCategorizer = new WeaponCategorizer(BuildDefaultWeaponRules(config.WeaponLikeAncestors));
        var categorization = weaponCategorizer.Categorize(itemDb, settings, config);

        // ── Categorize attachments ────────────────────────────────────────────
        var attachmentCategorizer = new AttachmentCategorizer(DefaultAttachmentRules.Rules);
        var attachmentCategorization = attachmentCategorizer.Categorize(itemDb, settings);

        // ── Snapshot weapon arrays before patching ────────────────────────────
        var prePatch = new Dictionary<ConditionNode, List<string>>(ReferenceEqualityComparer.Instance);
        foreach (var quest in loaded.QuestDb.Quests.Values)
        {
            foreach (var condition in quest.Conditions)
            {
                prePatch[condition] = [..condition.Weapon];
            }
        }

        var prePatchMods = new Dictionary<ConditionNode, (List<List<string>> Incl, List<List<string>> Excl)>(
            ReferenceEqualityComparer.Instance);
        foreach (var quest in loaded.QuestDb.Quests.Values)
        {
            foreach (var condition in quest.Conditions)
            {
                prePatchMods[condition] = (
                    condition.WeaponModsInclusive.Select(g => g.ToList()).ToList(),
                    condition.WeaponModsExclusive.Select(g => g.ToList()).ToList()
                );
            }
        }

        // ── Patch quests ──────────────────────────────────────────────────────
        var nameResolver = new ItemDbNameResolver(itemDb);
        var weaponExpander = new WeaponArrayExpander(new TypeSelector(), nameResolver);
        var modsExpander = new WeaponModsExpander(attachmentCategorization, nameResolver);
        var patcher = new QuestPatcher([weaponExpander, modsExpander], nameResolver);
        patcher.Patch(loaded.QuestDb, settings, categorization, logger);

        // ── Build result ──────────────────────────────────────────────────────
        return ReportBuilder.Build(
            settings,
            config,
            itemDb,
            loaded.QuestDb,
            prePatch,
            prePatchMods,
            categorization,
            attachmentCategorization);
    }
}
