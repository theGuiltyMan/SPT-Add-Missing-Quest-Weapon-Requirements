using System.Globalization;

namespace AddMissingQuestRequirements.Util;

/// <summary>
/// <see cref="IModLogger"/> that writes every level to a single file with a UTC
/// timestamp and level prefix. Truncates the file on construction so each server
/// start produces one clean log. I/O failures are silently swallowed — logging
/// must never break the pipeline.
/// </summary>
public sealed class FileModLogger : IModLogger
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileModLogger(string path)
    {
        _path = path;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, string.Empty);
        }
        catch
        {
            // Ignored — subsequent writes will no-op if the target remains unusable.
        }
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Success(string message)
    {
        Write("SUCCESS", message);
    }

    public void Warning(string message)
    {
        Write("WARNING", message);
    }

    public void Debug(string message)
    {
        Write("DEBUG", message);
    }

    private void Write(string level, string message)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line);
            }
            catch
            {
                // Ignored — see constructor rationale.
            }
        }
    }
}
