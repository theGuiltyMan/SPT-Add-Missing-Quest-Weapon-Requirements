# Migrated config rewrite — design

Date: 2026-05-10
Branch: `feature/quest-config-migration-fix`

## Problem

When `OverrideReader` loads a mod's `QuestOverrides.jsonc` /
`WeaponOverrides.jsonc` / `OverriddenWeapons.jsonc`, it migrates the JSON
in-memory through the version chain in `Migrations.cs` and discards the
result on every server launch. Two consequences:

1. The user has no visible record that a migration happened. Legacy patches
   keep their original on-disk shape forever; future debugging has to
   reconstruct what the migrator produced.
2. The legacy weapon-overrides filename (`OverriddenWeapons.jsonc`) keeps
   shadowing the canonical `WeaponOverrides.jsonc`. The fallback added in
   the previous spec works, but the file never moves to the new name —
   distributing a fixed mod still requires the user to rename by hand.

We want the loader to write the migrated content back to disk under the
canonical filename, and to preserve the original verbatim (including
comments) as a versioned backup so users can roll back or audit.

## Goals

- After the first launch following an upgrade, the on-disk file matches the
  in-memory migrated content and uses the canonical filename.
- The original pre-migration file is preserved verbatim at
  `<original-filename>.v<originalVersion>.bak`. Comments and whitespace are
  not lost — the .bak file is a byte-for-byte copy via `File.Move` /
  `File.Copy`.
- Multiple migrations across releases coexist: a v0→v3 migration leaves
  `QuestOverrides.jsonc.v0.bak`; a later v3→v4 migration adds
  `QuestOverrides.jsonc.v3.bak`.
- A same-version `.bak` collision (i.e. the source on disk is again at v\<N\>
  and `<file>.v<N>.bak` already exists) is treated as an ambiguous user
  state. The loader logs a warning and skips the disk write — in-memory
  loading still proceeds.
- Read-only filesystems do not crash the mod. I/O errors are caught,
  logged, and the loader continues with the in-memory result.
- Already-current files (e.g. shipped configs at `version: 2`) are
  untouched. Idempotent: nothing happens on subsequent launches once a
  file is at `currentVersion`.

Non-goals:

- Comment / whitespace preservation in the written canonical file. The
  `.bak` is the comment-preserving safety net. The new canonical file is
  serialized via `System.Text.Json` with `WriteIndented = true` and is
  intentionally machine-shaped.
  - **Note (2026-05-10):** a follow-up Newtonsoft swap was attempted to
    preserve comments through migration. Empirical result: Newtonsoft 13's
    `JObject.Parse` with `JsonLoadSettings { CommentHandling = Load }`
    preserves comments only inside arrays; comments between object
    properties are silently dropped. The legacy `OverriddenWeapons.jsonc`
    patches put their comments inside the `Override: { ... }` object, so
    the swap delivered zero benefit for the actual user-visible files. The
    Newtonsoft commits were reverted. Real comment preservation needs a
    token-walking reinjection layer (`JsonTextReader` to capture
    `(comment, next-property-name)` pairs, post-process indented output).
    Tracked as future work.
- Any change to the migration chain itself.
- Rolling backups beyond one-per-source-version. If `<file>.v<N>.bak`
  already exists, we do not produce `.v<N>.bak.1` etc. — collision = skip.
- Touching attachment overrides. `AttachmentMigrations` is empty, so
  `WasMigrated` will always be false there; no rewrite logic is wired.

## Mechanics

### 1. `MigrationResult` carries provenance

`Config/ConfigMigrator.cs`:

```csharp
public sealed record MigrationResult(
    JsonObject Json,
    int OriginalVersion,
    bool WasMigrated,
    IReadOnlyList<string> Warnings);
```

`Migrate` sets:

- `OriginalVersion` = `json["version"]?.GetValue<int>() ?? 0` (the value
  read from the file, before stamping).
