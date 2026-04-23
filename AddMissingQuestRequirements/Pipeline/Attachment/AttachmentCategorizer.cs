using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Database;
using AddMissingQuestRequirements.Pipeline.Rules;
using AddMissingQuestRequirements.Pipeline.Shared;

namespace AddMissingQuestRequirements.Pipeline.Attachment;

/// <summary>
/// Categorizes all attachment items (those with "Mod" ancestry) in the database.
/// Delegates shared logic to <see cref="CategorizerCore"/>; owns only the
/// attachment-specific pre-filter (HasAncestor "Mod") and type callback.
/// </summary>
public sealed class AttachmentCategorizer(IEnumerable<TypeRule> rules)
{
    private readonly IReadOnlyList<TypeRule> _rules = [..rules];

    public AttachmentCategorizationResult Categorize(
        IItemDatabase db,
        OverriddenSettings settings)
    {
        var engine = new RuleEngine(_rules.Concat(settings.AttachmentTypeRules), db);
        var resolver = engine.Ancestry;

        var modItems = db.Items.Values
            .Where(i => i.NodeType == "Item" && resolver.HasAncestorWithName(i.Id, "Mod", db));

        var input = new CategorizerInput(
            ManualOverrides:   settings.ManualAttachmentTypeOverrides,
            CanBeUsedAsSeeds:  settings.AttachmentCanBeUsedAs,
            GetTypes:          match => [match.Type, ..match.AlsoAs],
            AliasStripWords:   settings.AttachmentAliasNameStripWords,
            AliasExcludeIds:   []);

        var (itemToType, typeToItems, canBeUsedAs) =
            CategorizerCore.Categorize(db, modItems, engine, input);

        return new AttachmentCategorizationResult
        {
            AttachmentTypes  = CategorizationHelper.AsReadOnly(typeToItems),
            AttachmentToType = CategorizationHelper.AsReadOnly(itemToType),
            CanBeUsedAs      = CategorizationHelper.AsReadOnly(canBeUsedAs),
            KnownItemIds     = db.Items.Keys.ToHashSet(),
        };
    }
}
