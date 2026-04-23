namespace AddMissingQuestRequirements.Util;

/// <summary>
/// Fan-out logger. Info / Success / Warning write to both sinks; Debug writes
/// to the file sink only. Debug lines are high-volume and would flood the SPT
/// console — the file sink is wrapped with <see cref="DebugFilteringModLogger"/>
/// by the loader, so users who set <c>config.Debug = true</c> get the full
/// trace on disk while the console stays readable.
/// </summary>
public sealed class TeeModLogger(IModLogger console, IModLogger file) : IModLogger
{
    private readonly IModLogger _console = console;
    private readonly IModLogger _file = file;

    public void Info(string message)
    {
        _console.Info(message);
        _file.Info(message);
    }

    public void Success(string message)
    {
        _console.Success(message);
        _file.Success(message);
    }

    public void Warning(string message)
    {
        _console.Warning(message);
        _file.Warning(message);
    }

    public void Debug(string message)
    {
        _file.Debug(message);
    }
}
