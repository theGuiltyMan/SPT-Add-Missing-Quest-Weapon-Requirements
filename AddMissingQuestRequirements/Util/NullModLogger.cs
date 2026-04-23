namespace AddMissingQuestRequirements.Util;

/// <summary>
/// No-op logger. Use <see cref="Instance"/> in tests that do not assert on log output.
/// </summary>
public sealed class NullModLogger : IModLogger
{
    public static readonly NullModLogger Instance = new();

    private NullModLogger() { }

    public void Info(string message)
    {
    }

    public void Success(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Debug(string message)
    {
    }
}
