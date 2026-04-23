using System.Text.Json;
using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Attachment;
using AddMissingQuestRequirements.Pipeline.Database;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Attachment;

public class AttachmentCategorizerTests
{
    // ── Test database ────────────────────────────────────────────────────────
    // root → Item → Mod → Stock → stock_a (Item, locale: "Fab Defense UAS stock")
    //                          → stock_b (Item, locale: "Fab Defense UAS stock")  ← same name → aliased
    //                   → Scope → scope_a (Item, locale: "Valday 1-4x scope")
    //                           → scope_b (Item, locale: "Valday PS-320 scope")
    //             → Weapon → AssaultRifle → ak74 (Item)  ← must NOT appear in attachment results
    private static InMemoryItemDatabase MakeDb() => new(
    [
        new ItemNode { Id = "root",    Name = "Item",         ParentId = null,    NodeType = "Node" },
        new ItemNode { Id = "item",    Name = "Item",         ParentId = "root",  NodeType = "Node" },
        new ItemNode { Id = "mod",     Name = "Mod",          ParentId = "item",  NodeType = "Node" },
        new ItemNode { Id = "stock",   Name = "Stock",        ParentId = "mod",   NodeType = "Node" },
        new ItemNode { Id = "scope",   Name = "Scope",        ParentId = "mod",   NodeType = "Node" },
        new ItemNode { Id = "stock_a", Name = "Fab Stock",    ParentId = "stock", NodeType = "Item" },
        new ItemNode { Id = "stock_b", Name = "Fab Stock",    ParentId = "stock", NodeType = "Item" },
        new ItemNode { Id = "scope_a", Name = "Valday 1-4x",  ParentId = "scope", NodeType = "Item" },
        new ItemNode { Id = "scope_b", Name = "Valday PS-320",ParentId = "scope", NodeType = "Item" },
        // weapon items — must be excluded by the Mod-ancestry filter
        new ItemNode { Id = "weapon",  Name = "Weapon",       ParentId = "item",  NodeType = "Node" },
        new ItemNode { Id = "ar",      Name = "AssaultRifle", ParentId = "weapon",NodeType = "Node" },
        new ItemNode { Id = "ak74",    Name = "ak74",         ParentId = "ar",    NodeType = "Item" },
    ],
    localeNames: new()
    {
        ["stock_a"] = "Fab Defense UAS stock",
        ["stock_b"] = "Fab Defense UAS stock",   // same normalized name → alias group
        ["scope_a"] = "Valday 1-4x scope",
        ["scope_b"] = "Valday PS-320 scope",
    });

    // Catch-all rule: any Mod-descendant → {directChildOf:Mod}
    private static readonly TypeRule[] DefaultRules =
    [
        new TypeRule
        {
            Conditions = new() { ["hasAncestor"] = JsonDocument.Parse("\"Mod\"").RootElement },
            Type = "{directChildOf:Mod}",
        }
    ];

    private static OverriddenSettings EmptySettings() => new();

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Rule_engine_assigns_correct_type_via_directChildOf()
    {
        var cat = new AttachmentCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings());