- `WasMigrated` = `OriginalVersion < currentVersion` AND no
  newer-than-supported warning was raised. (If the file is newer than the
  binary, we don't rewrite — the user is on a downgraded mod.)

### 2. `LoadResult<T>` exposes provenance

`Config/ConfigLoader.cs`:

```csharp
public sealed record LoadResult<T>(
    T Config,
    JsonObject MigratedJson,
    int OriginalVersion,
    bool WasMigrated,
    IReadOnlyList<string> Warnings);
```

`LoadFromFile` and `LoadFromString` populate the new fields from
`MigrationResult`. When the file does not exist, the result is
`new T()` with `WasMigrated = false`, `OriginalVersion = currentVersion`,
empty `MigratedJson`.

### 3. `MigrationWriter` helper

New file `Config/MigrationWriter.cs`:

```csharp
public static class MigrationWriter
{
    public static void Persist(
        string sourcePath,
        string canonicalPath,
        int originalVersion,
        JsonObject migratedJson,
        IModLogger? logger = null);
}
```

Algorithm:

1. Compute `backupPath = sourcePath + ".v" + originalVersion + ".bak"`.
2. If `File.Exists(backupPath)`: log warning
   `"<sourcePath> at v<N> conflicts with existing backup <backupPath> — skipping rewrite"`
   and return. The mod's in-memory state is already correct; we just
   refuse to touch disk.
3. Otherwise:
   - `sourcePath != canonicalPath` (filename rename, e.g.
     `OverriddenWeapons.jsonc` → `WeaponOverrides.jsonc`):
     `File.Move(sourcePath, backupPath)` (preserves bytes including
     comments). Then write `migratedJson` to `canonicalPath`.
   - `sourcePath == canonicalPath` (in-place migration, e.g. quest file
     v1→v2): `File.Copy(sourcePath, backupPath, overwrite: false)` then
     `File.WriteAllText(sourcePath, json)`. The copy preserves the
     original bytes; the write replaces in place.
4. JSON serialization uses
   `JsonSerializer.Serialize(migratedJson, new JsonSerializerOptions { WriteIndented = true })`.
5. Wraps the whole thing in `try { ... } catch (IOException) / catch
   (UnauthorizedAccessException)` → log warning, return. Never throw.

### 4. `OverrideReader` invokes the writer

After each successful load:

```csharp
MigrationWriter.Persist(
    sourcePath: path,                            // resolved (legacy or canonical)
    canonicalPath: Path.Combine(mqwDir, WeaponOverridesFile), // always the new name
    originalVersion: loaded.OriginalVersion,
    migratedJson: loaded.MigratedJson,
    logger: logger);
```

For quest overrides, `sourcePath == canonicalPath == Path.Combine(mqwDir, QuestOverridesFile)`.
Attachment overrides skip the call entirely (no migrations defined →
`WasMigrated` is always false anyway, but skipping is cheaper than a
no-op call).

### 5. Trace logs

The Debug-gated traces from the previous commit stay. `MigrationWriter`
adds two more:

- `"backed up <sourcePath> to <backupPath>"` (success path).
- `"wrote migrated <canonicalPath>"` (success path).

Failure paths log via `Warning` (visible without Debug).

## Edge cases

- **Same-version `.bak` collision.** Source-on-disk at v\<N\> coexists with
  `<source>.v<N>.bak`. Skip + warn. User should investigate (likely they
  manually restored the .bak over the source); we will not silently
  overwrite a backup that's already there.
- **Legacy filename rewrite, second run with stale legacy.** First run
  produced `OverriddenWeapons.jsonc.v0.bak` and `WeaponOverrides.jsonc`.
  Second run finds both `OverriddenWeapons.jsonc` (re-created somehow)
  AND the .bak. The fallback in `OverrideReader` already prefers the new
  name, so source resolves to `WeaponOverrides.jsonc` (now at
  `currentVersion`) → `WasMigrated` false → `Persist` not called. The
  re-created legacy file is untouched on disk. No data loss; the user
  can investigate manually.
- **Read-only `user/mods/<mod>/MissingQuestWeapons/` directory.** Catch
  `UnauthorizedAccessException`, log warning, continue. In-memory load
  still applies.
- **Newer-than-binary file.** `MigrationResult.WasMigrated` is false (we
  use the file as-is per existing behaviour). Persist is not called.
- **Empty file / invalid JSON.** Already handled upstream by
  `ConfigLoader` (deserialize falls back to `new T()` with empty
  `MigratedJson`). `WasMigrated` is false because `OriginalVersion =
  currentVersion` short-circuits the migration loop. Persist is not
  called.

## Testing

Unit tests for `MigrationWriter` (new fixture
`AddMissingQuestRequirements.Tests/Config/MigrationWriterTests.cs`):

- `Persist_renames_source_to_versioned_backup_and_writes_canonical`
- `Persist_in_place_copies_to_versioned_backup_and_overwrites_canonical`
- `Persist_skips_when_versioned_backup_already_exists`
- `Persist_swallows_IOException_and_returns`
- `Persist_writes_indented_json`

Integration tests for `OverrideReader` (extend existing fixture):

- `Legacy_OverriddenWeapons_is_renamed_to_v0_bak_and_canonical_is_written`
- `Already_current_WeaponOverrides_is_not_rewritten`
- `Quest_v1_file_is_backed_up_as_v1_bak_and_overwritten_with_v2`

`ConfigLoader` / `ConfigMigrator` unit tests gain coverage for the new
`OriginalVersion` and `WasMigrated` fields.

## Risk and rollback

- All changes are additive at the type level
  (`MigrationResult`/`LoadResult` gain fields, callers ignore them by
  default). Existing callers that read `result.Config` / `result.Warnings`
  keep compiling.
- The disk side-effect is gated behind `WasMigrated` — already-current
  files are untouched. Rolling back is `cp <file>.v<N>.bak <file>` and
  removing the `.bak`.
- The `.bak` collision rule is conservative: the loader refuses to
  destroy any existing backup. The worst-case failure mode is "did
  nothing", not "lost data".
