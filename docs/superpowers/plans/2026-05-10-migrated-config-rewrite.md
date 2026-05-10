# Migrated Config Rewrite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When `OverrideReader` migrates a JSONC config file, persist the migrated content to disk under the canonical filename and back up the original verbatim as `<original>.v<originalVersion>.bak`. Skip-with-warn on same-version backup collisions. Idempotent for already-current files.

**Architecture:** Plumb `OriginalVersion` and `WasMigrated` through `MigrationResult` and `LoadResult`. Add a `MigrationWriter` static helper that (a) renames source→`.v<N>.bak` and writes canonical when source≠canonical, or (b) copies in-place to `.v<N>.bak` and overwrites canonical when source==canonical. `OverrideReader.ApplyQuestOverrides` and `ApplyWeaponOverrides` invoke the writer after each load. Attachment loading is unchanged (no migrations defined).

**Tech Stack:** .NET 9, xUnit + FluentAssertions. Mutates `Config/ConfigMigrator.cs`, `Config/ConfigLoader.cs`, `Pipeline/Override/OverrideReader.cs`. Adds `Config/MigrationWriter.cs` and `AddMissingQuestRequirements.Tests/Config/MigrationWriterTests.cs`.

**Spec:** `docs/superpowers/specs/2026-05-10-migrated-config-rewrite-design.md`

---

### Task 1: Plumb OriginalVersion and WasMigrated through ConfigMigrator and ConfigLoader

**Goal:** Callers of `ConfigLoader.LoadFromFile` / `LoadFromString` receive `MigratedJson`, `OriginalVersion`, and `WasMigrated` alongside the deserialized config.

**Files:**
- Modify: `AddMissingQuestRequirements/Config/ConfigMigrator.cs`
- Modify: `AddMissingQuestRequirements/Config/ConfigLoader.cs`
- Modify: `AddMissingQuestRequirements.Tests/Config/ConfigLoaderTests.cs` (extend with new fields, plus a new `WasMigrated` test)

**Acceptance Criteria:**
- [ ] `MigrationResult` is a `record` with `JsonObject Json`, `int OriginalVersion`, `bool WasMigrated`, `IReadOnlyList<string> Warnings`.
- [ ] `LoadResult<T>` is a `record` with `T Config`, `JsonObject MigratedJson`, `int OriginalVersion`, `bool WasMigrated`, `IReadOnlyList<string> Warnings`.
- [ ] `ConfigMigrator.Migrate` sets `WasMigrated = OriginalVersion < currentVersion` AND no newer-than-supported warning was emitted. (Newer-than-supported = `WasMigrated` false, file used as-is.)
- [ ] When the file does not exist, `ConfigLoader.LoadFromFile` returns `LoadResult` with `WasMigrated = false`, `OriginalVersion = currentVersion`, empty `MigratedJson`.
- [ ] Existing tests still compile and pass (the new fields are additive on records).

**Verify:** `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~ConfigLoaderTests|FullyQualifiedName~ConfigMigratorTests" -c Release` → all green. Then `dotnet test --filter "FullyQualifiedName!~Integration" -c Release` to confirm no regressions across the whole unit suite.

**Steps:**

- [ ] **Step 1: Update existing test usages to consume the new fields, then add a new failing test for `WasMigrated`.**

The existing tests reference `loaded.Config` and `loaded.Warnings` only. After the record gets two new positional members, existing usage keeps compiling because `LoadResult<T>` exposes `Config`, `Warnings`, and the new fields don't change the property names. (We are using `record` shorthand with positional members, but property access via `Config` / `Warnings` still works.)

Append a new failing test to `AddMissingQuestRequirements.Tests/Config/ConfigLoaderTests.cs`:

