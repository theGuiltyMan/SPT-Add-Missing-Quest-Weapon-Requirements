using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Config;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Config;

public class MigrationWriterTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    private string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
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

    [Fact]
    public void Persist_renames_source_to_versioned_backup_and_writes_canonical()
    {
        var dir = MakeTempDir();
        var source = Path.Combine(dir, "OverriddenWeapons.jsonc");
        var canonical = Path.Combine(dir, "WeaponOverrides.jsonc");
        File.WriteAllText(source, "{ \"original\": 1 }");

        var migrated = new JsonObject { ["version"] = 2, ["migrated"] = true };

        MigrationWriter.Persist(source, canonical, originalVersion: 0, migrated, logger: null);

        File.Exists(source).Should().BeFalse();
        File.Exists(source + ".v0.bak").Should().BeTrue();
        File.ReadAllText(source + ".v0.bak").Should().Be("{ \"original\": 1 }");
        File.Exists(canonical).Should().BeTrue();
        File.ReadAllText(canonical).Should().Contain("\"migrated\": true");
    }

    [Fact]
    public void Persist_in_place_copies_to_versioned_backup_and_overwrites_canonical()
    {
        var dir = MakeTempDir();
        var path = Path.Combine(dir, "QuestOverrides.jsonc");
        File.WriteAllText(path, "{ \"original\": 1 }");

        var migrated = new JsonObject { ["version"] = 2, ["migrated"] = true };

        MigrationWriter.Persist(path, path, originalVersion: 1, migrated, logger: null);

        File.Exists(path).Should().BeTrue();
        File.Exists(path + ".v1.bak").Should().BeTrue();
        File.ReadAllText(path + ".v1.bak").Should().Be("{ \"original\": 1 }");
        File.ReadAllText(path).Should().Contain("\"migrated\": true");
    }

    [Fact]
    public void Persist_skips_when_versioned_backup_already_exists()
    {
        var dir = MakeTempDir();
        var source = Path.Combine(dir, "OverriddenWeapons.jsonc");
        var canonical = Path.Combine(dir, "WeaponOverrides.jsonc");
        File.WriteAllText(source, "{ \"current\": true }");
        File.WriteAllText(source + ".v0.bak", "{ \"prior_backup\": true }");

        var migrated = new JsonObject { ["version"] = 2, ["migrated"] = true };
        var logger = new CapturingModLogger();

        MigrationWriter.Persist(source, canonical, originalVersion: 0, migrated, logger);

        File.Exists(source).Should().BeTrue();
        File.ReadAllText(source).Should().Be("{ \"current\": true }");
        File.ReadAllText(source + ".v0.bak").Should().Be("{ \"prior_backup\": true }");
        File.Exists(canonical).Should().BeFalse();
        logger.Warnings.Should().Contain(w => w.Contains("conflicts with existing backup"));
    }

    [Fact]
    public void Persist_writes_indented_json()
    {
        var dir = MakeTempDir();
        var path = Path.Combine(dir, "QuestOverrides.jsonc");
        File.WriteAllText(path, "{}");

        var migrated = new JsonObject
        {
            ["version"] = 2,
            ["overrides"] = new JsonArray { new JsonObject { ["id"] = "q1" } },
        };

        MigrationWriter.Persist(path, path, originalVersion: 0, migrated, logger: null);

        var written = File.ReadAllText(path);
        written.Should().Contain("\n");
        written.Should().Contain("  \"version\": 2");
    }

    [Fact]
    public void Persist_swallows_IOException_and_logs_warning()
    {
        var dir = MakeTempDir();
        // Use a non-existent source with source != canonical → File.Move throws
        // FileNotFoundException, which is an IOException subclass.
        var source = Path.Combine(dir, "does_not_exist.jsonc");
        var canonical = Path.Combine(dir, "canonical.jsonc");

        var migrated = new JsonObject { ["version"] = 2 };
        var logger = new CapturingModLogger();

        Action act = () => MigrationWriter.Persist(
            source, canonical, originalVersion: 0, migrated, logger);

        act.Should().NotThrow();
        logger.Warnings.Should().NotBeEmpty();
        File.Exists(canonical).Should().BeFalse();
    }

    [Fact]
    public void Persist_in_place_failure_to_create_backup_does_not_lose_canonical()
    {
        var dir = MakeTempDir();
        var path = Path.Combine(dir, "QuestOverrides.jsonc");
        File.WriteAllText(path, "{ \"original\": 1 }");
        // Pre-create the backup destination so File.Copy(..., overwrite:false) throws.
        File.WriteAllText(path + ".v0.bak", "{ \"prior\": true }");

        var migrated = new JsonObject { ["version"] = 2, ["migrated"] = true };
        var logger = new CapturingModLogger();

        Action act = () => MigrationWriter.Persist(
            path, path, originalVersion: 0, migrated, logger);

        act.Should().NotThrow();
        // Backup-exists guard fires first → no file changed.
        File.ReadAllText(path).Should().Be("{ \"original\": 1 }");
        File.ReadAllText(path + ".v0.bak").Should().Be("{ \"prior\": true }");
        File.Exists(path + ".tmp").Should().BeFalse();
        logger.Warnings.Should().Contain(w => w.Contains("conflicts with existing backup"));
    }

    [Fact]
    public void Persist_cross_path_failure_after_temp_write_cleans_up_tmp()
    {
        // Source absent, canonical writable. The temp write succeeds, then
        // File.Move(source, backup) throws FileNotFoundException. Temp must be cleaned.
        var dir = MakeTempDir();
        var source = Path.Combine(dir, "OverriddenWeapons.jsonc"); // does not exist
        var canonical = Path.Combine(dir, "WeaponOverrides.jsonc");

        var migrated = new JsonObject { ["version"] = 2 };
        var logger = new CapturingModLogger();

        Action act = () => MigrationWriter.Persist(
            source, canonical, originalVersion: 0, migrated, logger);

        act.Should().NotThrow();
        File.Exists(canonical).Should().BeFalse();
        File.Exists(canonical + ".tmp").Should().BeFalse("tmp must be cleaned up after failure");
        logger.Warnings.Should().NotBeEmpty();
    }
}
