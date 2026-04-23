using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Override;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Override;

/// <summary>
/// Tests for OverrideReader using real temp directories with JSONC files.
/// </summary>
public class OverrideReaderTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // Creates a temp mod directory with optional MissingQuestWeapons files.
    private string MakeModDir(
        string? questOverridesJsonc = null,
        string? weaponOverridesJsonc = null,
        string? attachmentOverridesJsonc = null)
    {
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modDir);
        _tempDirs.Add(modDir);

        if (questOverridesJsonc is not null
            || weaponOverridesJsonc is not null
            || attachmentOverridesJsonc is not null)
        {
            var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
            Directory.CreateDirectory(mqwDir);

            if (questOverridesJsonc is not null)
            {
                File.WriteAllText(Path.Combine(mqwDir, "QuestOverrides.jsonc"), questOverridesJsonc);
            }

            if (weaponOverridesJsonc is not null)
            {
                File.WriteAllText(Path.Combine(mqwDir, "WeaponOverrides.jsonc"), weaponOverridesJsonc);
            }

            if (attachmentOverridesJsonc is not null)
            {
                File.WriteAllText(
                    Path.Combine(mqwDir, "AttachmentOverrides.jsonc"), attachmentOverridesJsonc);
            }
        }

        return modDir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ── Basic loading ────────────────────────────────────────────────────────

    [Fact]
    public void No_mod_dirs_returns_empty_settings()
    {
        var reader = new OverrideReader(new InMemoryModDirectoryProvider([]));
        var result = reader.Read();

        result.ExcludedQuests.Should().BeEmpty();
        result.QuestOverrides.Should().BeEmpty();
        result.ManualTypeOverrides.Should().BeEmpty();
        result.CanBeUsedAs.Should().BeEmpty();
    }

    [Fact]
    public void Mod_dir_without_MissingQuestWeapons_folder_is_skipped()
    {
        var modDir = MakeModDir(); // no MissingQuestWeapons folder
        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.ExcludedQuests.Should().BeEmpty();
    }

    [Fact]
    public void Loads_excludedQuests_from_single_mod()
    {
        // JSON uses v1 keys — migration chain (v0→v1→v2) renames BlackListedQuests → excludedQuests
        var modDir = MakeModDir(questOverridesJsonc: """
        {
            "BlackListedQuests": ["quest_a", "quest_b"]
        }
        """);
        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.ExcludedQuests.Should().BeEquivalentTo(["quest_a", "quest_b"]);
    }

    [Fact]
    public void Loads_quest_overrides_from_single_mod()
    {
        // v1 JSON: whiteListedWeapons → includedWeapons, onlyUseWhiteListedWeapons:true → expansionMode:whitelistOnly
        var modDir = MakeModDir(questOverridesJsonc: """
        {
            "Overrides": [
                {
                    "id": "quest1",
                    "whiteListedWeapons": ["weapon_a"],
                    "onlyUseWhiteListedWeapons": true
                }
            ]
        }
        """);
        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.QuestOverrides.Should().ContainKey("quest1");
        result.QuestOverrides["quest1"][0].IncludedWeapons.Should().BeEquivalentTo(["weapon_a"]);
        result.QuestOverrides["quest1"][0].ExpansionMode.Should().Be(ExpansionMode.WhitelistOnly);
    }

    [Fact]
    public void Loads_manualTypeOverrides_from_single_mod()
    {
        // v1 JSON: "Override" → "manualTypeOverrides" via migration
        var modDir = MakeModDir(weaponOverridesJsonc: """
        {
            "Override": {
                "weapon_a": "AssaultRifle",
                "weapon_b": "Pistol,Revolver"
            }
        }
        """);
        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.ManualTypeOverrides["weapon_a"].Should().Be("AssaultRifle");
        result.ManualTypeOverrides["weapon_b"].Should().Be("Pistol,Revolver");
    }

    [Fact]
    public void Loads_CanBeUsedAs_from_single_mod()
    {
        var modDir = MakeModDir(weaponOverridesJsonc: """
        {
            "CanBeUsedAs": {
                "weapon_a": ["weapon_b", "weapon_c"]
            }
        }
        """);
        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.CanBeUsedAs["weapon_a"].Should().BeEquivalentTo(["weapon_b", "weapon_c"]);
    }

    // ── Multi-mod merging ────────────────────────────────────────────────────

    [Fact]
    public void Two_mods_IGNORE_skips_second_mods_entry_for_same_quest()
    {
        var mod1 = MakeModDir(questOverridesJsonc: """
        {
            "OverrideBehaviour": "IGNORE",
            "Overrides": [{ "id": "quest1", "whiteListedWeapons": ["weapon_a"] }]
        }
        """);
        var mod2 = MakeModDir(questOverridesJsonc: """
        {
            "OverrideBehaviour": "IGNORE",
            "Overrides": [{ "id": "quest1", "whiteListedWeapons": ["weapon_b"] }]
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var result = reader.Read();

        result.QuestOverrides["quest1"][0].IncludedWeapons.Should().BeEquivalentTo(["weapon_a"]);
    }

    [Fact]
    public void Two_mods_MERGE_combines_whitelist_arrays()
    {
        var mod1 = MakeModDir(questOverridesJsonc: """
        {
            "Overrides": [{ "id": "quest1", "whiteListedWeapons": ["weapon_a"] }]
        }
        """);
        var mod2 = MakeModDir(questOverridesJsonc: """
        {
            "OverrideBehaviour": "MERGE",
            "Overrides": [{ "id": "quest1", "whiteListedWeapons": ["weapon_b"] }]
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var result = reader.Read();

        result.QuestOverrides["quest1"][0].IncludedWeapons
            .Should().BeEquivalentTo(["weapon_a", "weapon_b"]);
    }

    [Fact]
    public void Two_mods_excludedQuests_MERGE_unions_lists()
    {
        var mod1 = MakeModDir(questOverridesJsonc: """{"BlackListedQuests":["quest_a"]}""");
        var mod2 = MakeModDir(questOverridesJsonc: """
        {
            "OverrideBehaviour": "MERGE",
            "BlackListedQuests": ["quest_b"]
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var result = reader.Read();

        result.ExcludedQuests.Should().BeEquivalentTo(["quest_a", "quest_b"]);
    }

    [Fact]
    public void CanBeUsedAs_DELETE_entry_removes_alias()
    {
        var mod1 = MakeModDir(weaponOverridesJsonc: """
        {"CanBeUsedAs":{"weapon_a":["weapon_b","weapon_c"]}}
        """);
        var mod2 = MakeModDir(weaponOverridesJsonc: """
        {
            "OverrideBehaviour": "MERGE",
            "CanBeUsedAs": {
                "weapon_a": [{"value":"weapon_b","behaviour":"DELETE"}]
            }
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var result = reader.Read();

        result.CanBeUsedAs["weapon_a"].Should().BeEquivalentTo(["weapon_c"]);
    }

    // ── AttachmentOverrides ──────────────────────────────────────────────────────

    [Fact]
    public void AttachmentOverrides_missing_file_returns_empty_fields()
    {
        var modDir  = MakeModDir(); // no AttachmentOverrides.jsonc
        var reader  = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result  = reader.Read();

        result.ManualAttachmentTypeOverrides.Should().BeEmpty();
        result.AttachmentCanBeUsedAs.Should().BeEmpty();
        result.AttachmentAliasNameStripWords.Should().BeEmpty();
    }

    [Fact]
    public void AttachmentOverrides_canBeUsedAs_loaded_from_single_mod()
    {
        var modDir = MakeModDir(attachmentOverridesJsonc: """
            { "version": 1, "canBeUsedAs": { "scope_a": ["scope_b"] } }
            """);

        var result = new OverrideReader(new InMemoryModDirectoryProvider([modDir])).Read();

        result.AttachmentCanBeUsedAs.Should().ContainKey("scope_a")
            .WhoseValue.Should().Contain("scope_b");
    }

    [Fact]
    public void AttachmentOverrides_manualTypeOverrides_loaded()
    {
        var modDir = MakeModDir(attachmentOverridesJsonc: """
            { "version": 1, "manualAttachmentTypeOverrides": { "scope_x": "Scope,TacticalScope" } }
            """);

        var result = new OverrideReader(new InMemoryModDirectoryProvider([modDir])).Read();

        result.ManualAttachmentTypeOverrides.Should().ContainKey("scope_x")
            .WhoseValue.Should().Be("Scope,TacticalScope");
    }

    [Fact]
    public void AttachmentOverrides_two_mods_MERGE_unions_canBeUsedAs()
    {
        var modA = MakeModDir(attachmentOverridesJsonc: """
            { "version": 1, "overrideBehaviour": "MERGE", "canBeUsedAs": { "scope_a": ["scope_b"] } }
            """);
        var modB = MakeModDir(attachmentOverridesJsonc: """
            { "version": 1, "overrideBehaviour": "MERGE", "canBeUsedAs": { "scope_a": ["scope_c"] } }
            """);

        var result = new OverrideReader(new InMemoryModDirectoryProvider([modA, modB])).Read();

        result.AttachmentCanBeUsedAs["scope_a"].Should().Contain("scope_b").And.Contain("scope_c");
    }

    [Fact]
    public void AttachmentOverrides_aliasNameStripWords_loaded()
    {
        var modDir = MakeModDir(attachmentOverridesJsonc: """
            { "version": 1, "aliasNameStripWords": ["FDE", "Black"] }
            """);

        var result = new OverrideReader(new InMemoryModDirectoryProvider([modDir])).Read();

        result.AttachmentAliasNameStripWords.Should().Equal("FDE", "Black");
    }

    // ── AliasNameExcludeWeapons ──────────────────────────────────────────────────

    [Fact]
    public void Loads_AliasNameExcludeWeapons_from_single_mod()
    {
        // v1 JSON: CanBeUsedAsShortNameBlacklist → aliasNameExcludeWeapons via migration
        var modDir = MakeModDir(weaponOverridesJsonc: """
        {
            "CanBeUsedAsShortNameBlacklist": ["FDE", "Gold"]
        }
        """);
        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.AliasNameExcludeWeapons.Should().BeEquivalentTo(["FDE", "Gold"]);
    }

    [Fact]
    public void Two_mods_AliasNameExcludeWeapons_MERGE_unions_lists()
    {
        var mod1 = MakeModDir(weaponOverridesJsonc: """
        {
            "aliasNameExcludeWeapons": ["FDE"]
        }
        """);
        var mod2 = MakeModDir(weaponOverridesJsonc: """
        {
            "OverrideBehaviour": "MERGE",
            "aliasNameExcludeWeapons": ["Gold"]
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var result = reader.Read();

        result.AliasNameExcludeWeapons.Should().BeEquivalentTo(["FDE", "Gold"]);
    }

    // ── TypeRules from WeaponOverridesFile.customTypeRules ───────────────────────

    [Fact]
    public void Single_mod_customTypeRules_populates_TypeRules()
    {
        var modDir = MakeModDir(weaponOverridesJsonc: """
            {
                "version": 2,
                "customTypeRules": [
                    { "type": "AKM", "conditions": { "nameMatches": "AKM" } }
                ]
            }
            """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var settings = reader.Read();

        settings.TypeRules.Should().HaveCount(1);
        settings.TypeRules[0].Type.Should().Be("AKM");
    }

    [Fact]
    public void Two_mods_file_MERGE_appends_rules_from_both()
    {
        var mod1 = MakeModDir(weaponOverridesJsonc: """
            {
                "version": 2,
                "overrideBehaviour": "MERGE",
                "customTypeRules": [{ "type": "AKM", "conditions": { "nameMatches": "AKM" } }]
            }
            """);
        var mod2 = MakeModDir(weaponOverridesJsonc: """
            {
                "version": 2,
                "overrideBehaviour": "MERGE",
                "customTypeRules": [{ "type": "AK74", "conditions": { "nameMatches": "AK74" } }]
            }
            """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var settings = reader.Read();

        settings.TypeRules.Should().HaveCount(2);
        settings.TypeRules.Select(r => r.Type).Should().Contain(["AKM", "AK74"]);
    }

    [Fact]
    public void Two_mods_file_IGNORE_default_uses_first_mods_rules_only()
    {
        // Default OverrideBehaviour is IGNORE — if first mod sets rules, second mod is skipped
        var mod1 = MakeModDir(weaponOverridesJsonc: """
            {
                "version": 2,
                "customTypeRules": [{ "type": "AKM", "conditions": { "nameMatches": "AKM" } }]
            }
            """);
        var mod2 = MakeModDir(weaponOverridesJsonc: """
            {
                "version": 2,
                "customTypeRules": [{ "type": "AK74", "conditions": { "nameMatches": "AK74" } }]
            }
            """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var settings = reader.Read();

        settings.TypeRules.Should().HaveCount(1);
        settings.TypeRules[0].Type.Should().Be("AKM");
    }

    [Fact]
    public void Two_mods_file_REPLACE_second_mod_replaces_first()
    {
        var mod1 = MakeModDir(weaponOverridesJsonc: """
            {
                "version": 2,
                "customTypeRules": [{ "type": "AKM", "conditions": { "nameMatches": "AKM" } }]
            }
            """);
        var mod2 = MakeModDir(weaponOverridesJsonc: """
            {
                "version": 2,
                "overrideBehaviour": "REPLACE",
                "customTypeRules": [{ "type": "AK74", "conditions": { "nameMatches": "AK74" } }]
            }
            """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var settings = reader.Read();

        settings.TypeRules.Should().HaveCount(1);
        settings.TypeRules[0].Type.Should().Be("AK74");
    }

    // ── TypeRules from AttachmentOverridesFile.customTypeRules ────────────────

    [Fact]
    public void Single_mod_attachment_customTypeRules_populates_AttachmentTypeRules()
    {
        var modDir = MakeModDir(attachmentOverridesJsonc: """
            {
                "version": 1,
                "customTypeRules": [
                    { "type": "CustomSilencer", "conditions": { "hasAncestor": "Silencer" } }
                ]
            }
            """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var settings = reader.Read();

        settings.AttachmentTypeRules.Should().HaveCount(1);
        settings.AttachmentTypeRules[0].Type.Should().Be("CustomSilencer");
        settings.TypeRules.Should().BeEmpty("weapon rule list must not be polluted by attachment rules");
    }

    [Fact]
    public void Two_mods_attachment_MERGE_appends_rules_from_both()
    {
        var mod1 = MakeModDir(attachmentOverridesJsonc: """
            {
                "version": 1,
                "overrideBehaviour": "MERGE",
                "customTypeRules": [{ "type": "CustomSilencer", "conditions": { "hasAncestor": "Silencer" } }]
            }
            """);
        var mod2 = MakeModDir(attachmentOverridesJsonc: """
            {
                "version": 1,
                "overrideBehaviour": "MERGE",
                "customTypeRules": [{ "type": "CustomScope", "conditions": { "hasAncestor": "OpticScope" } }]
            }
            """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var settings = reader.Read();

        settings.AttachmentTypeRules.Should().HaveCount(2);
        settings.AttachmentTypeRules.Select(r => r.Type).Should().Contain(["CustomSilencer", "CustomScope"]);
    }

    [Fact]
    public void Two_mods_attachment_REPLACE_second_mod_replaces_first()
    {
        var mod1 = MakeModDir(attachmentOverridesJsonc: """
            {
                "version": 1,
                "customTypeRules": [{ "type": "CustomSilencer", "conditions": { "hasAncestor": "Silencer" } }]
            }
            """);
        var mod2 = MakeModDir(attachmentOverridesJsonc: """
            {
                "version": 1,
                "overrideBehaviour": "REPLACE",
                "customTypeRules": [{ "type": "CustomScope", "conditions": { "hasAncestor": "OpticScope" } }]
            }
            """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1, mod2]));
        var settings = reader.Read();

        settings.AttachmentTypeRules.Should().HaveCount(1);
        settings.AttachmentTypeRules[0].Type.Should().Be("CustomScope");
    }
}
