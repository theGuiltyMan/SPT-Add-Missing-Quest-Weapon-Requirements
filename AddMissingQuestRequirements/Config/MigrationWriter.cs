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
        var tmpPath = $"{canonicalPath}.tmp";

        if (File.Exists(backupPath))
        {
            logger?.Warning(
                $"Migrated config rewrite skipped: '{sourcePath}' at v{originalVersion} " +
                $"conflicts with existing backup '{backupPath}'. " +
                "Remove the backup or the source manually if you intend to re-migrate.");
            return;
        }

        var tmpExists = false;
        try
        {
            var json = JsonSerializer.Serialize(migratedJson, WriteOptions);
            File.WriteAllText(tmpPath, json);
            tmpExists = true;

            if (string.Equals(sourcePath, canonicalPath, StringComparison.Ordinal))
            {
                File.Copy(sourcePath, backupPath, overwrite: false);
            }
            else
            {
                File.Move(sourcePath, backupPath);
            }

            File.Move(tmpPath, canonicalPath, overwrite: true);
            tmpExists = false;

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
        finally
        {
            if (tmpExists && File.Exists(tmpPath))
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
