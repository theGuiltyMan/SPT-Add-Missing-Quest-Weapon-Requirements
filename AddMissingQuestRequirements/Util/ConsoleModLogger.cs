namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Routes log messages directly to stdout/stderr with no buffering.
/// </summary>
public sealed class ConsoleModLogger : IModLogger
{
    public static readonly ConsoleModLogger Instance = new();

    public bool EnableDebug { get; set; } = false;

    private ConsoleModLogger() { }

    public void Info(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }

    public void Success(string message)
    {
        Console.WriteLine($"[OK]   {message}");
    }

    public void Warning(string message)
    {
        Console.Error.WriteLine($"[WARN] {message}");
    }

    public void Debug(string message)
    {
        if (EnableDebug)
        {
            Console.WriteLine($"[DBG]  {message}");
        }
    }
}