```csharp
    [Fact]
    public void LoadFromString_v0_file_reports_WasMigrated_true_and_OriginalVersion_zero()
    {
        var jsonc = "{ \"BlackListedQuests\": [\"q\"] }"; // no version key → v0
        var loaded = ConfigLoader.LoadFromString<Models.QuestOverridesFile>(
            jsonc,
            currentVersion: 2,
            migrations: [Migrations.v0_to_v1, Migrations.v1_to_v2_Quest]);

        loaded.WasMigrated.Should().BeTrue();
        loaded.OriginalVersion.Should().Be(0);
        loaded.MigratedJson["version"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void LoadFromString_already_current_reports_WasMigrated_false()
    {
        var jsonc = "{ \"version\": 2, \"excludedQuests\": [\"q\"] }";
        var loaded = ConfigLoader.LoadFromString<Models.QuestOverridesFile>(
            jsonc,
            currentVersion: 2,
            migrations: [Migrations.v0_to_v1, Migrations.v1_to_v2_Quest]);

        loaded.WasMigrated.Should().BeFalse();
        loaded.OriginalVersion.Should().Be(2);
    }

    [Fact]
    public void LoadFromString_newer_than_supported_reports_WasMigrated_false()
    {
        var jsonc = "{ \"version\": 99 }";
        var loaded = ConfigLoader.LoadFromString<Models.QuestOverridesFile>(
            jsonc,
            currentVersion: 2,
            migrations: [Migrations.v0_to_v1, Migrations.v1_to_v2_Quest]);

        loaded.WasMigrated.Should().BeFalse();
        loaded.OriginalVersion.Should().Be(99);
        loaded.Warnings.Should().NotBeEmpty();
    }
```

