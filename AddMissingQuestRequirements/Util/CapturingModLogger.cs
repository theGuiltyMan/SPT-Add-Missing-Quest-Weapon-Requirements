namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Logger that stores all messages in typed lists.
/// Use in tests that need to assert on log output (warnings, summaries, etc.).
/// </summary>
public class CapturingModLogger : IModLogger
{
    public List<string> Infos { get; } = [];
    public List<string> Successes { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Debugs { get; } = [];

    public void Info(string message)
    {
        Infos.Add(message);
    }

    public void Success(string message)
    {
        Successes.Add(message);
    }

    public void Warning(string message)
    {
        Warnings.Add(message);
    }

    public void Debug(string message)
    {
        Debugs.Add(message);
    }
}
