using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Override;
using AddMissingQuestRequirements.Pipeline.Quest;
using AddMissingQuestRequirements.Util;
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

    private static QuestOverrideEntry? Resolve(OverriddenSettings settings, string questId, string conditionId) =>
        QuestOverrideResolver.Resolve(settings, questId, new ConditionNode { Id = conditionId });

    private static QuestOverrideEntry? ResolveAnyCondition(OverriddenSettings settings, string questId) =>
        Resolve(settings, questId, "any-condition");

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

        ResolveAnyCondition(result, "quest1")!.IncludedWeapons.Should().BeEquivalentTo(["weapon_a"]);
    }

    [Fact]
    public void IGNORE_keeps_per_condition_entries_from_one_file()
    {
        var mod1 = MakeModDir(questOverridesJsonc: """
        {
            "OverrideBehaviour": "IGNORE",
            "Overrides": [
                { "id": "quest1", "conditions": ["c1"], "whiteListedWeapons": ["weapon_a"] },
                { "id": "quest1", "conditions": ["c2"], "whiteListedWeapons": ["weapon_b"] }
            ]
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([mod1]));
        var result = reader.Read();

        Resolve(result, "quest1", "c1")!.IncludedWeapons.Should().BeEquivalentTo(["weapon_a"]);
        Resolve(result, "quest1", "c2")!.IncludedWeapons.Should().BeEquivalentTo(["weapon_b"]);
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

        ResolveAnyCondition(result, "quest1")!.IncludedWeapons
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
    public void Two_mods_file_IGNORE_default_adds_second_mods_rules_for_new_types()
    {
        // Default OverrideBehaviour is IGNORE: an earlier mod's rules don't block new types
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

        settings.TypeRules.Select(r => r.Type).Should().Equal("AKM", "AK74");
    }

    [Fact]
    public void IGNORE_file_excludedQuests_survive_an_earlier_mods_non_empty_list()
    {
        var earlier = MakeModDir(questOverridesJsonc: """
        {
            "OverrideBehaviour": "MERGE",
            "excludedQuests": ["quest_other"]
        }
        """);
        var baseline = MakeModDir(questOverridesJsonc: """
        {
            "OverrideBehaviour": "IGNORE",
            "excludedQuests": ["quest1", "quest2", "quest_other"]
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([earlier, baseline]));
        var result = reader.Read();

        result.ExcludedQuests.Should().BeEquivalentTo(["quest_other", "quest1", "quest2"]);
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

    // ── Legacy filename fallback ─────────────────────────────────────────────

    [Fact]
    public void Legacy_OverriddenWeapons_filename_is_loaded_when_new_name_absent()
    {
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modDir);
        _tempDirs.Add(modDir);

        var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
        Directory.CreateDirectory(mqwDir);

        File.WriteAllText(Path.Combine(mqwDir, "OverriddenWeapons.jsonc"), """
        {
            "OverrideBehaviour": "MERGE",
            "Override": {
                "weapon_legacy_id": "BoltActionSniperRifle"
            }
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.ManualTypeOverrides.Should().ContainKey("weapon_legacy_id");
        result.ManualTypeOverrides["weapon_legacy_id"].Should().Be("BoltActionSniperRifle");
    }

    [Fact]
    public void New_WeaponOverrides_filename_wins_over_legacy_when_both_present()
    {
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modDir);
        _tempDirs.Add(modDir);

        var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
        Directory.CreateDirectory(mqwDir);

        File.WriteAllText(Path.Combine(mqwDir, "WeaponOverrides.jsonc"), """
        {
            "manualTypeOverrides": { "weapon_new_id": "AssaultRifle" }
        }
        """);
        File.WriteAllText(Path.Combine(mqwDir, "OverriddenWeapons.jsonc"), """
        {
            "Override": { "weapon_legacy_id": "BoltActionSniperRifle" }
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var result = reader.Read();

        result.ManualTypeOverrides.Should().ContainKey("weapon_new_id");
        result.ManualTypeOverrides.Should().NotContainKey("weapon_legacy_id");
    }

    // ── Disk rewrite after migration ─────────────────────────────────────────

    [Fact]
    public void Legacy_OverriddenWeapons_is_renamed_to_v0_bak_and_canonical_is_written()
    {
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modDir);
        _tempDirs.Add(modDir);

        var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
        Directory.CreateDirectory(mqwDir);

        var legacyPath = Path.Combine(mqwDir, "OverriddenWeapons.jsonc");
        var canonicalPath = Path.Combine(mqwDir, "WeaponOverrides.jsonc");
        File.WriteAllText(legacyPath, """
        {
            "Override": { "weapon_legacy_id": "BoltActionSniperRifle" }
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        var settings = reader.Read();

        File.Exists(legacyPath).Should().BeFalse("legacy file should be renamed to .v0.bak");
        File.Exists(legacyPath + ".v0.bak").Should().BeTrue();
        File.Exists(canonicalPath).Should().BeTrue();

        var rewritten = File.ReadAllText(canonicalPath);
        rewritten.Should().Contain("\"version\": 2");
        rewritten.Should().Contain("manualTypeOverrides");

        settings.ManualTypeOverrides.Should().ContainKey("weapon_legacy_id");
    }

    [Fact]
    public void Already_current_WeaponOverrides_is_not_rewritten_and_no_backup_appears()
    {
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modDir);
        _tempDirs.Add(modDir);

        var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
        Directory.CreateDirectory(mqwDir);

        var canonicalPath = Path.Combine(mqwDir, "WeaponOverrides.jsonc");
        var content = """
        {
            "version": 2,
            "manualTypeOverrides": { "id1": "AssaultRifle" }
        }
        """;
        File.WriteAllText(canonicalPath, content);
        var beforeBytes = File.ReadAllBytes(canonicalPath);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        reader.Read();

        File.ReadAllBytes(canonicalPath).Should().Equal(beforeBytes);
        Directory.GetFiles(mqwDir, "*.bak").Should().BeEmpty();
    }

    [Fact]
    public void Quest_v1_file_is_backed_up_as_v1_bak_and_overwritten_with_v2()
    {
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modDir);
        _tempDirs.Add(modDir);

        var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
        Directory.CreateDirectory(mqwDir);

        var path = Path.Combine(mqwDir, "QuestOverrides.jsonc");
        File.WriteAllText(path, """
        {
            "version": 1,
            "BlackListedQuests": ["q_a"],
            "Overrides": [
                { "id": "q1", "whiteListedWeapons": ["w1"], "onlyUseWhiteListedWeapons": true }
            ]
        }
        """);

        var reader = new OverrideReader(new InMemoryModDirectoryProvider([modDir]));
        reader.Read();

        File.Exists(path + ".v1.bak").Should().BeTrue();
        var rewritten = File.ReadAllText(path);
        rewritten.Should().Contain("\"version\": 3");
        rewritten.Should().Contain("excludedQuests");
        rewritten.Should().Contain("includedWeapons");
    }

    [Fact]
    public void PersistMigrations_false_migrates_in_memory_and_leaves_the_folder_untouched()
    {
        var modDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempDirs.Add(modDir);
        var mqwDir = Path.Combine(modDir, "MissingQuestWeapons");
        Directory.CreateDirectory(mqwDir);

        var legacyPath = Path.Combine(mqwDir, "OverriddenWeapons.jsonc");
        File.WriteAllText(legacyPath, """
        {
            "Override": { "weapon_legacy_id": "BoltActionSniperRifle" }
        }
        """);
        File.WriteAllText(Path.Combine(mqwDir, "QuestOverrides.jsonc"), """
        {
            "version": 1,
            "BlackListedQuests": ["q_a"]
        }
        """);
        var before = Directory.GetFiles(mqwDir)
            .ToDictionary(path => path, File.ReadAllBytes);

        var settings = new OverrideReader(
            new InMemoryModDirectoryProvider([modDir]), persistMigrations: false).Read();

        settings.ManualTypeOverrides.Should().ContainKey("weapon_legacy_id");
        settings.ExcludedQuests.Should().Contain("q_a");
        var after = Directory.GetFiles(mqwDir)
            .ToDictionary(path => path, File.ReadAllBytes);
        after.Keys.Should().BeEquivalentTo(before.Keys);
        foreach (var (path, bytes) in before)
        {
            after[path].Should().Equal(bytes, $"{path} must not be rewritten");
        }
    }

    [Fact]
    public void Read_binds_the_given_config()
    {
        var config = new ModConfig { Debug = true, ModBundleCartesianCap = 7 };

        var settings = new OverrideReader(new InMemoryModDirectoryProvider([MakeModDir()])).Read(config);

        settings.Config.Should().BeSameAs(config);
    }

    // ── Per-file failure isolation ───────────────────────────────────────────

    [Fact]
    public void Broken_json_in_one_mod_is_skipped_with_warning_and_other_mods_still_apply()
    {
        var good = MakeModDir(weaponOverridesJsonc: """
        { "version": 2, "manualTypeOverrides": { "w_good": "AK" } }
        """);
        var bad = MakeModDir(weaponOverridesJsonc: "{ \"manualTypeOverrides\": { \"w_bad\": ");
        var badPath = Path.Combine(bad, "MissingQuestWeapons", "WeaponOverrides.jsonc");
        var logger = new CapturingModLogger();

        var result = new OverrideReader(new InMemoryModDirectoryProvider([bad, good]), logger).Read();

        result.ManualTypeOverrides.Should().ContainKey("w_good");
        result.ManualTypeOverrides.Should().NotContainKey("w_bad");
        logger.Warnings.Should().ContainSingle(w => w.Contains(badPath));
        result.FileErrors.Should().ContainSingle(e => e.Path == badPath && e.Message.Length > 0);
    }

    [Fact]
    public void Misspelled_enum_value_is_skipped_with_warning_and_other_mods_still_apply()
    {
        var good = MakeModDir(questOverridesJsonc: """
        { "version": 3, "excludedQuests": ["q_good"] }
        """);
        var bad = MakeModDir(questOverridesJsonc: """
        { "version": 3, "overrideBehaviour": "MERG", "excludedQuests": ["q_bad"] }
        """);
        var badPath = Path.Combine(bad, "MissingQuestWeapons", "QuestOverrides.jsonc");
        var logger = new CapturingModLogger();

        var result = new OverrideReader(new InMemoryModDirectoryProvider([bad, good]), logger).Read();

        result.ExcludedQuests.Should().BeEquivalentTo(["q_good"]);
        logger.Warnings.Should().ContainSingle(w => w.Contains(badPath));
    }

    [Fact]
    public void Bad_file_does_not_block_good_files_in_the_same_folder()
    {
        var mod = MakeModDir(
            questOverridesJsonc: "{ not json",
            weaponOverridesJsonc: """{ "version": 2, "manualTypeOverrides": { "w1": "AK" } }""",
            attachmentOverridesJsonc: """{ "version": 2, "manualAttachmentTypeOverrides": { "a1": "Scope" } }""");
        var logger = new CapturingModLogger();

        var result = new OverrideReader(new InMemoryModDirectoryProvider([mod]), logger).Read();

        result.ManualTypeOverrides.Should().ContainKey("w1");
        result.ManualAttachmentTypeOverrides.Should().ContainKey("a1");
        logger.Warnings.Should().ContainSingle(w => w.Contains("QuestOverrides.jsonc"));
        result.FileErrors.Should().ContainSingle();
    }

    [Fact]
    public void Bad_attachment_file_is_skipped_with_warning()
    {
        var mod = MakeModDir(attachmentOverridesJsonc: "[1,");
        var logger = new CapturingModLogger();

        var result = new OverrideReader(new InMemoryModDirectoryProvider([mod]), logger).Read();

        logger.Warnings.Should().ContainSingle(w => w.Contains("AttachmentOverrides.jsonc"));
        result.FileErrors.Should().ContainSingle();
    }

    [Fact]
    public void Valid_files_produce_no_file_errors()
    {
        var mod = MakeModDir(weaponOverridesJsonc: """{ "version": 2 }""");
        var logger = new CapturingModLogger();

        var result = new OverrideReader(new InMemoryModDirectoryProvider([mod]), logger).Read();

        result.FileErrors.Should().BeEmpty();
        logger.Warnings.Should().BeEmpty();
    }
}