- [ ] **Step 2: Run the new tests; expect compilation failure (the new properties don't exist yet).**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~ConfigLoaderTests" -c Release`

Expected: build fails with "no `WasMigrated` / `OriginalVersion` / `MigratedJson` on `LoadResult`". This is the red.

- [ ] **Step 3: Update `MigrationResult` in `AddMissingQuestRequirements/Config/ConfigMigrator.cs`.**

Replace:

```csharp
public sealed record MigrationResult(JsonObject Json, IReadOnlyList<string> Warnings);
```

With:

```csharp
public sealed record MigrationResult(
    JsonObject Json,
    int OriginalVersion,
    bool WasMigrated,
    IReadOnlyList<string> Warnings);
```

Update the `Migrate` method body. The current end of the method is:

```csharp
        json["version"] = currentVersion;
        return new MigrationResult(json, warnings);
    }
```

Replace the relevant region of the method to compute `OriginalVersion` and `WasMigrated`. The new method body should look like this — replace the existing implementation in full so the warning paths stay correct:

```csharp
    public static MigrationResult Migrate(
        JsonObject json,
        int currentVersion,
        Func<JsonObject, JsonObject>[] migrations)
    {
        var warnings = new List<string>();

        var fileVersion = json["version"]?.GetValue<int>() ?? 0;

        if (fileVersion > currentVersion)
        {
            warnings.Add(
                $"Config version {fileVersion} is newer than the mod's expected version {currentVersion}. " +
                "Some settings may be ignored.");
            return new MigrationResult(json, fileVersion, false, warnings);
        }

        for (var from = fileVersion; from < currentVersion; from++)
        {
            if (from < migrations.Length)
            {
                json = migrations[from](json);
            }
        }

        json["version"] = currentVersion;

        var wasMigrated = fileVersion < currentVersion;
        return new MigrationResult(json, fileVersion, wasMigrated, warnings);
    }
```

- [ ] **Step 4: Update `LoadResult<T>` in `AddMissingQuestRequirements/Config/ConfigLoader.cs`.**

Replace:

```csharp
public sealed record LoadResult<T>(T Config, IReadOnlyList<string> Warnings);
```

With:

```csharp
public sealed record LoadResult<T>(
    T Config,
    JsonObject MigratedJson,
    int OriginalVersion,
    bool WasMigrated,
    IReadOnlyList<string> Warnings);
```

Update `LoadFromString` and `LoadFromFile` to populate the new fields. The new bodies:

```csharp
    public static LoadResult<T> LoadFromString<T>(
        string jsonc,
        int currentVersion,
        Func<JsonObject, JsonObject>[] migrations)
        where T : IVersionedConfig, new()
    {
        var node = JsonNode.Parse(jsonc, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var obj = node?.AsObject() ?? new JsonObject();
        var migrated = ConfigMigrator.Migrate(obj, currentVersion, migrations);
        var config = migrated.Json.Deserialize<T>(JsoncReader.DefaultOptions) ?? new T();
        return new LoadResult<T>(
            config, migrated.Json, migrated.OriginalVersion, migrated.WasMigrated, migrated.Warnings);
    }

    public static LoadResult<T> LoadFromFile<T>(
        string path,
        int currentVersion,
        Func<JsonObject, JsonObject>[] migrations)
        where T : IVersionedConfig, new()
    {
        if (!File.Exists(path))
        {
            return new LoadResult<T>(new T(), new JsonObject(), currentVersion, false, []);
        }

        var content = File.ReadAllText(path);
        return LoadFromString<T>(content, currentVersion, migrations);
    }
```

- [ ] **Step 5: Run the new ConfigLoader tests; expect green.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~ConfigLoaderTests" -c Release`

Expected: PASS for the three new tests, no regressions in the existing ones.

- [ ] **Step 6: Run the full unit suite to confirm no callers broke.**

Run: `dotnet test --filter "FullyQualifiedName!~Integration" -c Release`

Expected: all green. The `OverrideReader` callsites continue to use `loaded.Config` only — the new fields are additive.

- [ ] **Step 7: Commit.**

```bash
git add AddMissingQuestRequirements/Config/ConfigMigrator.cs \
        AddMissingQuestRequirements/Config/ConfigLoader.cs \
        AddMissingQuestRequirements.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat(config): expose OriginalVersion and WasMigrated on load result"
```

---

### Task 2: Add MigrationWriter helper

**Goal:** A `Config/MigrationWriter.Persist(...)` helper that backs up the source file to `<source>.v<N>.bak` and writes the migrated JSON to the canonical path. Idempotent. Skip-with-warn on same-version backup collisions. Catches I/O errors.

**Files:**
- Create: `AddMissingQuestRequirements/Config/MigrationWriter.cs`
- Create: `AddMissingQuestRequirements.Tests/Config/MigrationWriterTests.cs`

**Acceptance Criteria:**
- [ ] `MigrationWriter.Persist(string sourcePath, string canonicalPath, int originalVersion, JsonObject migratedJson, IModLogger? logger = null)` is a `public static` method.
- [ ] When `<sourcePath>.v<N>.bak` does not exist:
  - `sourcePath != canonicalPath`: `File.Move(sourcePath, backupPath)` + write `canonicalPath`.
  - `sourcePath == canonicalPath`: `File.Copy(sourcePath, backupPath, overwrite:false)` + overwrite `sourcePath`.
- [ ] When `<sourcePath>.v<N>.bak` exists: log Warning, do NOT touch any file.
- [ ] JSON output is `WriteIndented = true`.
- [ ] `IOException` and `UnauthorizedAccessException` are caught, logged at Warning, and swallowed.
- [ ] Logger is optional (null = no log output, no crash).

**Verify:** `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~MigrationWriterTests" -c Release` → all green.

**Steps:**

- [ ] **Step 1: Write the failing tests.**

Create `AddMissingQuestRequirements.Tests/Config/MigrationWriterTests.cs`:

```csharp
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
        logger.Warnings.Should().ContainMatch("*conflicts with existing backup*");
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
        var logger = new CapturingModLogger();
        // Source path that cannot exist: an illegal-on-Windows but we use a path
        // pointing to a directory that is itself the source — File.Copy will throw.
        var dir = MakeTempDir();
        var sourceDir = Path.Combine(dir, "is_a_directory");
        Directory.CreateDirectory(sourceDir);

        var migrated = new JsonObject { ["version"] = 2 };

        // Calling Persist with a directory path triggers IOException on File.Move/File.Copy.
        // The helper must catch it and log; never throw.
        Action act = () => MigrationWriter.Persist(
            sourceDir, sourceDir, originalVersion: 0, migrated, logger);

        act.Should().NotThrow();
        logger.Warnings.Should().NotBeEmpty();
    }
}
```

`CapturingModLogger` lives at `AddMissingQuestRequirements/Util/CapturingModLogger.cs` and has `IReadOnlyList<string> Warnings { get; }` — verify this matches the existing surface before relying on it; if the surface is different (e.g. `WarningMessages`), adapt the assertion.

- [ ] **Step 2: Run the new tests; confirm compilation failure (`MigrationWriter` doesn't exist yet).**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~MigrationWriterTests" -c Release`

Expected: build fails. This is the red.

- [ ] **Step 3: Implement `MigrationWriter`.**

Create `AddMissingQuestRequirements/Config/MigrationWriter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Config;

/// <summary>
/// Persists a migrated JSONC config back to disk. The original file is preserved
/// verbatim as <c>&lt;source&gt;.v&lt;originalVersion&gt;.bak</c>; the migrated
/// content is written to the canonical filename. If the versioned backup already
/// exists (i.e. the source is at the same version that was previously backed up),
/// the helper logs a warning and does nothing — refusing to clobber existing
/// backups. I/O errors are caught and logged; the helper never throws.
/// </summary>
public static class MigrationWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    public static void Persist(
        string sourcePath,
        string canonicalPath,
        int originalVersion,
        JsonObject migratedJson,
        IModLogger? logger = null)
    {
        var backupPath = $"{sourcePath}.v{originalVersion}.bak";

        if (File.Exists(backupPath))
        {
            logger?.Warning(
                $"Migrated config rewrite skipped: '{sourcePath}' at v{originalVersion} " +
                $"conflicts with existing backup '{backupPath}'. " +
                "Remove the backup or the source manually if you intend to re-migrate.");
            return;
        }

        try
        {
            if (string.Equals(sourcePath, canonicalPath, StringComparison.Ordinal))
            {
                File.Copy(sourcePath, backupPath, overwrite: false);
            }
            else
            {
                File.Move(sourcePath, backupPath);
            }

            var json = JsonSerializer.Serialize(migratedJson, WriteOptions);
            File.WriteAllText(canonicalPath, json);

            logger?.Info(
                $"Migrated config: backed up '{sourcePath}' → '{backupPath}', " +
                $"wrote '{canonicalPath}'.");
        }
        catch (IOException ex)
        {
            logger?.Warning(
                $"Failed to persist migrated config '{canonicalPath}': {ex.Message}. " +
                "In-memory load still applied.");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.Warning(
                $"Failed to persist migrated config '{canonicalPath}': {ex.Message}. " +
                "In-memory load still applied.");
        }
    }
}
```

- [ ] **Step 4: Run the new tests; expect green.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~MigrationWriterTests" -c Release`

Expected: all five PASS. If `Persist_swallows_IOException_and_logs_warning` fails because `File.Move` on a directory path silently succeeds on the runner's filesystem, replace the test setup with a path under a directory marked read-only via `new DirectoryInfo(...).Attributes |= FileAttributes.ReadOnly` or a non-existent source path with `sourcePath != canonicalPath` (`File.Move` throws `FileNotFoundException` which derives from `IOException`).

- [ ] **Step 5: Run the full unit suite.**

Run: `dotnet test --filter "FullyQualifiedName!~Integration" -c Release`

Expected: all green.

- [ ] **Step 6: Commit.**

```bash
git add AddMissingQuestRequirements/Config/MigrationWriter.cs \
        AddMissingQuestRequirements.Tests/Config/MigrationWriterTests.cs
git commit -m "feat(config): MigrationWriter persists migrated JSONC with versioned backup"
```

---

### Task 3: Wire MigrationWriter into OverrideReader

**Goal:** After `ApplyQuestOverrides` and `ApplyWeaponOverrides` load and migrate a file, invoke `MigrationWriter.Persist` to mirror the result to disk. Attachment loading is unchanged.

**Files:**
- Modify: `AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs` (call `Persist` from the two relevant Apply* methods)
- Modify: `AddMissingQuestRequirements.Tests/Pipeline/Override/OverrideReaderTests.cs` (extend with three new `[Fact]` tests asserting the on-disk side effect)

**Acceptance Criteria:**
- [ ] `ApplyQuestOverrides` calls `MigrationWriter.Persist(path, path, loaded.OriginalVersion, loaded.MigratedJson, logger)` after the load.
- [ ] `ApplyWeaponOverrides` calls `MigrationWriter.Persist(path, canonicalPath, loaded.OriginalVersion, loaded.MigratedJson, logger)` after the load, where `canonicalPath = Path.Combine(mqwDir, WeaponOverridesFile)`.
- [ ] When a legacy `OverriddenWeapons.jsonc` is loaded and migrated, on disk the legacy file is renamed to `OverriddenWeapons.jsonc.v0.bak` and a new `WeaponOverrides.jsonc` is written.
- [ ] When a quest file is at v1 and `CurrentVersion` is 2, on disk a `QuestOverrides.jsonc.v1.bak` appears next to the now-v2 `QuestOverrides.jsonc`.
- [ ] When a file is already at `CurrentVersion`, no `.bak` is written and the file is byte-identical to its prior state.
- [ ] All previously passing tests continue to pass.

**Verify:** `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~OverrideReaderTests" -c Release` → all green.

**Steps:**

- [ ] **Step 1: Write the three failing tests.**

Append to `AddMissingQuestRequirements.Tests/Pipeline/Override/OverrideReaderTests.cs` inside the existing class (before the closing brace):

```csharp
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

        // In-memory result remains correct.
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
        rewritten.Should().Contain("\"version\": 2");
        rewritten.Should().Contain("excludedQuests");
        rewritten.Should().Contain("includedWeapons");
    }
```

- [ ] **Step 2: Run the new tests; confirm two FAIL.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~OverrideReaderTests.Legacy_OverriddenWeapons_is_renamed_to_v0_bak|FullyQualifiedName~OverrideReaderTests.Already_current_WeaponOverrides_is_not_rewritten|FullyQualifiedName~OverrideReaderTests.Quest_v1_file_is_backed_up_as_v1_bak" -c Release`

Expected: `Legacy_OverriddenWeapons_...` and `Quest_v1_file_...` FAIL (no rewrite happens). `Already_current_WeaponOverrides_...` PASSES already (nothing was rewriting it).

- [ ] **Step 3: Wire `MigrationWriter.Persist` into `OverrideReader`.**

In `AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs`:

After the `ConfigLoader.LoadFromFile` call inside `ApplyQuestOverrides`, add the persist call. The current method is around the lines:

```csharp
        var path = Path.Combine(mqwDir, QuestOverridesFile);
        Trace($"QuestOverrides path='{path}' exists={File.Exists(path)}");
        var loaded = ConfigLoader.LoadFromFile<Models.QuestOverridesFile>(
            path, CurrentVersion, QuestMigrations);
```

Right after the `loaded` variable is in scope (between the trace `QuestOverrides loaded behaviour=...` block and the merge calls), insert:

```csharp
        if (loaded.WasMigrated)
        {
            MigrationWriter.Persist(
                sourcePath: path,
                canonicalPath: path,
                originalVersion: loaded.OriginalVersion,
                migratedJson: loaded.MigratedJson,
                logger: logger);
        }
```

In `ApplyWeaponOverrides`, after the legacy fallback resolves `path` and after `loaded` is in scope, insert:

```csharp
        if (loaded.WasMigrated)
        {
            var canonicalPath = Path.Combine(mqwDir, WeaponOverridesFile);
            MigrationWriter.Persist(
                sourcePath: path,
                canonicalPath: canonicalPath,
                originalVersion: loaded.OriginalVersion,
                migratedJson: loaded.MigratedJson,
                logger: logger);
        }
```

`ApplyAttachmentOverrides` is unchanged — `AttachmentMigrations` is empty, so `WasMigrated` is always false; calling `Persist` would be a no-op anyway, but to keep the intent explicit we omit the call.

- [ ] **Step 4: Re-run the new tests; expect all three PASS.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~OverrideReaderTests.Legacy_OverriddenWeapons_is_renamed_to_v0_bak|FullyQualifiedName~OverrideReaderTests.Already_current_WeaponOverrides_is_not_rewritten|FullyQualifiedName~OverrideReaderTests.Quest_v1_file_is_backed_up_as_v1_bak" -c Release`

Expected: all three PASS.

- [ ] **Step 5: Run the full `OverrideReaderTests` suite.**

Run: `dotnet test AddMissingQuestRequirements.Tests --filter "FullyQualifiedName~OverrideReaderTests" -c Release`

Expected: all green. Notable: the existing `Legacy_OverriddenWeapons_filename_is_loaded_when_new_name_absent` test from the previous PR will now leave a `.v0.bak` in its temp dir, which is fine because `_tempDirs` cleanup nukes the whole directory. Verify no new failures.

- [ ] **Step 6: Run the full unit suite.**

Run: `dotnet test --filter "FullyQualifiedName!~Integration" -c Release`

Expected: all green.

- [ ] **Step 7: Commit.**

```bash
git add AddMissingQuestRequirements/Pipeline/Override/OverrideReader.cs \
        AddMissingQuestRequirements.Tests/Pipeline/Override/OverrideReaderTests.cs
git commit -m "feat(override): persist migrated configs to disk with versioned backups"
```

---

## Self-review

- **Spec coverage.** Spec §1–§4 (`MigrationResult`, `LoadResult`, `MigrationWriter`, `OverrideReader` invocation) → Tasks 1, 2, 3. Spec §"Edge cases" → backup-collision test in Task 2, IOException test in Task 2, already-current test in Task 3, legacy-rename test in Task 3.
- **Placeholders.** All steps contain literal code, paths, commands. No "TBD".
- **Type consistency.** `MigrationResult(JsonObject Json, int OriginalVersion, bool WasMigrated, IReadOnlyList<string> Warnings)` matches the `LoadResult<T>` constructor call site in `LoadFromString`. The `OriginalVersion` field is consumed by `OverrideReader` via `loaded.OriginalVersion`. `MigratedJson` consumed via `loaded.MigratedJson`. `WasMigrated` consumed in both Apply* methods. `MigrationWriter.Persist` signature matches all four call sites in tests + production.
- **Logger surface.** `IModLogger.Info / Warning` exist (`Util/IModLogger.cs`). `CapturingModLogger.Warnings` should exist; verify before relying on the test assertion. If the property name differs, adjust the assertion in Task 2 Step 1.