        cat.AttachmentTypes.Should().ContainKey("Stock")
            .WhoseValue.Should().Contain("stock_a").And.Contain("stock_b");
        cat.AttachmentTypes.Should().ContainKey("Scope")
            .WhoseValue.Should().Contain("scope_a").And.Contain("scope_b");
        cat.AttachmentToType["stock_a"].Should().Contain("Stock");
        cat.AttachmentToType["scope_a"].Should().Contain("Scope");
    }

    [Fact]
    public void Weapon_items_are_excluded_from_result()
    {
        var cat = new AttachmentCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings());

        cat.AttachmentToType.Should().NotContainKey("ak74");
        cat.AttachmentToType.Should().NotContainKey("weapon");
        cat.AttachmentToType.Should().NotContainKey("ar");
    }

    [Fact]
    public void ManualAttachmentTypeOverride_bypasses_rule_engine()
    {
        var settings = new OverriddenSettings
        {
            ManualAttachmentTypeOverrides = new()
            {
                ["scope_a"] = "TacticalScope,Scope"
            }
        };

        var cat = new AttachmentCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings);

        cat.AttachmentToType["scope_a"].Should().Contain("TacticalScope").And.Contain("Scope");
        cat.AttachmentTypes.Should().ContainKey("TacticalScope")
            .WhoseValue.Should().Contain("scope_a");
    }

    [Fact]
    public void Short_name_alias_matching_cross_links_items_with_same_normalized_name()
    {
        // stock_a and stock_b both have locale name "Fab Defense UAS stock" → aliased
        var cat = new AttachmentCategorizer(DefaultRules)
            .Categorize(MakeDb(), EmptySettings());

        cat.CanBeUsedAs.Should().ContainKey("stock_a")
            .WhoseValue.Should().Contain("stock_b");
        cat.CanBeUsedAs.Should().ContainKey("stock_b")
            .WhoseValue.Should().Contain("stock_a");
    }

    [Fact]
    public void AliasNameStripWords_strips_words_before_name_comparison()
    {
        // scope_a = "Valday 1-4x scope", scope_b = "Valday PS-320 scope"
        // Stripping "1-4x" and "PS-320" → both become "Valday scope" → aliased
        var settings = new OverriddenSettings
        {
            AttachmentAliasNameStripWords = ["1-4x", "PS-320"]
        };

        var cat = new AttachmentCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings);

        cat.CanBeUsedAs.Should().ContainKey("scope_a")
            .WhoseValue.Should().Contain("scope_b");
    }

    [Fact]
    public void Manual_AttachmentCanBeUsedAs_seeds_the_alias_graph()
    {
        var settings = new OverriddenSettings
        {
            AttachmentCanBeUsedAs = new() { ["scope_a"] = ["scope_b"] }
        };

        var cat = new AttachmentCategorizer(DefaultRules)
            .Categorize(MakeDb(), settings);

        cat.CanBeUsedAs.Should().ContainKey("scope_a")
            .WhoseValue.Should().Contain("scope_b");
        // Reverse link: scope_b → scope_a (scope_b is a known categorized attachment)
        cat.CanBeUsedAs.Should().ContainKey("scope_b")
            .WhoseValue.Should().Contain("scope_a");
    }

    [Fact]
    public void Transitive_expansion_cross_links_all_members_of_connected_component()
    {
        // Three scopes: seed A→B and C→B; after transitive expansion A↔C must be linked
        var db = new InMemoryItemDatabase(
        [
            new ItemNode { Id = "root",  Name = "Item",  ParentId = null,   NodeType = "Node" },
            new ItemNode { Id = "item",  Name = "Item",  ParentId = "root", NodeType = "Node" },
            new ItemNode { Id = "mod",   Name = "Mod",   ParentId = "item", NodeType = "Node" },
            new ItemNode { Id = "scope", Name = "Scope", ParentId = "mod",  NodeType = "Node" },
            new ItemNode { Id = "sc_a",  Name = "sc_a",  ParentId = "scope",NodeType = "Item" },
            new ItemNode { Id = "sc_b",  Name = "sc_b",  ParentId = "scope",NodeType = "Item" },
            new ItemNode { Id = "sc_c",  Name = "sc_c",  ParentId = "scope",NodeType = "Item" },
        ]);

        var settings = new OverriddenSettings
        {
            AttachmentCanBeUsedAs = new()
            {
                ["sc_a"] = ["sc_b"],
                ["sc_c"] = ["sc_b"],
            }
        };

        var cat = new AttachmentCategorizer(DefaultRules).Categorize(db, settings);

        // After transitive expansion, A and C must be cross-linked via B
        cat.CanBeUsedAs["sc_a"].Should().Contain("sc_c");
        cat.CanBeUsedAs["sc_c"].Should().Contain("sc_a");
    }

    [Fact]
    public void User_authored_AttachmentTypeRules_tag_matched_attachments()
    {
        var db = MakeDb();
        var settings = new OverriddenSettings
        {
            AttachmentTypeRules =
            [
                new TypeRule
                {
                    Conditions = new Dictionary<string, JsonElement>
                    {
                        ["hasAncestor"] = JsonDocument.Parse("\"Scope\"").RootElement
                    },
                    Type = "UserScope"
                }
            ]
        };

        var cat = new AttachmentCategorizer(DefaultRules)
            .Categorize(db, settings);

        var scopeId = cat.AttachmentToType
            .FirstOrDefault(kvp => kvp.Value.Contains("UserScope")).Key;

        scopeId.Should().NotBeNull("user rule must tag at least one scope in the fixture");
        cat.AttachmentToType[scopeId].Should().Contain("UserScope");
    }
}
